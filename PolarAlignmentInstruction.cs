using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Utility.Notification;
using NINA.Sequencer.SequenceItem;
using NINA.Profile.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.PlateSolving;
using NINA.PlateSolving.Interfaces;
using NINA.Core.Model.Equipment;
using NINA.Astrometry;
using NINA.Equipment.Model;
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

        [Import(typeof(IPlateSolverFactory))]
        private IPlateSolverFactory plateSolverFactory { get; set; }

        [Import(typeof(IImagingMediator))]
        private IImagingMediator imagingMediator { get; set; }

        [Import(typeof(IFilterWheelMediator))]
        private IFilterWheelMediator filterWheelMediator { get; set; }

        private bool hasExecutedBefore = false;
        private Coordinates homePosition;
        private double recordedAlt = 0;
        private double recordedAz = 0;
        private bool hasRecordedPosition = false;

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

            // ==================== PHASE B: Initial Positioning & Verification ====================
            bool isSimulation = (cameraMediator.GetInfo()?.Name?.Contains("Simulator", StringComparison.OrdinalIgnoreCase) ?? false) ||
                                (telescopeMediator.GetInfo()?.Name?.Contains("Simulator", StringComparison.OrdinalIgnoreCase) ?? false);

            if (isSimulation) {
                progress?.Report(new ApplicationStatus { Status = "2-Point Polar Alignment: Simulator detected! Running in Simulation Mode." });
                global::NINA.Core.Utility.Logger.Info("2-Point Polar Alignment: Simulator detected! Running in Simulation Mode.");
            }

            progress?.Report(new ApplicationStatus { Status = "2-Point Polar Alignment: Initiating Phase B (Initial Positioning & Verification)..." });
            global::NINA.Core.Utility.Logger.Info("2-Point Polar Alignment: Initiating Phase B (Initial Positioning & Verification)");

            var currentPosition = telescopeMediator.GetCurrentPosition();
            if (currentPosition == null) {
                throw new InvalidOperationException("Could not retrieve current telescope position.");
            }

            if (Method == RotationMethod.Automatic) {
                if (!hasExecutedBefore) {
                    bool isNearPole = Math.Abs(Math.Abs(currentPosition.Dec) - 90.0) < 1.0;
                    if (!isNearPole) {
                        string errMsg = "Telescope is not in the home position. Please move the telescope to the home position first.";
                        progress?.Report(new ApplicationStatus { Status = $"2-Point Polar Alignment Error: {errMsg}" });
                        global::NINA.Core.Utility.Logger.Info($"2-Point Polar Alignment Error: {errMsg}");
                        Notification.ShowError($"2-Point Polar Alignment Error: {errMsg}");
                        throw new InvalidOperationException(errMsg);
                    }

                    homePosition = new Coordinates(currentPosition.RA, currentPosition.Dec, currentPosition.Epoch, Coordinates.RAType.Hours);
                    hasExecutedBefore = true;
                    global::NINA.Core.Utility.Logger.Info($"First run verified at Home position (RA: {homePosition.RAString}, Dec: {homePosition.DecString}). Reference state initialized.");
                } else {
                    var info = telescopeMediator.GetInfo();
                    double currentAlt = info?.Altitude ?? 0;
                    double currentAz = info?.Azimuth ?? 0;

                    bool isValidRetry = hasRecordedPosition && 
                                        Math.Abs(currentAlt - recordedAlt) < 0.25 && 
                                        Math.Abs(currentAz - recordedAz) < 0.25;

                    if (!isValidRetry) {
                        string errMsg = "Telescope has been moved from its recorded position. Please move the telescope to the home position and start again.";
                        progress?.Report(new ApplicationStatus { Status = $"2-Point Polar Alignment Error: {errMsg}" });
                        global::NINA.Core.Utility.Logger.Info($"2-Point Polar Alignment Error: {errMsg}");
                        Notification.ShowError($"2-Point Polar Alignment Error: {errMsg}");
                        hasExecutedBefore = false;
                        hasRecordedPosition = false;
                        throw new InvalidOperationException(errMsg);
                    }

                    global::NINA.Core.Utility.Logger.Info($"Valid retry detected (Alt: {currentAlt:F2}°, Az: {currentAz:F2}° matches recorded Alt: {recordedAlt:F2}°, Az: {recordedAz:F2}°).");
                    if (homePosition != null) {
                        progress?.Report(new ApplicationStatus { Status = $"2-Point Polar Alignment: Subsequent retry run detected. Automatically slewing back to verified Home Position (RA: {homePosition.RAString}, Dec: {homePosition.DecString})..." });
                        global::NINA.Core.Utility.Logger.Info($"2-Point Polar Alignment: Subsequent retry run detected. Automatically slewing back to verified Home Position (RA: {homePosition.RAString}, Dec: {homePosition.DecString})...");
                        await telescopeMediator.SlewToCoordinatesAsync(homePosition, token);
                        progress?.Report(new ApplicationStatus { Status = "2-Point Polar Alignment: Successfully returned to Home Position." });
                        global::NINA.Core.Utility.Logger.Info("2-Point Polar Alignment: Successfully returned to Home Position");
                        currentPosition = homePosition;
                    }
                }
            }

            // 1. Mount Slewing (Pre-rotation if requested)
            if (StartingPoint == StartingPointMode.PreRotateHalfRange) {
                double offsetDegrees = RotationAmount / 2.0;
                double offsetHours = offsetDegrees / 15.0;
                progress?.Report(new ApplicationStatus { Status = $"2-Point Polar Alignment: Pre-rotating RA by {offsetDegrees:F1}° in the opposite direction of {Direction}..." });
                global::NINA.Core.Utility.Logger.Info($"2-Point Polar Alignment: Pre-rotating RA by {offsetDegrees:F1} degrees in opposite direction of {Direction}");

                double targetRA = currentPosition.RA + (Direction == RotationDirection.East ? -offsetHours : offsetHours);
                if (targetRA < 0) targetRA += 24.0;
                if (targetRA >= 24.0) targetRA -= 24.0;

                Coordinates targetCoords = new Coordinates(targetRA, currentPosition.Dec, currentPosition.Epoch, Coordinates.RAType.Hours);
                await telescopeMediator.SlewToCoordinatesAsync(targetCoords, token);
                progress?.Report(new ApplicationStatus { Status = "2-Point Polar Alignment: Pre-rotation completed successfully." });
                global::NINA.Core.Utility.Logger.Info("2-Point Polar Alignment: Pre-rotation completed successfully");
            } else {
                progress?.Report(new ApplicationStatus { Status = "2-Point Polar Alignment: Starting at current/home position." });
                global::NINA.Core.Utility.Logger.Info("2-Point Polar Alignment: Starting at current/home position");
            }

            // Record target Alt/Az position for valid retry check
            if (Method == RotationMethod.Automatic) {
                var info = telescopeMediator.GetInfo();
                recordedAlt = info?.Altitude ?? 0;
                recordedAz = info?.Azimuth ?? 0;
                hasRecordedPosition = true;
                global::NINA.Core.Utility.Logger.Info($"Recorded target position Alt: {recordedAlt:F2}°, Az: {recordedAz:F2}° for subsequent retry verification.");
            }

            // 2. Camera Exposure and Plate Solving
            progress?.Report(new ApplicationStatus { Status = "2-Point Polar Alignment: Capturing starting image and solving..." });
            global::NINA.Core.Utility.Logger.Info("2-Point Polar Alignment: Capturing starting image and solving");

            int binVal = 1;
            if (!string.IsNullOrEmpty(Binning) && Binning.Length >= 1) {
                int.TryParse(Binning.Substring(0, 1), out binVal);
            }

            CaptureSequence sequence = new CaptureSequence {
                ExposureTime = ExposureTime,
                ImageType = "LIGHT",
                Gain = Gain,
                Binning = new BinningMode((short)binVal, (short)binVal),
                FilterType = new FilterInfo { Name = Filter },
                Offset = Offset,
                Enabled = true,
                TotalExposureCount = 1
            };

            PlateSolveResult result;
            if (isSimulation) {
                progress?.Report(new ApplicationStatus { Status = "2-Point Polar Alignment [Simulator]: Simulating 3s camera exposure and plate solve..." });
                await Task.Delay(3000, token);
                var simPosition = telescopeMediator.GetCurrentPosition() ?? new Coordinates(12.0, 45.0, Epoch.JNOW, Coordinates.RAType.Hours);
                result = new PlateSolveResult {
                    Success = true,
                    Coordinates = new Coordinates(simPosition.RA, simPosition.Dec, simPosition.Epoch, Coordinates.RAType.Hours)
                };
            } else {
                var profile = profileService.ActiveProfile;
                var plateSolveSettings = profile.GetType().GetProperty("PlateSolveSettings")?.GetValue(profile) as IPlateSolveSettings;
                if (plateSolveSettings == null) {
                    throw new InvalidOperationException("Could not retrieve active plate solve settings.");
                }

                IPlateSolver solver = plateSolverFactory.GetPlateSolver(plateSolveSettings);
                IPlateSolver blindSolver = plateSolverFactory.GetBlindSolver(plateSolveSettings);
                ICaptureSolver captureSolver = plateSolverFactory.GetCaptureSolver(solver, blindSolver, imagingMediator, filterWheelMediator);

                CaptureSolverParameter solverParam = new CaptureSolverParameter {
                    Attempts = 1,
                    ReattemptDelay = TimeSpan.FromSeconds(2),
                    FocalLength = profile.TelescopeSettings.FocalLength,
                    PixelSize = cameraMediator.GetInfo()?.PixelSize ?? 0,
                    Binning = binVal,
                    DisableNotifications = true
                };

                var solveProgress = new Progress<PlateSolveProgress>();
                var appProgress = new Progress<ApplicationStatus>();

                result = await captureSolver.Solve(sequence, solverParam, solveProgress, appProgress, token);
            }

            if (result == null || !result.Success) {
                Notification.ShowError("2-Point Polar Alignment Error: Initial plate solve failed!");
                throw new InvalidOperationException("Initial plate solve failed. Ensure exposure settings are correct and the sky is clear.");
            }

            progress?.Report(new ApplicationStatus { Status = $"2-Point Polar Alignment: Solved initial coordinates: RA {result.Coordinates.RAString}, Dec {result.Coordinates.DecString}" });
            global::NINA.Core.Utility.Logger.Info($"2-Point Polar Alignment: Solved initial coordinates successfully: RA={result.Coordinates.RAString}, Dec={result.Coordinates.DecString}");
            Notification.ShowSuccess("2-Point Polar Alignment: Phase B (Initial Positioning & Verification) completed successfully!");
        }

        public override object Clone() {
            return new PolarAlignmentInstruction(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(PolarAlignmentInstruction)}";
        }
    }
}
