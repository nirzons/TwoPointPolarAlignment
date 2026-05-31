using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.Equipment.Interfaces.Mediator;

namespace NirZonshine.NINA.TwoPointPolarAlignment {

    [Export(typeof(ISequenceItem))]
    [ExportMetadata("Name", "2-Point Polar Alignment")]
    [ExportMetadata("Description", "Automatically runs the 2-point polar alignment routine.")]
    [ExportMetadata("Icon", "TwoPointPolarAlignmentSVG")]
    [ExportMetadata("Category", "Polar Alignment")]
    [JsonObject(MemberSerialization.OptIn)]
    public class TwoPointPolarAlignmentSequenceItem : SequenceItem, IValidatable {

        public TwoPointPolarAlignmentSequenceItem() {
            Name = "2-Point Polar Alignment";
            Description = "Automatically runs the 2-point polar alignment routine.";
            Category = "Polar Alignment";
            
            try {
                if (System.Windows.Application.Current != null) {
                    var resource = System.Windows.Application.Current.TryFindResource("TwoPointPolarAlignmentSVG");
                    if (resource is System.Windows.Media.GeometryGroup geoGroup) {
                        Icon = geoGroup;
                    } else {
                        var group = new System.Windows.Media.GeometryGroup();
                        group.Children.Add(System.Windows.Media.Geometry.Parse("M3,12 A9,9 0 0,1 12,3"));
                        group.Children.Add(System.Windows.Media.Geometry.Parse("M3,9.5 L4.2,11.8 L6.8,11.8 L4.7,13.3 L5.5,15.6 L3,14.1 L0.5,15.6 L1.3,13.3 L-0.8,11.8 L1.8,11.8 Z"));
                        group.Children.Add(System.Windows.Media.Geometry.Parse("M12,0.5 L13.2,2.8 L15.8,2.8 L13.7,4.3 L14.5,6.6 L12,5.1 L9.5,6.6 L10.3,4.3 L8.2,2.8 L10.8,2.8 Z"));
                        group.Freeze();
                        if (!System.Windows.Application.Current.Resources.Contains("TwoPointPolarAlignmentSVG")) {
                            System.Windows.Application.Current.Resources.Add("TwoPointPolarAlignmentSVG", group);
                        }
                        Icon = group;
                    }
                }
            } catch {
                // Fallback to TelescopeSVG
                try {
                    if (System.Windows.Application.Current != null) {
                        var resource = System.Windows.Application.Current.TryFindResource("TelescopeSVG");
                        if (resource is System.Windows.Media.GeometryGroup geoGroup) {
                            Icon = geoGroup;
                        }
                    }
                } catch {}
            }
        }

        [Import]
        public PolarAlignmentDockableVM PolarAlignmentDockableVM { get; set; }

        [Import]
        public ICameraMediator CameraMediator { get; set; }

        [Import]
        public ITelescopeMediator TelescopeMediator { get; set; }

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged(nameof(Issues));
            }
        }

        public bool Validate() {
            var newIssues = new List<string>();

            var cameraConnected = false;
            var mountConnected = false;

            if (CameraMediator != null) {
                cameraConnected = CameraMediator.GetInfo()?.Connected ?? false;
            } else if (PolarAlignmentDockableVM.Instance != null) {
                cameraConnected = PolarAlignmentDockableVM.Instance.IsCameraConnected;
            }

            if (TelescopeMediator != null) {
                mountConnected = TelescopeMediator.GetInfo()?.Connected ?? false;
            } else if (PolarAlignmentDockableVM.Instance != null) {
                mountConnected = PolarAlignmentDockableVM.Instance.IsMountConnected;
            }

            var isManual = false;
            var vm = PolarAlignmentDockableVM ?? PolarAlignmentDockableVM.Instance;
            if (vm != null) {
                isManual = (vm.Method == RotationMethod.Manual);
            }

            if (!cameraConnected) {
                newIssues.Add("Please connect the camera.");
            }

            if (!isManual && !mountConnected) {
                newIssues.Add("Please connect the telescope mount.");
            }

            Issues = newIssues;

            return Issues.Count == 0;
        }

        private double exposureTime = 2.0;
        [JsonProperty]
        public double ExposureTime {
            get => exposureTime;
            set {
                if (exposureTime != value) {
                    exposureTime = value;
                    RaisePropertyChanged(nameof(ExposureTime));
                }
            }
        }

        private int gain = 0;
        [JsonProperty]
        public int Gain {
            get => gain;
            set {
                if (gain != value) {
                    gain = value;
                    RaisePropertyChanged(nameof(Gain));
                }
            }
        }

        private double rotationAmount = 90.0;
        [JsonProperty]
        public double RotationAmount {
            get => rotationAmount;
            set {
                if (rotationAmount != value) {
                    rotationAmount = value;
                    RaisePropertyChanged(nameof(RotationAmount));
                }
            }
        }

        private string filter = "(Current)";
        [JsonProperty]
        public string Filter {
            get => filter;
            set {
                if (filter != value) {
                    filter = value;
                    RaisePropertyChanged(nameof(Filter));
                }
            }
        }

        private RotationMethod method = RotationMethod.Automatic;
        [JsonProperty]
        public RotationMethod Method {
            get => method;
            set {
                if (method != value) {
                    method = value;
                    RaisePropertyChanged(nameof(Method));
                }
            }
        }

        private RotationDirection direction = RotationDirection.East;
        [JsonProperty]
        public RotationDirection Direction {
            get => direction;
            set {
                if (direction != value) {
                    direction = value;
                    RaisePropertyChanged(nameof(Direction));
                }
            }
        }

        private StartingPointMode startingPoint = StartingPointMode.PreRotateHalfRange;
        [JsonProperty]
        public StartingPointMode StartingPoint {
            get => startingPoint;
            set {
                if (startingPoint != value) {
                    startingPoint = value;
                    RaisePropertyChanged(nameof(StartingPoint));
                }
            }
        }

        private string binning = "1x1";
        [JsonProperty]
        public string Binning {
            get => binning;
            set {
                if (binning != value) {
                    binning = value;
                    RaisePropertyChanged(nameof(Binning));
                }
            }
        }

        private int offset = 0;
        [JsonProperty]
        public int Offset {
            get => offset;
            set {
                if (offset != value) {
                    offset = value;
                    RaisePropertyChanged(nameof(Offset));
                }
            }
        }

        private int plateSolveRetries = 5;
        [JsonProperty]
        public int PlateSolveRetries {
            get => plateSolveRetries;
            set {
                if (plateSolveRetries != value) {
                    plateSolveRetries = value;
                    RaisePropertyChanged(nameof(PlateSolveRetries));
                }
            }
        }

        private bool enableOnePointAlignment = true;
        [JsonProperty]
        public bool EnableOnePointAlignment {
            get => enableOnePointAlignment;
            set {
                if (enableOnePointAlignment != value) {
                    enableOnePointAlignment = value;
                    RaisePropertyChanged(nameof(EnableOnePointAlignment));
                }
            }
        }

        private ExposuresPerPoint exposuresPerPoint = ExposuresPerPoint.Single;
        [JsonProperty]
        public ExposuresPerPoint ExposuresPerPoint {
            get => exposuresPerPoint;
            set {
                if (exposuresPerPoint != value) {
                    exposuresPerPoint = value;
                    RaisePropertyChanged(nameof(ExposuresPerPoint));
                }
            }
        }

        private TaskCompletionSource<bool> resumeTcs;

        public void Resume() {
            resumeTcs?.TrySetResult(true);
        }

        public override async Task Execute(IProgress<global::NINA.Core.Model.ApplicationStatus> progress, CancellationToken token) {
            var vm = PolarAlignmentDockableVM ?? PolarAlignmentDockableVM.Instance;
            if (vm == null) {
                throw new InvalidOperationException("Polar Alignment Dockable View Model is not loaded. Please make sure the 2-Point Polar Alignment plugin tab is active in the Imaging screen.");
            }

            // Lockout checks for Camera and Mount
            if (!vm.IsCameraConnected) {
                throw new InvalidOperationException("2-Point Polar Alignment failed: Camera is not connected!");
            }
            if (vm.Method != RotationMethod.Manual && !vm.IsMountConnected) {
                throw new InvalidOperationException("2-Point Polar Alignment failed: Telescope mount is not connected!");
            }

            progress.Report(new global::NINA.Core.Model.ApplicationStatus { Status = "Executing 2-Point Polar Alignment Sequence Item" });
            resumeTcs = new TaskCompletionSource<bool>();

            // Setup sequence item as the active running sequencer item in the VM
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                vm.RunningSequenceItem = this;
                // Apply overrides
                vm.SettingsManager.SetOverrides(
                    ExposureTime,
                    Gain,
                    RotationAmount,
                    Filter,
                    Method,
                    Direction,
                    StartingPoint,
                    Binning,
                    Offset,
                    PlateSolveRetries,
                    EnableOnePointAlignment,
                    ExposuresPerPoint
                );
                
                // Automatically launch the first alignment run!
                if (vm.CanStart) {
                    vm.StartAlignment();
                }
            });

            try {
                // Await until either the user clicks Resume Sequence (resumeTcs completes)
                // OR the user cancels the sequence in N.I.N.A. (token is canceled)
                using (token.Register(() => resumeTcs.TrySetCanceled(token))) {
                    await resumeTcs.Task;
                }
            }
            finally {
                // Clean up overrides and state
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                    // Make sure we stop any active internal alignment workflow run when sequence item finishes or cancels
                    if (vm.IsRunning) {
                        vm.StopAlignment();
                    }
                    vm.SettingsManager.ClearOverrides();
                    vm.RunningSequenceItem = null;
                });
            }
        }

        public override object Clone() {
            return new TwoPointPolarAlignmentSequenceItem {
                Name = this.Name,
                Description = this.Description,
                Icon = this.Icon,
                Category = this.Category,
                ExposureTime = this.ExposureTime,
                Gain = this.Gain,
                RotationAmount = this.RotationAmount,
                Filter = this.Filter,
                Method = this.Method,
                Direction = this.Direction,
                StartingPoint = this.StartingPoint,
                Binning = this.Binning,
                Offset = this.Offset,
                PlateSolveRetries = this.PlateSolveRetries,
                EnableOnePointAlignment = this.EnableOnePointAlignment,
                ExposuresPerPoint = this.ExposuresPerPoint,
                PolarAlignmentDockableVM = this.PolarAlignmentDockableVM,
                CameraMediator = this.CameraMediator,
                TelescopeMediator = this.TelescopeMediator
            };
        }
    }
}
