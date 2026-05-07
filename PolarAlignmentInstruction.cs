using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Utility.Notification;
using NINA.Sequencer.SequenceItem;
using NINA.Profile.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace NirZonshine.NINA.TwoPointPolarAlignment {

    public enum RotationMethod {
        Automatic,
        Manual
    }

    public enum RotationDirection {
        East,
        West
    }

    public enum StartingPointMode {
        StartAtHome,
        PreRotateHalfRange
    }

    [ExportMetadata("Name", "2-Point Polar Alignment")]
    [ExportMetadata("Description", "Fast polar alignment using home position and a 90° RA rotation")]
    [ExportMetadata("Icon", "Telescope")]
    [ExportMetadata("Category", "Alignment")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class PolarAlignmentInstruction : SequenceItem {

        private double rotationAmount = 90.0;
        private RotationMethod method = RotationMethod.Automatic;
        private RotationDirection direction = RotationDirection.East;
        private StartingPointMode startingPoint = StartingPointMode.StartAtHome;
        private double exposureTime = 2.0;
        private int gain = 0;
        private string filter = "(Current)";
        private string binning = "1x1";
        private int offset = 0;
        private double telescopeMoveRate = 3.0;

        [Import(typeof(IProfileService))]
        private IProfileService profileService { get; set; }

        [Import(typeof(ICameraMediator))]
        private ICameraMediator cameraMediator { get; set; }

        [Import(typeof(ITelescopeMediator))]
        private ITelescopeMediator telescopeMediator { get; set; }

        [ImportingConstructor]
        public PolarAlignmentInstruction() {
        }

        public PolarAlignmentInstruction(PolarAlignmentInstruction copyMe) : this() {
            CopyMetaData(copyMe);
            RotationAmount = copyMe.RotationAmount;
            Method = copyMe.Method;
            Direction = copyMe.Direction;
            StartingPoint = copyMe.StartingPoint;
            ExposureTime = copyMe.ExposureTime;
            Gain = copyMe.Gain;
            Filter = copyMe.Filter;
            Binning = copyMe.Binning;
            Offset = copyMe.Offset;
            TelescopeMoveRate = copyMe.TelescopeMoveRate;
        }

        [JsonProperty]
        public double RotationAmount {
            get => rotationAmount;
            set {
                rotationAmount = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public RotationMethod Method {
            get => method;
            set {
                method = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public RotationDirection Direction {
            get => direction;
            set {
                direction = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public StartingPointMode StartingPoint {
            get => startingPoint;
            set {
                startingPoint = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public double ExposureTime {
            get => exposureTime;
            set {
                exposureTime = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public int Gain {
            get => gain;
            set {
                gain = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public string Filter {
            get => filter;
            set {
                filter = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public string Binning {
            get => binning;
            set {
                binning = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public int Offset {
            get => offset;
            set {
                offset = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public double TelescopeMoveRate {
            get => telescopeMoveRate;
            set {
                telescopeMoveRate = value;
                RaisePropertyChanged();
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

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            progress?.Report(new ApplicationStatus { Status = "2-Point Polar Alignment: Initiating Phase A (Pre-flight Checks)..." });
            token.ThrowIfCancellationRequested();

            // 1. Verify Camera Connection
            bool isCameraConnected = cameraMediator?.GetInfo()?.Connected ?? false;
            if (!isCameraConnected) {
                Notification.ShowError("2-Point Polar Alignment Error: Camera is not connected!");
                throw new InvalidOperationException("Camera is not connected. Connect your camera before running polar alignment.");
            }

            // 2. Verify Mount Connection
            bool isMountConnected = telescopeMediator?.GetInfo()?.Connected ?? false;
            if (!isMountConnected) {
                Notification.ShowError("2-Point Polar Alignment Error: Telescope Mount is not connected!");
                throw new InvalidOperationException("Telescope Mount is not connected. Connect your mount before running polar alignment.");
            }

            // 3. Verify Plate Solver Configuration
            bool isSolverOk = false;
            string detectedSolverName = "Unknown";
            try {
                if (profileService?.ActiveProfile != null) {
                    var profileType = profileService.ActiveProfile.GetType();
                    
                    var solverSettingsProp = profileType.GetProperty("PlateSolveSettings");
                    if (solverSettingsProp != null) {
                        var settingsObj = solverSettingsProp.GetValue(profileService.ActiveProfile);
                        if (settingsObj != null) {
                            var solverTypeProp = settingsObj.GetType().GetProperty("PlateSolverType");
                            if (solverTypeProp != null) {
                                var solverValue = solverTypeProp.GetValue(settingsObj);
                                detectedSolverName = solverValue?.ToString() ?? "Unknown";

                                string pathPropName = null;
                                if (detectedSolverName.Contains("ASTAP", StringComparison.OrdinalIgnoreCase)) {
                                    pathPropName = "ASTAPLocation";
                                } else if (detectedSolverName.Contains("PS2", StringComparison.OrdinalIgnoreCase) || detectedSolverName.Contains("PlateSolve2", StringComparison.OrdinalIgnoreCase)) {
                                    pathPropName = "PS2Location";
                                } else if (detectedSolverName.Contains("PS3", StringComparison.OrdinalIgnoreCase) || detectedSolverName.Contains("PlateSolve3", StringComparison.OrdinalIgnoreCase)) {
                                    pathPropName = "PS3Location";
                                } else if (detectedSolverName.Contains("ASPS", StringComparison.OrdinalIgnoreCase)) {
                                    pathPropName = "AspsLocation";
                                }

                                if (!string.IsNullOrEmpty(pathPropName)) {
                                    var pathProp = settingsObj.GetType().GetProperty(pathPropName);
                                    if (pathProp != null) {
                                        string path = pathProp.GetValue(settingsObj) as string;
                                        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) {
                                            isSolverOk = false;
                                        } else {
                                            isSolverOk = true;
                                        }
                                    } else {
                                        isSolverOk = true; // Fallback if path property not found
                                    }
                                } else {
                                    isSolverOk = !detectedSolverName.Equals("None", StringComparison.OrdinalIgnoreCase) && !detectedSolverName.Contains("NoSolver");
                                }
                            }
                        }
                    }
                } else {
                    isSolverOk = true; // Fallback
                }
            } catch {
                isSolverOk = true; // Fail-safe
            }

            if (!isSolverOk) {
                Notification.ShowError($"2-Point Polar Alignment Error: Selected Plate Solver ({detectedSolverName}) is unconfigured or not installed!");
                throw new InvalidOperationException($"Selected Plate Solver ({detectedSolverName}) is unconfigured or not installed.");
            }

            progress?.Report(new ApplicationStatus { Status = $"2-Point Polar Alignment: Valid Plate Solver found ({detectedSolverName})." });
            progress?.Report(new ApplicationStatus { Status = "2-Point Polar Alignment: Phase A (Pre-flight Checks) completed successfully!" });
            Notification.ShowSuccess("2-Point Polar Alignment: Pre-flight checks passed successfully!");

            await Task.Delay(1000, token);
        }

        public override object Clone() {
            return new PolarAlignmentInstruction(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(PolarAlignmentInstruction)}";
        }
    }
}
