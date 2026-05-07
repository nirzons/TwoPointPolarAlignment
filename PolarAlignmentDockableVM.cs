using System;
using NINA.WPF.Base.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Core.Model.Equipment;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Core.Utility.Notification;
using NINA.PlateSolving;
using NINA.PlateSolving.Interfaces;
using NINA.Astrometry;
using NINA.Core.Model;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Windows.Media;
using System.Windows.Input;

namespace NirZonshine.NINA.TwoPointPolarAlignment {

    public class RelayCommand : ICommand {
        private readonly Action<object> execute;
        private readonly Func<object, bool> canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null) {
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
            this.canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => canExecute == null || canExecute(parameter);

        public void Execute(object parameter) => execute(parameter);

        public event EventHandler CanExecuteChanged {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

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

    [Export(typeof(global::NINA.Equipment.Interfaces.ViewModel.IDockableVM))]
    public class PolarAlignmentDockableVM : DockableVM, ICameraConsumer {

        private double rotationAmount = 90.0;
        private RotationMethod method = RotationMethod.Automatic;
        private RotationDirection direction = RotationDirection.East;
        private StartingPointMode startingPoint = StartingPointMode.PreRotateHalfRange;
        private double exposureTime = 2.0;
        private int gain = 0;
        private ImageSource lastFrame;
        private string filter = "(Current)";
        private string binning = "1x1";
        private int offset = 0;
        private double telescopeMoveRate = 3.0;
        private string logs = "[System] Waiting for user interaction...";
        private ICommand startAlignmentCommand;

        private readonly ICameraMediator cameraMediator;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IPlateSolverFactory plateSolverFactory;
        private readonly IImagingMediator imagingMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly System.Windows.Threading.DispatcherTimer statusTimer;
        private bool lastIsCameraConnected;
        private bool lastIsMountConnected;
        private bool hasExecutedBefore = false;
        private Coordinates homePosition;
        private double recordedDec = 0;
        private double recordedRA = 0;
        private double recordedHA = 0;
        private bool hasRecordedPosition = false;
        private Coordinates coordinates1;
        private double angle1;
        private Coordinates coordinates2;
        private double angle2;

        [ImportingConstructor]
        public PolarAlignmentDockableVM(IProfileService profileService, ICameraMediator cameraMediator, ITelescopeMediator telescopeMediator, IPlateSolverFactory plateSolverFactory, IImagingMediator imagingMediator, IFilterWheelMediator filterWheelMediator) : base(profileService) {
            this.cameraMediator = cameraMediator;
            this.telescopeMediator = telescopeMediator;
            this.plateSolverFactory = plateSolverFactory;
            this.imagingMediator = imagingMediator;
            this.filterWheelMediator = filterWheelMediator;
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

        public string Logs {
            get => logs;
            set {
                logs = value;
                RaisePropertyChanged(nameof(Logs));
            }
        }

        public ICommand StartAlignmentCommand => startAlignmentCommand ??= new RelayCommand(o => StartAlignment());

        public void Log(string message) {
            Logs += $"\n[{DateTime.Now:HH:mm:ss}] {message}";
            global::NINA.Core.Utility.Logger.Info($"[2-Point Polar Alignment] {message}");
        }

        public void StartAlignment() {
            Task.Run(async () => {
                try {
                    await StartAlignmentAsync();
                } catch (Exception ex) {
                    Log($"[Error] Alignment failed: {ex.Message}");
                    Notification.ShowError($"Alignment failed: {ex.Message}");
                }
            });
        }

        private async Task StartAlignmentAsync() {
            Logs = $"[{DateTime.Now:HH:mm:ss}] [System] Starting alignment sequence...";
            Log("Initiating Phase A (Pre-flight Checks)...");

            // 1. Verify Camera Connection
            bool isCameraConnected = IsCameraConnected;
            if (!isCameraConnected) {
                Log("Error: Camera is not connected!");
                Notification.ShowError("2-Point Polar Alignment Error: Camera is not connected!");
                return;
            }

            // 2. Verify Mount Connection
            bool isMountConnected = IsMountConnected;
            if (!isMountConnected) {
                Log("Error: Telescope Mount is not connected!");
                Notification.ShowError("2-Point Polar Alignment Error: Telescope Mount is not connected!");
                return;
            }

            // 3. Verify Filter Wheel Connection (If a specific filter is selected)
            if (!string.IsNullOrEmpty(Filter) && Filter != "(Current)") {
                bool isFWConnected = filterWheelMediator?.GetInfo()?.Connected ?? false;
                if (!isFWConnected) {
                    Log($"Error: Specific filter '{Filter}' is selected, but the Filter Wheel is not connected!");
                    Notification.ShowError($"2-Point Polar Alignment Error: Specific filter '{Filter}' is selected, but the Filter Wheel is not connected!");
                    return;
                }
            }

            // 4. Verify Plate Solver Configuration
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
                Log($"Error: Selected Plate Solver ({detectedSolverName}) is unconfigured or not installed!");
                Notification.ShowError($"2-Point Polar Alignment Error: Selected Plate Solver ({detectedSolverName}) is unconfigured or not installed!");
                return;
            }

            Log($"Valid Plate Solver found: {detectedSolverName}");
            Log("Phase A (Pre-flight Checks) completed successfully!");
            Notification.ShowSuccess("2-Point Polar Alignment: Pre-flight checks passed successfully!");

            await Task.Delay(1000);

            // Save original tracking state and disable tracking for polar alignment
            bool originalTracking = false;
            try {
                originalTracking = telescopeMediator.GetInfo()?.TrackingEnabled ?? false;
                if (originalTracking) {
                    telescopeMediator.SetTrackingEnabled(false);
                    Log("Disabling telescope tracking for polar alignment sequence.");
                }
            } catch { }

            try {
                // ==================== PHASE B: Initial Positioning & Verification ====================
            bool isSimulation = (cameraMediator.GetInfo()?.Name?.Contains("Simulator", StringComparison.OrdinalIgnoreCase) ?? false) ||
                                (telescopeMediator.GetInfo()?.Name?.Contains("Simulator", StringComparison.OrdinalIgnoreCase) ?? false);

            if (isSimulation) {
                Log("Simulator detected! Running in Simulation Mode.");
            }

            Log("Initiating Phase B (Initial Positioning & Verification)...");

            var currentPosition = telescopeMediator.GetCurrentPosition();
            if (currentPosition == null) {
                throw new InvalidOperationException("Could not retrieve current telescope position.");
            }

            if (Method == RotationMethod.Automatic) {
                if (!hasExecutedBefore) {
                    bool isNearPole = Math.Abs(Math.Abs(currentPosition.Dec) - 90.0) < 1.0;
                    if (!isNearPole) {
                        string errMsg = "Telescope is not in the home position. Please move the telescope to the home position first.";
                        Log($"Error: {errMsg}");
                        throw new InvalidOperationException(errMsg);
                    }

                    homePosition = new Coordinates(currentPosition.RA, currentPosition.Dec, currentPosition.Epoch, Coordinates.RAType.Hours);
                    hasExecutedBefore = true;
                    Log($"First run verified at Home position (RA: {homePosition.RAString}, Dec: {homePosition.DecString}). Reference state initialized.");
                } else {
                    var info = telescopeMediator.GetInfo();
                    double currentDec = info?.Declination ?? 0;
                    double currentRA = info?.RightAscension ?? 0;
                    double lst = info?.SiderealTime ?? 0;
                    double currentHA = lst - currentRA;
                    if (currentHA < 0) currentHA += 24.0;
                    if (currentHA >= 24.0) currentHA -= 24.0;

                    bool isNearHome = Math.Abs(Math.Abs(currentDec) - 90.0) < 1.0;
                    if (isNearHome) {
                        homePosition = new Coordinates(currentRA, currentDec, currentPosition?.Epoch ?? Epoch.JNOW, Coordinates.RAType.Hours);
                        hasRecordedPosition = false;
                        Log($"Telescope returned to Home position (RA: {homePosition.RAString}, Dec: {homePosition.DecString}). Starting a fresh alignment run.");
                    } else {
                        double haDiff = Math.Abs(currentHA - recordedHA);
                        if (haDiff > 12.0) haDiff = 24.0 - haDiff;

                        double raDiff = Math.Abs(currentRA - recordedRA);
                        if (raDiff > 12.0) raDiff = 24.0 - raDiff;

                        double toleranceHours = 0.25 / 15.0;
                        bool isValidRetry = hasRecordedPosition && 
                                            Math.Abs(currentDec - recordedDec) < 0.25 && 
                                            (haDiff < toleranceHours || raDiff < toleranceHours);

                        if (!isValidRetry) {
                            string errMsg = "Telescope has been moved from its recorded position. Please move the telescope to the home position and start again.";
                            Log($"Error: {errMsg}");
                            hasExecutedBefore = false;
                            hasRecordedPosition = false;
                            throw new InvalidOperationException(errMsg);
                        }

                        Log($"Valid retry detected (Dec: {currentDec:F2}°, RA: {currentRA:F4}h, HA: {currentHA:F2}h matches recorded Dec: {recordedDec:F2}°, RA: {recordedRA:F4}h, HA: {recordedHA:F4}h).");
                        if (homePosition != null) {
                            Log($"Automatically slewing back to verified Home Position (RA: {homePosition.RAString}, Dec: {homePosition.DecString})...");
                            await telescopeMediator.SlewToCoordinatesAsync(homePosition, CancellationToken.None);
                            try { telescopeMediator.SetTrackingEnabled(false); } catch { }
                            Log("Successfully returned to Home Position.");
                            currentPosition = homePosition;
                        }
                    }
                }
            }

            // 1. Filter Wheel Check (Done at safe static home position before any mount movement)
            if (!string.IsNullOrEmpty(Filter) && Filter != "(Current)") {
                if (filterWheelMediator == null) {
                    throw new InvalidOperationException("Filter wheel is requested but the filter wheel mediator is unavailable.");
                }

                FilterInfo targetFilterInfo = null;
                try {
                    if (profileService?.ActiveProfile != null) {
                        var profileType = profileService.ActiveProfile.GetType();
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
                                            var name = nameProp?.GetValue(item) as string;
                                            if (name == Filter) {
                                                var posProp = item.GetType().GetProperty("Position");
                                                short pos = posProp != null ? Convert.ToInt16(posProp.GetValue(item)) : (short)0;
                                                targetFilterInfo = new FilterInfo { Name = name, Position = pos };
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (targetFilterInfo == null) {
                            var filtersProp = profileType.GetProperty("Filters") ?? profileType.GetProperty("FilterSettings");
                            if (filtersProp != null) {
                                var filtersValue = filtersProp.GetValue(profileService.ActiveProfile);
                                if (filtersValue is System.Collections.IEnumerable enumerable) {
                                    foreach (var item in enumerable) {
                                        var nameProp = item.GetType().GetProperty("Name");
                                        var name = nameProp?.GetValue(item) as string;
                                        if (name == Filter) {
                                            var posProp = item.GetType().GetProperty("Position");
                                            short pos = posProp != null ? Convert.ToInt16(posProp.GetValue(item)) : (short)0;
                                            targetFilterInfo = new FilterInfo { Name = name, Position = pos };
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }

                if (targetFilterInfo == null) {
                    targetFilterInfo = new FilterInfo { Name = Filter, Position = 0 };
                }

                Log($"Changing filter to {Filter} (Position: {targetFilterInfo.Position})...");
                var filterProgress = new Progress<ApplicationStatus>();
                await filterWheelMediator.ChangeFilter(targetFilterInfo, CancellationToken.None, filterProgress);
                Log($"Filter successfully changed to {Filter}.");
            }

            // 1. Mount Slewing (Pre-rotation if requested)
            if (StartingPoint == StartingPointMode.PreRotateHalfRange) {
                double offsetDegrees = RotationAmount / 2.0;
                double offsetHours = offsetDegrees / 15.0;
                Log($"Pre-rotating RA by {offsetDegrees:F1}° in the opposite direction of {Direction}...");

                double targetRA = currentPosition.RA + (Direction == RotationDirection.East ? -offsetHours : offsetHours);
                if (targetRA < 0) targetRA += 24.0;
                if (targetRA >= 24.0) targetRA -= 24.0;

                Coordinates targetCoords = new Coordinates(targetRA, currentPosition.Dec, currentPosition.Epoch, Coordinates.RAType.Hours);
                await telescopeMediator.SlewToCoordinatesAsync(targetCoords, CancellationToken.None);
                try { telescopeMediator.SetTrackingEnabled(false); } catch { }
                Log("Pre-rotation completed successfully.");
            } else {
                Log("Starting at current/home position.");
            }


            // ==================== PHASE C: First Measurement ====================
            Log("Initiating Phase C (First Measurement)...");

            // 2. Camera Exposure and Plate Solving
            Log("Capturing starting image and solving...");

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
                Log("[Simulator] Simulating 3s camera exposure and plate solve...");
                await Task.Delay(3000);
                var simPosition = telescopeMediator.GetCurrentPosition() ?? new Coordinates(12.0, 45.0, Epoch.JNOW, Coordinates.RAType.Hours);
                result = new PlateSolveResult {
                    Success = true,
                    Coordinates = simPosition,
                    PositionAngle = 45.0
                };
            } else {
                var profile = profileService.ActiveProfile;
                var plateSolveSettings = profile.GetType().GetProperty("PlateSolveSettings")?.GetValue(profile) as IPlateSolveSettings;
                if (plateSolveSettings == null) {
                    throw new InvalidOperationException("Could not retrieve active plate solve settings.");
                }

                IPlateSolver solver = plateSolverFactory.GetPlateSolver(plateSolveSettings);
                ICaptureSolver captureSolver = plateSolverFactory.GetCaptureSolver(solver, null, imagingMediator, filterWheelMediator);

                double searchRadiusVal = 15.0;
                try {
                    var radiusProp = plateSolveSettings.GetType().GetProperty("SearchRadius");
                    if (radiusProp != null) {
                        searchRadiusVal = Convert.ToDouble(radiusProp.GetValue(plateSolveSettings) ?? 15.0);
                    }
                } catch { }

                CaptureSolverParameter solverParam = new CaptureSolverParameter {
                    Attempts = 1,
                    ReattemptDelay = TimeSpan.FromSeconds(2),
                    FocalLength = profile.TelescopeSettings.FocalLength,
                    PixelSize = cameraMediator.GetInfo()?.PixelSize ?? 0,
                    Binning = binVal,
                    SearchRadius = searchRadiusVal,
                    BlindFailoverEnabled = false,
                    DisableNotifications = true
                };

                var solveProgress = new Progress<PlateSolveProgress>(p => {
                    if (p.Thumbnail != null) {
                        System.Windows.Application.Current.Dispatcher.Invoke(() => {
                            LastFrame = p.Thumbnail;
                        });
                    }
                });
                var appProgress = new Progress<ApplicationStatus>();

                try {
                    result = await captureSolver.Solve(sequence, solverParam, solveProgress, appProgress, CancellationToken.None);
                } catch (Exception ex) {
                    global::NINA.Core.Utility.Logger.Error($"[2-Point Polar Alignment] Internal solve error: {ex.Message}");
                    throw new InvalidOperationException("Initial plate solve failed. Ensure exposure settings are correct, the lens cap is off, and stars are visible.");
                }
            }

            if (result == null || !result.Success) {
                throw new InvalidOperationException("Initial plate solve failed. Ensure exposure settings are correct and the sky is clear.");
            }

            // 4. Coordinate Storage
            coordinates1 = result.Coordinates;
            angle1 = result.PositionAngle;

            Log($"Solved initial coordinates successfully: RA {coordinates1.RAString}, Dec {coordinates1.DecString}, Orientation Angle: {angle1:F2}°");

            // Safety check: verify solved coordinates are not too far from the mount's physical position
            var physicalPos1 = telescopeMediator.GetCurrentPosition();
            if (physicalPos1 != null) {
                double searchRadius = 15.0; // default fallback
                try {
                    var profile = profileService.ActiveProfile;
                    var settings = profile.GetType().GetProperty("PlateSolveSettings")?.GetValue(profile);
                    var radiusProp = settings?.GetType().GetProperty("SearchRadius");
                    if (radiusProp != null) {
                        searchRadius = Convert.ToDouble(radiusProp.GetValue(settings) ?? 15.0);
                    }
                } catch { }

                double safetyThreshold = Math.Max(5.0, searchRadius - 5.0);

                double lat1 = physicalPos1.Dec * Math.PI / 180.0;
                double lat2 = coordinates1.Dec * Math.PI / 180.0;
                double dLon = (physicalPos1.RA - coordinates1.RA) * 15.0 * Math.PI / 180.0;

                double cosTheta = Math.Sin(lat1) * Math.Sin(lat2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
                cosTheta = Math.Clamp(cosTheta, -1.0, 1.0);
                double separation = Math.Acos(cosTheta) * 180.0 / Math.PI;

                Log($"Solver/Mount Separation (M1): {separation:F2}° (Active Solver Search Radius: {searchRadius:F1}°, Safety Threshold: {safetyThreshold:F1}°).");

                if (separation > safetyThreshold) {
                    string errMsg = $"Safety Intercept: The plate solved position is too far from the mount's reported position ({separation:F2}° separation exceeds the {safetyThreshold:F1}° safety threshold). Alignment aborted for safety.";
                    Log($"Error: {errMsg}");
                    throw new InvalidOperationException(errMsg);
                }
            }

            Log("Saved Measurement 1 -> RA 1, Dec 1, Angle 1.");
            Notification.ShowSuccess("2-Point Polar Alignment: Phase C (First Measurement) completed successfully!");

            await Task.Delay(1000);

            // ==================== PHASE D: The Rotation ====================
            Log("Initiating Phase D (The Rotation)...");

            if (Method == RotationMethod.Manual) {
                Log($"[Manual Rotation] Please release the clutches, manually rotate the RA axis by approximately {RotationAmount:F1}° {Direction}, re-engage clutches, and click OK.");
                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    System.Windows.MessageBox.Show(
                        $"Please release the clutches, manually rotate the RA axis by approximately {RotationAmount:F1}° {Direction}, re-engage the clutches, and click OK to continue.",
                        "Manual RA Rotation Required",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information
                    );
                });
                Log("Manual rotation completed by user.");
            } else {
                var physicalPos = telescopeMediator.GetCurrentPosition() ?? coordinates1;
                double offsetHours = RotationAmount / 15.0;
                double targetRA = physicalPos.RA + (Direction == RotationDirection.East ? offsetHours : -offsetHours);
                if (targetRA < 0) targetRA += 24.0;
                if (targetRA >= 24.0) targetRA -= 24.0;
 
                Coordinates targetCoords = new Coordinates(targetRA, physicalPos.Dec, physicalPos.Epoch, Coordinates.RAType.Hours);
                Log($"Automatically slewing RA axis by {RotationAmount:F1}° {Direction} to target RA {targetCoords.RAString}...");
                await telescopeMediator.SlewToCoordinatesAsync(targetCoords, CancellationToken.None);
                try { telescopeMediator.SetTrackingEnabled(false); } catch { }
                Log("Automatic rotation completed successfully.");
            }

            await Task.Delay(1000);

            // ==================== PHASE E: Second Measurement ====================
            Log("Initiating Phase E (Second Measurement)...");

            if (isSimulation) {
                Log("[Simulator] Simulating 3s camera exposure and plate solve for Measurement 2...");
                await Task.Delay(3000);
                var simPosition = telescopeMediator.GetCurrentPosition() ?? new Coordinates(12.0, 45.0, Epoch.JNOW, Coordinates.RAType.Hours);
                result = new PlateSolveResult {
                    Success = true,
                    Coordinates = simPosition,
                    PositionAngle = angle1 + (Direction == RotationDirection.East ? RotationAmount : -RotationAmount)
                };
            } else {
                Log("Capturing second image and solving...");
                var profile = profileService.ActiveProfile;
                var plateSolveSettings = profile.GetType().GetProperty("PlateSolveSettings")?.GetValue(profile) as IPlateSolveSettings;
                if (plateSolveSettings == null) {
                    throw new InvalidOperationException("Could not retrieve active plate solve settings.");
                }

                IPlateSolver solver = plateSolverFactory.GetPlateSolver(plateSolveSettings);
                ICaptureSolver captureSolver = plateSolverFactory.GetCaptureSolver(solver, null, imagingMediator, filterWheelMediator);

                double searchRadiusVal = 15.0;
                try {
                    var radiusProp = plateSolveSettings.GetType().GetProperty("SearchRadius");
                    if (radiusProp != null) {
                        searchRadiusVal = Convert.ToDouble(radiusProp.GetValue(plateSolveSettings) ?? 15.0);
                    }
                } catch { }

                CaptureSolverParameter solverParam = new CaptureSolverParameter {
                    Attempts = 1,
                    ReattemptDelay = TimeSpan.FromSeconds(2),
                    FocalLength = profile.TelescopeSettings.FocalLength,
                    PixelSize = cameraMediator.GetInfo()?.PixelSize ?? 0,
                    Binning = binVal,
                    SearchRadius = searchRadiusVal,
                    BlindFailoverEnabled = false,
                    DisableNotifications = true
                };

                var solveProgress = new Progress<PlateSolveProgress>(p => {
                    if (p.Thumbnail != null) {
                        System.Windows.Application.Current.Dispatcher.Invoke(() => {
                            LastFrame = p.Thumbnail;
                        });
                    }
                });
                var appProgress = new Progress<ApplicationStatus>();

                try {
                    result = await captureSolver.Solve(sequence, solverParam, solveProgress, appProgress, CancellationToken.None);
                } catch (Exception ex) {
                    global::NINA.Core.Utility.Logger.Error($"[2-Point Polar Alignment] Internal solve error: {ex.Message}");
                    throw new InvalidOperationException("Second plate solve failed. Ensure exposure settings are correct, the lens cap is off, and stars are visible.");
                }
            }

            if (result == null || !result.Success) {
                throw new InvalidOperationException("Second plate solve failed. Ensure exposure settings are correct and the sky is clear.");
            }

            coordinates2 = result.Coordinates;
            angle2 = result.PositionAngle;

            Log($"Solved second coordinates successfully: RA {coordinates2.RAString}, Dec {coordinates2.DecString}, Orientation Angle: {angle2:F2}°");

            // Safety check: verify second solved coordinates are not too far from the mount's physical position
            var physicalPos2 = telescopeMediator.GetCurrentPosition();
            if (physicalPos2 != null) {
                double searchRadius = 15.0; // default fallback
                try {
                    var profile = profileService.ActiveProfile;
                    var settings = profile.GetType().GetProperty("PlateSolveSettings")?.GetValue(profile);
                    var radiusProp = settings?.GetType().GetProperty("SearchRadius");
                    if (radiusProp != null) {
                        searchRadius = Convert.ToDouble(radiusProp.GetValue(settings) ?? 15.0);
                    }
                } catch { }

                double safetyThreshold = Math.Max(5.0, searchRadius - 5.0);

                double lat1 = physicalPos2.Dec * Math.PI / 180.0;
                double lat2 = coordinates2.Dec * Math.PI / 180.0;
                double dLon = (physicalPos2.RA - coordinates2.RA) * 15.0 * Math.PI / 180.0;

                double cosTheta = Math.Sin(lat1) * Math.Sin(lat2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
                cosTheta = Math.Clamp(cosTheta, -1.0, 1.0);
                double separation = Math.Acos(cosTheta) * 180.0 / Math.PI;

                Log($"Solver/Mount Separation (M2): {separation:F2}° (Active Solver Search Radius: {searchRadius:F1}°, Safety Threshold: {safetyThreshold:F1}°).");

                if (separation > safetyThreshold) {
                    string errMsg = $"Safety Intercept: The second plate solved position is too far from the mount's reported position ({separation:F2}° separation exceeds the {safetyThreshold:F1}° safety threshold). Alignment aborted for safety.";
                    Log($"Error: {errMsg}");
                    throw new InvalidOperationException(errMsg);
                }
            }

            Log("Saved Measurement 2 -> RA 2, Dec 2, Angle 2.");

            // Record final target DEC/RA/HA position for subsequent retry checks
            if (Method == RotationMethod.Automatic) {
                var info = telescopeMediator.GetInfo();
                recordedDec = info?.Declination ?? 0;
                recordedRA = info?.RightAscension ?? 0;
                double lst = info?.SiderealTime ?? 0;
                double calculatedHA = lst - recordedRA;
                if (calculatedHA < 0) calculatedHA += 24.0;
                if (calculatedHA >= 24.0) calculatedHA -= 24.0;
                recordedHA = calculatedHA;
                hasRecordedPosition = true;
                Log($"Recorded final target position Dec: {recordedDec:F2}°, RA: {recordedRA:F4}h, HA: {recordedHA:F4}h for subsequent retry verification.");
            }

            Notification.ShowSuccess("2-Point Polar Alignment: Phase E (Second Measurement) completed successfully!");
            }
            finally {
                try {
                    if (originalTracking) {
                        telescopeMediator.SetTrackingEnabled(true);
                        Log("Restored telescope tracking to its original state (Enabled).");
                    }
                } catch { }
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
