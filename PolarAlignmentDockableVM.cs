using System;
using NINA.WPF.Base.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Core.Model.Equipment;
using NINA.Equipment.Equipment.MyCamera;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Windows.Media;

namespace NirZonshine.NINA.TwoPointPolarAlignment {

    [Export(typeof(global::NINA.Equipment.Interfaces.ViewModel.IDockableVM))]
    public class PolarAlignmentDockableVM : DockableVM, ICameraConsumer {

        private double rotationAmount = 90.0;
        private RotationMethod method = RotationMethod.Automatic;
        private RotationDirection direction = RotationDirection.East;
        private StartingPointMode startingPoint = StartingPointMode.StartAtHome;
        private double exposureTime = 2.0;
        private int gain = 0;
        private ImageSource lastFrame;
        private string filter = "(Current)";
        private string binning = "1x1";
        private int offset = 0;
        private double telescopeMoveRate = 3.0;

        private readonly ICameraMediator cameraMediator;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly System.Windows.Threading.DispatcherTimer statusTimer;
        private bool lastIsCameraConnected;
        private bool lastIsMountConnected;

        [ImportingConstructor]
        public PolarAlignmentDockableVM(IProfileService profileService, ICameraMediator cameraMediator, ITelescopeMediator telescopeMediator) : base(profileService) {
            this.cameraMediator = cameraMediator;
            this.telescopeMediator = telescopeMediator;
            Title = "2-Point Polar Alignment";

            // Initialize connection states
            lastIsCameraConnected = IsCameraConnected;
            lastIsMountConnected = IsMountConnected;

            // Start a lightweight 1-second status polling timer as a bulletproof mechanism for equipment connection changes
            statusTimer = new System.Windows.Threading.DispatcherTimer {
                Interval = TimeSpan.FromSeconds(1)
            };
            statusTimer.Tick += StatusTimer_Tick;
            statusTimer.Start();
            
            // Set the custom 2-Point Polar Alignment icon (simplified arc and two stars for maximum clarity at 16x16 resolution)
            var group = new GeometryGroup();
            
            // 1. Curved tracking arc
            group.Children.Add(Geometry.Parse("M3,12 A9,9 0 0,1 12,3"));
            
            // 2. Left Star (9 o'clock)
            group.Children.Add(Geometry.Parse("M3,9.5 L4.2,11.8 L6.8,11.8 L4.7,13.3 L5.5,15.6 L3,14.1 L0.5,15.6 L1.3,13.3 L-0.8,11.8 L1.8,11.8 Z"));
            
            // 3. Top Star (12 o'clock)
            group.Children.Add(Geometry.Parse("M12,0.5 L13.2,2.8 L15.8,2.8 L13.7,4.3 L14.5,6.6 L12,5.1 L9.5,6.6 L10.3,4.3 L8.2,2.8 L10.8,2.8 Z"));
            
            ImageGeometry = group;
        }

        public override bool IsTool => true;

        public bool IsCameraConnected => cameraMediator?.GetInfo()?.Connected ?? false;

        public bool IsMountConnected => telescopeMediator?.GetInfo()?.Connected ?? false;

        public bool CanRun => IsCameraConnected && IsMountConnected;

        private void StatusTimer_Tick(object sender, EventArgs e) {
            var currentCamera = IsCameraConnected;
            var currentMount = IsMountConnected;

            if (currentCamera != lastIsCameraConnected) {
                lastIsCameraConnected = currentCamera;
                RaisePropertyChanged(nameof(IsCameraConnected));
                RaisePropertyChanged(nameof(CanRun));
            }

            if (currentMount != lastIsMountConnected) {
                lastIsMountConnected = currentMount;
                RaisePropertyChanged(nameof(IsMountConnected));
                RaisePropertyChanged(nameof(CanRun));
            }
        }

        public void UpdateDeviceInfo(CameraInfo deviceInfo) {
            // Unused but required by ICameraConsumer
        }

        public void Dispose() {
            try {
                statusTimer?.Stop();
            } catch { }
        }

        public double RotationAmount {
            get => rotationAmount;
            set {
                rotationAmount = value;
                RaisePropertyChanged(nameof(RotationAmount));
            }
        }

        public RotationMethod Method {
            get => method;
            set {
                method = value;
                RaisePropertyChanged(nameof(Method));
            }
        }

        public RotationDirection Direction {
            get => direction;
            set {
                direction = value;
                RaisePropertyChanged(nameof(Direction));
            }
        }

        public StartingPointMode StartingPoint {
            get => startingPoint;
            set {
                startingPoint = value;
                RaisePropertyChanged(nameof(StartingPoint));
            }
        }

        public double ExposureTime {
            get => exposureTime;
            set {
                exposureTime = value;
                RaisePropertyChanged(nameof(ExposureTime));
            }
        }

        public int Gain {
            get => gain;
            set {
                gain = value;
                RaisePropertyChanged(nameof(Gain));
            }
        }

        public ImageSource LastFrame {
            get => lastFrame;
            set {
                lastFrame = value;
                RaisePropertyChanged(nameof(LastFrame));
            }
        }

        public string Filter {
            get => filter;
            set {
                filter = value;
                RaisePropertyChanged(nameof(Filter));
            }
        }

        public string Binning {
            get => binning;
            set {
                binning = value;
                RaisePropertyChanged(nameof(Binning));
            }
        }

        public int Offset {
            get => offset;
            set {
                offset = value;
                RaisePropertyChanged(nameof(Offset));
            }
        }

        public double TelescopeMoveRate {
            get => telescopeMoveRate;
            set {
                telescopeMoveRate = value;
                RaisePropertyChanged(nameof(TelescopeMoveRate));
            }
        }

        public IEnumerable<string> Filters {
            get {
                var list = new List<string> { "(Current)" };
                try {
                    if (profileService?.ActiveProfile != null) {
                        var profileType = profileService.ActiveProfile.GetType();
                        
                        // Try FilterWheelSettings.FilterWheelFilters
                        var fwSettingsProp = profileType.GetProperty("FilterWheelSettings");
                        if (fwSettingsProp != null) {
                            var fwSettings = fwSettingsProp.GetValue(profileService.ActiveProfile);
                            if (fwSettings != null) {
                                var fwFiltersProp = fwSettings.GetType().GetProperty("FilterWheelFilters");
                                if (fwFiltersProp != null) {
                                    var fwFilters = fwFiltersProp.GetValue(fwSettings);
                                    if (fwFilters is System.Collections.IEnumerable enumerable) {
                                        foreach (var item in enumerable) {
                                            var nameProp = item.GetType().GetProperty("Name");
                                            if (nameProp != null) {
                                                var name = nameProp.GetValue(item) as string;
                                                if (!string.IsNullOrEmpty(name)) {
                                                    list.Add(name);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // Fallback to legacy Filters or FilterSettings property if list has only "(Current)"
                        if (list.Count == 1) {
                            var filtersProp = profileType.GetProperty("Filters") ?? profileType.GetProperty("FilterSettings");
                            if (filtersProp != null) {
                                var filtersValue = filtersProp.GetValue(profileService.ActiveProfile);
                                if (filtersValue is System.Collections.IEnumerable enumerable) {
                                    foreach (var item in enumerable) {
                                        var nameProp = item.GetType().GetProperty("Name");
                                        if (nameProp != null) {
                                            var name = nameProp.GetValue(item) as string;
                                            if (!string.IsNullOrEmpty(name)) {
                                                list.Add(name);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch {
                    // Fail-safe suppression
                }

                if (list.Count == 1) {
                    list.AddRange(new[] { "Luminance", "Red", "Green", "Blue", "Ha", "OIII", "SII" });
                }
                return list;
            }
        }
    }
}
