using System;
using Math = System.Math;
using NINA.WPF.Base.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Core.Model.Equipment;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyTelescope;
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
using NirZonshine.NINA.TwoPointPolarAlignment.Domain;
using NirZonshine.NINA.TwoPointPolarAlignment.Services;

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

    public enum AltitudeKnobDirection {
        UpArrow,
        Clockwise,
        AntiClockwise
    }

    [Export(typeof(global::NINA.Equipment.Interfaces.ViewModel.IDockableVM))]
    public class PolarAlignmentDockableVM : DockableVM, ICameraConsumer, ITelescopeConsumer {

        private readonly SettingsManager _settingsManager;
        private ImageSource lastFrame;
        private string logs = "[System] Waiting for user interaction...";
        private ICommand startAlignmentCommand;
        private string azimuthError = "--' --\"";
        private string altitudeError = "--' --\"";
        private double totalErrorValue = 0.0;
        private string totalError = "--' --\"";
        private Brush totalErrorColor = Brushes.LightCoral;
        private Brush totalErrorRatingColor = Brushes.LightCoral;
        private string azimuthInstruction = "Waiting...";
        private string altitudeInstruction = "Waiting...";
        private string totalErrorRating = "Waiting...";
        private bool isRunning = false;
        private bool requestedHome = false;
        private int _taskExecutingFlag = 0; // 0 = idle, 1 = executing (Interlocked for thread-safe access)
        private bool isPreviousAlignmentDimmed = false;
        // T-3 Fix: Removed duplicate _hardwareInterlock — the controller owns the single interlock instance.

        // T-3 Fix: ExecuteHardwareOperationAsync removed from VM — HomeAlignment now calls mediator directly.
        // The controller's _hardwareInterlock guards all workflow hardware ops.

        private System.Threading.CancellationTokenSource alignmentCts;
        private Coordinates lastStoppedCoordinates;
        private RotationDirection? lastStoppedDirection;
        private bool isAltitudePriority = false;
        private bool isAzimuthPriority = false;
        private bool isBlindSolvingActive = false;
        private string statusIndicatorText = "Ready to Start";
        private Brush statusIndicatorColor = StatusIdleColor;

        private readonly ICameraMediator cameraMediator;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IPlateSolverFactory plateSolverFactory;
        private readonly IImagingMediator imagingMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly System.Windows.Threading.DispatcherTimer statusTimer;
        
        private CameraInfo currentCameraInfo;
        private TelescopeInfo currentTelescopeInfo;
        private bool lastIsCameraConnected;
        private bool lastIsMountConnected;
        private readonly NirZonshine.NINA.TwoPointPolarAlignment.Solvers.IPolarSolver _polarSolver = new NirZonshine.NINA.TwoPointPolarAlignment.Solvers.TwoPointPolarSolver();

        private static Brush CreateFrozenBrush(string hex) {
            try {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                b.Freeze();
                return b;
            } catch { return Brushes.SkyBlue; }
        }
        
        private static readonly Brush StatusIdleColor = CreateFrozenBrush("#72BDFF");
        private static readonly Brush StatusWarningColor = CreateFrozenBrush("#FBBF24");
        private static readonly Brush StatusSuccessColor = CreateFrozenBrush("#22C55E");
        private static readonly Brush StatusFailureColor = CreateFrozenBrush("#EF4444");
        private static readonly Brush StatusProgressColor = CreateFrozenBrush("#6366F1");
        private static readonly Brush StatusTrackingColor = CreateFrozenBrush("#A855F7");

        private void SetStatus(string text, Brush color) {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                StatusIndicatorText = text;
                StatusIndicatorColor = color;
            }));
        }

        private readonly IProfileService _profileService;

        [ImportingConstructor]
        public PolarAlignmentDockableVM(IProfileService profileService, ICameraMediator cameraMediator, ITelescopeMediator telescopeMediator, IPlateSolverFactory plateSolverFactory, IImagingMediator imagingMediator, IFilterWheelMediator filterWheelMediator) : base(profileService) {
            this._profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.telescopeMediator = telescopeMediator;
            this.plateSolverFactory = plateSolverFactory;
            this.imagingMediator = imagingMediator;
            this.filterWheelMediator = filterWheelMediator;
            
            cameraMediator.RegisterConsumer(this);
            telescopeMediator.RegisterConsumer(this);
            
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
            
            group.Freeze(); // Crucial for cross-thread WPF access
            ImageGeometry = group;
            
            _settingsManager = new SettingsManager(profileService);
            _settingsManager.PropertyChanged += SettingsManager_PropertyChanged;
        }

        private void SettingsManager_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            RaisePropertyChanged(e.PropertyName);
            if (e.PropertyName == nameof(Method)) {
                RaisePropertyChanged(nameof(ShowMountWarning));
                RaisePropertyChanged(nameof(CanRun));
                RaisePropertyChanged(nameof(CanStart));
            }
        }

        public override bool IsTool => true;
        
        public bool IsCameraConnected => (currentCameraInfo?.Connected ?? cameraMediator?.GetInfo()?.Connected ?? false);
        
        public bool IsMountConnected => (currentTelescopeInfo?.Connected ?? telescopeMediator?.GetInfo()?.Connected ?? false);

        public bool CanRun => IsCameraConnected && (Method == RotationMethod.Manual || IsMountConnected);

        public bool ShowMountWarning => !IsMountConnected && Method != RotationMethod.Manual;



        private void StatusTimer_Tick(object sender, EventArgs e) {
            var currentCamera = IsCameraConnected;
            var currentMount = IsMountConnected;

            if (currentCamera != lastIsCameraConnected) {
                lastIsCameraConnected = currentCamera;
                RaisePropertyChanged(nameof(IsCameraConnected));
                RaisePropertyChanged(nameof(CanRun));
                RaisePropertyChanged(nameof(CanStart));
            }

            if (currentMount != lastIsMountConnected) {
                lastIsMountConnected = currentMount;
                RaisePropertyChanged(nameof(IsMountConnected));
                RaisePropertyChanged(nameof(ShowMountWarning));
                RaisePropertyChanged(nameof(CanRun));
                RaisePropertyChanged(nameof(CanStart));
                RaisePropertyChanged(nameof(CanHome));
            }
        }

        public void UpdateDeviceInfo(CameraInfo deviceInfo) {
            currentCameraInfo = deviceInfo;
            
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                RaisePropertyChanged(nameof(IsCameraConnected));
                RaisePropertyChanged(nameof(CanRun));
                RaisePropertyChanged(nameof(CanStart));
            });
        }

        public void UpdateDeviceInfo(TelescopeInfo deviceInfo) {
            currentTelescopeInfo = deviceInfo;
            
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                RaisePropertyChanged(nameof(IsMountConnected));
                RaisePropertyChanged(nameof(ShowMountWarning));
                RaisePropertyChanged(nameof(CanRun));
                RaisePropertyChanged(nameof(CanStart));
                RaisePropertyChanged(nameof(CanHome));
            });
        }

        public void Dispose() {
            try { StopAlignment(); }
            catch (Exception ex) { global::NINA.Core.Utility.Logger.Error($"[2-Point Polar Alignment] Dispose.StopAlignment failed: {ex.Message}"); }

            try {
                if (statusTimer != null) {
                    statusTimer.Tick -= StatusTimer_Tick;
                    statusTimer.Stop();
                }
            }
            catch (Exception ex) { global::NINA.Core.Utility.Logger.Error($"[2-Point Polar Alignment] Dispose.TimerStop failed: {ex.Message}"); }

            // M-1 Fix: Detach SettingsManager events and dispose to release ProfileChanged subscription
            try {
                _settingsManager.PropertyChanged -= SettingsManager_PropertyChanged;
                _settingsManager.Dispose();
            }
            catch (Exception ex) { global::NINA.Core.Utility.Logger.Error($"[2-Point Polar Alignment] Dispose.SettingsManager failed: {ex.Message}"); }

            try { cameraMediator.RemoveConsumer(this); }
            catch (Exception ex) { global::NINA.Core.Utility.Logger.Error($"[2-Point Polar Alignment] Dispose.RemoveCameraConsumer failed: {ex.Message}"); }

            try { telescopeMediator.RemoveConsumer(this); }
            catch (Exception ex) { global::NINA.Core.Utility.Logger.Error($"[2-Point Polar Alignment] Dispose.RemoveTelescopeConsumer failed: {ex.Message}"); }
        }

        public double RotationAmount {
            get => _settingsManager.RotationAmount;
            set => _settingsManager.RotationAmount = value;
        }

        public RotationMethod Method {
            get => _settingsManager.Method;
            set => _settingsManager.Method = value;
        }

        public RotationDirection Direction {
            get => _settingsManager.Direction;
            set => _settingsManager.Direction = value;
        }

        public StartingPointMode StartingPoint {
            get => _settingsManager.StartingPoint;
            set => _settingsManager.StartingPoint = value;
        }

        public double ExposureTime {
            get => _settingsManager.ExposureTime;
            set => _settingsManager.ExposureTime = value;
        }

        public int Gain {
            get => _settingsManager.Gain;
            set => _settingsManager.Gain = value;
        }

        public ImageSource LastFrame {
            get => lastFrame;
            set {
                lastFrame = value;
                RaisePropertyChanged(nameof(LastFrame));
            }
        }

        public string Filter {
            get => _settingsManager.Filter;
            set => _settingsManager.Filter = value;
        }

        public AltitudeKnobDirection AltKnobDirection {
            get => _settingsManager.AltKnobDirection;
            set {
                _settingsManager.AltKnobDirection = value;
            }
        }

        public bool IsBlindSolvingActive {
            get => isBlindSolvingActive;
            set {
                isBlindSolvingActive = value;
                RaisePropertyChanged(nameof(IsBlindSolvingActive));
            }
        }

        public string Binning {
            get => _settingsManager.Binning;
            set => _settingsManager.Binning = value;
        }

        public int Offset {
            get => _settingsManager.Offset;
            set => _settingsManager.Offset = value;
        }

        public double TelescopeMoveRate {
            get => _settingsManager.TelescopeMoveRate;
            set => _settingsManager.TelescopeMoveRate = value;
        }

        public int PlateSolveRetries {
            get => _settingsManager.PlateSolveRetries;
            set => _settingsManager.PlateSolveRetries = value;
        }

        public bool OverrideMountHome {
            get => _settingsManager.OverrideMountHome;
            set {
                _settingsManager.OverrideMountHome = value;
                RaisePropertyChanged(nameof(OverrideMountHome));
                RaisePropertyChanged(nameof(OverrideMountHomeIndex));
            }
        }

        public int OverrideMountHomeIndex {
            get => _settingsManager.OverrideMountHome ? 1 : 0;
            set {
                _settingsManager.OverrideMountHome = (value == 1);
                RaisePropertyChanged(nameof(OverrideMountHome));
                RaisePropertyChanged(nameof(OverrideMountHomeIndex));
            }
        }

        public string Logs {
            get => logs;
            set {
                logs = value;
                RaisePropertyChanged(nameof(Logs));
            }
        }

        public string AzimuthError {
            get => azimuthError;
            set {
                azimuthError = value;
                RaisePropertyChanged(nameof(AzimuthError));
            }
        }

        public string AltitudeError {
            get => altitudeError;
            set {
                altitudeError = value;
                RaisePropertyChanged(nameof(AltitudeError));
            }
        }

        public double TotalErrorValue {
            get => totalErrorValue;
            set {
                totalErrorValue = value;
                RaisePropertyChanged(nameof(TotalErrorValue));
            }
        }

        public string TotalError {
            get => totalError;
            set {
                totalError = value;
                RaisePropertyChanged(nameof(TotalError));
            }
        }

        public Brush TotalErrorColor {
            get => totalErrorColor;
            set {
                totalErrorColor = value;
                RaisePropertyChanged(nameof(TotalErrorColor));
            }
        }

        public Brush TotalErrorRatingColor {
            get => totalErrorRatingColor;
            set {
                totalErrorRatingColor = value;
                RaisePropertyChanged(nameof(TotalErrorRatingColor));
            }
        }

        public bool IsPreviousAlignmentDimmed {
            get => isPreviousAlignmentDimmed;
            set {
                isPreviousAlignmentDimmed = value;
                RaisePropertyChanged(nameof(IsPreviousAlignmentDimmed));
            }
        }

        public string StatusIndicatorText {
            get => statusIndicatorText;
            set {
                statusIndicatorText = value;
                RaisePropertyChanged(nameof(StatusIndicatorText));
            }
        }

        public Brush StatusIndicatorColor {
            get => statusIndicatorColor;
            set {
                statusIndicatorColor = value;
                RaisePropertyChanged(nameof(StatusIndicatorColor));
            }
        }

        public bool IsAltitudePriority {
            get => isAltitudePriority;
            set {
                isAltitudePriority = value;
                RaisePropertyChanged(nameof(IsAltitudePriority));
            }
        }

        public bool IsAzimuthPriority {
            get => isAzimuthPriority;
            set {
                isAzimuthPriority = value;
                RaisePropertyChanged(nameof(IsAzimuthPriority));
            }
        }

        public string AzimuthInstruction {
            get => azimuthInstruction;
            set {
                azimuthInstruction = value;
                RaisePropertyChanged(nameof(AzimuthInstruction));
            }
        }

        public string AltitudeInstruction {
            get => altitudeInstruction;
            set {
                altitudeInstruction = value;
                RaisePropertyChanged(nameof(AltitudeInstruction));
            }
        }

        public string TotalErrorRating {
            get => totalErrorRating;
            set {
                totalErrorRating = value;
                RaisePropertyChanged(nameof(TotalErrorRating));
            }
        }

        private bool isReversedFlowActive = false;
        public bool IsReversedFlowActive {
            get => isReversedFlowActive;
            set {
                isReversedFlowActive = value;
                RaisePropertyChanged(nameof(IsReversedFlowActive));
            }
        }

        public bool EnableOnePointAlignment {
            get => _settingsManager.EnableOnePointAlignment;
            set => _settingsManager.EnableOnePointAlignment = value;
        }

        public bool IsRunning {
            get => isRunning;
            set {
                isRunning = value;
                RaisePropertyChanged(nameof(IsRunning));
                RaisePropertyChanged(nameof(CanStart));
                RaisePropertyChanged(nameof(CanHome));
            }
        }

        public bool CanStart => CanRun && !IsRunning && Volatile.Read(ref _taskExecutingFlag) == 0;

        public bool CanHome => IsRunning || IsMountConnected;

        public ICommand StartAlignmentCommand => startAlignmentCommand ??= new RelayCommand(o => {
            if (!IsRunning && Volatile.Read(ref _taskExecutingFlag) == 0) {
                StartAlignment();
            }
        });

        private ICommand stopAlignmentCommand;
        public ICommand StopAlignmentCommand => stopAlignmentCommand ??= new RelayCommand(o => {
            StopAlignment();
        });

        public void StopAlignment() {
            if (IsRunning) {
                Log("Stop requested by user. Aborting alignment sequence...");
                try {
                    alignmentCts?.Cancel();
                } catch { }
                IsRunning = false;
                try {
                    if (telescopeMediator != null && telescopeMediator.GetInfo()?.Connected == true) {
                        telescopeMediator.StopSlew();
                        Log("Mount slew stopped immediately.");
                    }
                } catch (Exception ex) {
                    global::NINA.Core.Utility.Logger.Error($"[2-Point Polar Alignment] StopSlew failed: {ex.Message}");
                }
            }
        }

        private ICommand homeAlignmentCommand;
        public ICommand HomeAlignmentCommand => homeAlignmentCommand ??= new RelayCommand(o => {
            HomeAlignment();
        });

        public void HomeAlignment() {
            if (IsRunning) {
                requestedHome = true;
                StopAlignment();
            } else if (IsMountConnected && Interlocked.CompareExchange(ref _taskExecutingFlag, 1, 0) == 0) {
                RaisePropertyChanged(nameof(CanStart));
                Task.Run(async () => {
                    SetStatus("Homing", StatusWarningColor);
                    try {
                        if (OverrideMountHome) {
                            var currentPosition = telescopeMediator.GetCurrentPosition();
                            if (_profileService?.ActiveProfile?.AstrometrySettings != null && currentPosition != null) {
                                bool isNorthern = _profileService.ActiveProfile.AstrometrySettings.Latitude >= 0;
                                bool targetIsNorthern = _settingsManager.PolarHomeDec >= 0;
                                if (isNorthern != targetIsNorthern) {
                                    // Auto-reset Polar Home to current position when hemisphere changed
                                    bool isNearPole = Math.Abs(Math.Abs(currentPosition.Dec) - 90.0) < 1.0;
                                    if (isNearPole) {
                                        _settingsManager.PolarHomeRA = currentPosition.RA;
                                        _settingsManager.PolarHomeDec = currentPosition.Dec;
                                        Log($"[Polar Home] Hemisphere change detected. Auto-relocked Custom Polar Home to RA: {currentPosition.RA:F2}h, Dec: {currentPosition.Dec:F2}° (new hemisphere).");
                                    } else {
                                        string hemisphereCurrent = isNorthern ? "Northern" : "Southern";
                                        string hemisphereTarget = targetIsNorthern ? "Northern" : "Southern";
                                        string currentPole = isNorthern ? "North" : "South";
                                        string err = $"Hemisphere Mismatch: The locked Custom Polar Home is in the {hemisphereTarget} Hemisphere ({_settingsManager.PolarHomeDec:F2}°), but your mount is currently configured for the {hemisphereCurrent} Hemisphere.\n\n" +
                                                     $"Please manually slew near the {currentPole} Celestial Pole and click 'Lock Polar Home' to update your starting position.";
                                        ShowNinaStyledMessageBox("Hemisphere Mismatch", err);
                                        Log($"[Error] Custom Home Slew aborted: hemisphere mismatch (current: {hemisphereCurrent}, target: {hemisphereTarget}).");
                                        return;
                                    }
                                }
                            }

                            Log("Dispatching custom Polar Home slew command...");
                            var epoch = currentPosition?.Epoch ?? global::NINA.Astrometry.Epoch.J2000;
                            var coords = new global::NINA.Astrometry.Coordinates(
                                _settingsManager.PolarHomeRA,
                                _settingsManager.PolarHomeDec,
                                epoch,
                                global::NINA.Astrometry.Coordinates.RAType.Hours
                            );
                            // T-3 Fix: Direct mediator call — VM no longer owns a hardware interlock
                            await telescopeMediator.SlewToCoordinatesAsync(coords, System.Threading.CancellationToken.None);
                            try { telescopeMediator.SetTrackingEnabled(false); } catch { }
                            Log("Successfully slewed to Custom Polar Home Position.");
                        } else {
                            var info = telescopeMediator.GetInfo();
                            var currentPosition = telescopeMediator.GetCurrentPosition();
                            if (info != null && info.AtHome && currentPosition != null && Math.Abs(currentPosition.Dec) < 45.0) {
                                string err = "Your mount's native Home position is pointing away from the Celestial Pole (near the Equator/Horizon).\n\n" +
                                             "Native homing is disabled to prevent incorrect positioning. Please enable 'Override Mount Home' in settings and slew your mount to its Polar Home position near the pole.";
                                ShowNinaStyledMessageBox("Homing Disabled", err);
                                Log("[Error] Homing command aborted: native home position points away from Celestial Pole. Please enable 'Override Mount Home' in settings.");
                                return;
                            }

                            Log("Dispatching native FindHome command to mount controller...");
                            // T-3 Fix: Direct mediator call — VM no longer owns a hardware interlock
                            await telescopeMediator.FindHome(new Progress<global::NINA.Core.Model.ApplicationStatus>(), System.Threading.CancellationToken.None);
                            try { telescopeMediator.SetTrackingEnabled(false); } catch { }
                            Log("Successfully returned to Home Position.");
                        }
                    } catch (Exception ex) {
                        Log($"[Error] Failed to complete Home directive: {ex.Message}");
                    } finally {
                        SetStatus("Ready to Start", StatusIdleColor);
                        Interlocked.Exchange(ref _taskExecutingFlag, 0);
                        RaisePropertyChanged(nameof(CanStart));
                    }
                });
            }
        }

        private ICommand lockPolarHomeCommand;
        public ICommand LockPolarHomeCommand => lockPolarHomeCommand ??= new RelayCommand(o => {
            LockPolarHome();
        });

        public void LockPolarHome() {
            if (!IsMountConnected) {
                Log("[Error] Cannot lock Polar Home: Telescope is not connected.");
                return;
            }
            var currentPosition = telescopeMediator.GetCurrentPosition();
            if (currentPosition == null) {
                Log("[Error] Cannot lock Polar Home: Could not retrieve current position.");
                return;
            }
            
            bool isNearPole = Math.Abs(Math.Abs(currentPosition.Dec) - 90.0) < 1.0;
            if (!isNearPole) {
                ShowNinaStyledMessageBox("Invalid Position", "Cannot lock Polar Home. Declination must be very close to the Celestial Pole (90°).");
                Log($"[Error] Cannot lock Polar Home: Declination ({currentPosition.Dec:F2}°) is not close to 90°.");
                return;
            }

            _settingsManager.PolarHomeRA = currentPosition.RA;
            _settingsManager.PolarHomeDec = currentPosition.Dec;
            
            Log($"[Polar Home] Locked new Custom Polar Home at RA: {currentPosition.RA:F2}h, Dec: {currentPosition.Dec:F2}°");
            ShowNinaStyledMessageBox("Success", $"Custom Polar Home successfully locked at:\nRA: {currentPosition.RA:F2}h\nDec: {currentPosition.Dec:F2}°");
        }

        public void Log(string message) {
            // W-2 Fix: Marshal Logs += to UI thread so PropertyChanged fires on the dispatcher
            var formatted = $"\n[{DateTime.Now:HH:mm:ss}] {message}";
            global::NINA.Core.Utility.Logger.Info($"[2-Point Polar Alignment] {message}");
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                Logs += formatted;
            }));
        }


        private bool ShowNinaStyledMessageBox(string title, string message, bool isYesNo = false) {
            bool result = false;
            System.Windows.Application.Current.Dispatcher.Invoke(() => {
                var dialog = new System.Windows.Window {
                    Title = title,
                    Width = 430,
                    Height = 180,
                    SizeToContent = System.Windows.SizeToContent.Height,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                    ResizeMode = System.Windows.ResizeMode.NoResize,
                    WindowStyle = System.Windows.WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false
                };
                try { dialog.Owner = System.Windows.Application.Current.MainWindow; } catch { }

                var mainBorder = new System.Windows.Controls.Border {
                    Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x22)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x50)),
                    BorderThickness = new System.Windows.Thickness(1),
                    CornerRadius = new System.Windows.CornerRadius(12),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 30, ShadowDepth = 0, Opacity = 0.6 },
                    Padding = new System.Windows.Thickness(0,0,0,20)
                };

                var grid = new System.Windows.Controls.Grid();
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(45) }); // Header
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto }); // Message
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto }); // Buttons

                var headerColor = Color.FromRgb(0x22, 0xC5, 0x5E); // Default Green
                if (title.Contains("Error") || title.Contains("Invalid") || title.Contains("Failed") || title.Contains("Required")) {
                    headerColor = Color.FromRgb(0xE1, 0x1D, 0x48); // Rose Red
                } else if (title.Contains("Warning")) {
                    headerColor = Color.FromRgb(0xF5, 0x9E, 0x0B); // Amber Orange
                }

                var header = new System.Windows.Controls.Border {
                    Background = new SolidColorBrush(headerColor),
                    CornerRadius = new System.Windows.CornerRadius(12, 12, 0, 0),
                    Padding = new System.Windows.Thickness(20, 0, 20, 0)
                };
                var headerTxt = new System.Windows.Controls.TextBlock {
                    Text = title, VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    FontSize = 15, FontWeight = System.Windows.FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White
                };
                header.Child = headerTxt;
                System.Windows.Controls.Grid.SetRow(header, 0);
                grid.Children.Add(header);

                var msgTxt = new System.Windows.Controls.TextBlock {
                    Text = message, Margin = new System.Windows.Thickness(25, 25, 25, 25),
                    TextWrapping = System.Windows.TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.WhiteSmoke,
                    FontSize = 13, LineHeight = 18
                };
                System.Windows.Controls.Grid.SetRow(msgTxt, 1);
                grid.Children.Add(msgTxt);

                var btnStack = new System.Windows.Controls.StackPanel {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    Margin = new System.Windows.Thickness(0, 0, 25, 5)
                };
                System.Windows.Controls.Grid.SetRow(btnStack, 2);
                grid.Children.Add(btnStack);

                System.Windows.Controls.ControlTemplate CreateNinaBtnTemplate(Color baseClr) {
                    var t = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button));
                    var b = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border));
                    b.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new System.Windows.CornerRadius(6));
                    b.SetValue(System.Windows.Controls.Border.BackgroundProperty, new SolidColorBrush(baseClr));
                    var g = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Grid));
                    var gl = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border));
                    gl.SetValue(System.Windows.Controls.Border.BackgroundProperty, System.Windows.Media.Brushes.White);
                    gl.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new System.Windows.CornerRadius(6));
                    gl.SetValue(System.Windows.UIElement.OpacityProperty, 0.0);
                    gl.Name = "glow";
                    var cp = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
                    cp.SetValue(System.Windows.FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
                    cp.SetValue(System.Windows.FrameworkElement.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
                    cp.SetValue(System.Windows.Controls.ContentPresenter.MarginProperty, new System.Windows.Thickness(15, 0, 15, 0));
                    g.AppendChild(gl); g.AppendChild(cp); b.AppendChild(g);
                    t.VisualTree = b;
                    var h = new System.Windows.Trigger { Property = System.Windows.UIElement.IsMouseOverProperty, Value = true };
                    h.Setters.Add(new System.Windows.Setter(System.Windows.UIElement.OpacityProperty, 0.15, "glow"));
                    t.Triggers.Add(h);
                    return t;
                }

                var okBtn = new System.Windows.Controls.Button { 
                    Content = isYesNo ? "Yes, Engaged Rescue" : "Acknowledged", MinWidth = 90, Height = 32, Margin = new System.Windows.Thickness(10, 0, 0, 0),
                    Foreground = System.Windows.Media.Brushes.White, FontWeight = System.Windows.FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand,
                    Template = CreateNinaBtnTemplate(headerColor)
                };
                okBtn.Click += (s, e) => { result = true; dialog.DialogResult = true; dialog.Close(); };
                
                if (isYesNo) {
                    var noBtn = new System.Windows.Controls.Button { 
                        Content = "Cancel / Abort", MinWidth = 90, Height = 32,
                        Foreground = System.Windows.Media.Brushes.White, FontWeight = System.Windows.FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand,
                        Template = CreateNinaBtnTemplate(Color.FromRgb(0x3D, 0x3D, 0x50))
                    };
                    noBtn.Click += (s, e) => { result = false; dialog.DialogResult = false; dialog.Close(); };
                    btnStack.Children.Add(noBtn);
                }
                btnStack.Children.Add(okBtn);

                mainBorder.Child = grid;
                dialog.Content = mainBorder;
                dialog.ShowDialog();
            });
            return result;
        }

        public void StartAlignment() {
            if (Interlocked.CompareExchange(ref _taskExecutingFlag, 1, 0) != 0) {
                return;
            }
            IsReversedFlowActive = false;
            IsPreviousAlignmentDimmed = true;
            IsRunning = true;
            alignmentCts = new System.Threading.CancellationTokenSource();
            RaisePropertyChanged(nameof(CanStart));
            // W-3 Fix: Construct Progress<T> on the UI thread so callbacks marshal via SynchronizationContext
            var progress = new Progress<AlignmentProgressReport>(report => {
                if (report.LogMessage != null) Log(report.LogMessage);
                if (report.IsReversedFlowActive.HasValue) IsReversedFlowActive = report.IsReversedFlowActive.Value;
                if (report.IsBlindSolvingActive.HasValue) IsBlindSolvingActive = report.IsBlindSolvingActive.Value;
                if (report.StatusText != null && report.StatusColorHex != null) {
                    SetStatus(report.StatusText, CreateFrozenBrush(report.StatusColorHex));
                }
                if (report.AltitudeError != null) {
                    AltitudeError = report.AltitudeError;
                    IsPreviousAlignmentDimmed = false;
                }
                if (report.AzimuthError != null) AzimuthError = report.AzimuthError;
                if (report.TotalError != null) TotalError = report.TotalError;
                if (report.TotalErrorValue > 0) TotalErrorValue = report.TotalErrorValue;
                if (report.AltitudeInstruction != null) AltitudeInstruction = report.AltitudeInstruction;
                if (report.AzimuthInstruction != null) AzimuthInstruction = report.AzimuthInstruction;
                IsAltitudePriority = report.IsAltitudePriority;
                IsAzimuthPriority = report.IsAzimuthPriority;
                if (report.TotalErrorRating != null) TotalErrorRating = report.TotalErrorRating;
                if (report.TotalErrorRatingColorHex != null) TotalErrorRatingColor = CreateFrozenBrush(report.TotalErrorRatingColorHex);
                if (report.TotalErrorValue > 0 && report.TotalErrorRatingColorHex != null) {
                    TotalErrorColor = CreateFrozenBrush(report.TotalErrorRatingColorHex);
                }
            });
            Task.Run(async () => {
                try {
                    await StartAlignmentAsync(progress);
                } catch (OperationCanceledException) {
                    Log("Alignment sequence successfully aborted.");
                    Notification.ShowSuccess("2-Point Polar Alignment: Sequence aborted successfully!");
                    try {
                        if (telescopeMediator != null && telescopeMediator.GetInfo()?.Connected == true) {
                            var pos = telescopeMediator.GetCurrentPosition();
                            if (pos != null) {
                                lastStoppedCoordinates = pos;
                                lastStoppedDirection = IsReversedFlowActive ? 
                                    (Direction == RotationDirection.East ? RotationDirection.West : RotationDirection.East) : 
                                    Direction;
                                Log($"[Smart Restart] Saved last stopped position: RA {pos.RA:F2}h, Dec {pos.Dec:F2}° (Direction: {lastStoppedDirection})");
                            }
                        }
                    } catch { }
                } catch (Exception ex) {
                    Log($"[Error] Alignment failed: {ex.Message}");
                    Notification.ShowError($"Alignment failed: {ex.Message}");
                } finally {
                    bool triggerHome = requestedHome;
                    requestedHome = false;
                    IsRunning = false;
                    Interlocked.Exchange(ref _taskExecutingFlag, 0);
                    RaisePropertyChanged(nameof(CanStart));
                    var oldCts = Interlocked.Exchange(ref alignmentCts, null);
                    oldCts?.Dispose();
                    if (triggerHome) {
                        Log("Waiting for mount to complete deceleration and come to a complete stop...");
                        int maxPolls = 25; // 5.0 seconds maximum timeout
                        int poll = 0;
                        while (telescopeMediator != null && telescopeMediator.GetInfo()?.Slewing == true && poll < maxPolls) {
                            await Task.Delay(200);
                            poll++;
                        }
                        HomeAlignment();
                    }
                }
            });
        }

        private async Task StartAlignmentAsync(IProgress<AlignmentProgressReport> progress) {
            var cts = alignmentCts ?? throw new InvalidOperationException("Alignment CTS was not initialized.");
            
            var controller = new NirZonshine.NINA.TwoPointPolarAlignment.Workflow.AlignmentWorkflowController(
                _profileService, cameraMediator, telescopeMediator, plateSolverFactory, imagingMediator, filterWheelMediator, _polarSolver, _settingsManager
            );
            
            controller.OnManualRotationRequested = async (context, targetDegrees, direction, initialCoords, sequence, captureSolver, isSimulation) => {
                await System.Windows.Application.Current.Dispatcher.Invoke(async () => {
                    var vm = new NirZonshine.NINA.TwoPointPolarAlignment.ViewModels.ManualRotationVM {
                        TargetDegrees = targetDegrees,
                        InstructionText = $"Rotate the mount {direction} to the target angle. Tighten clutches, then click Finish.",
                        IsSimulation = isSimulation
                    };
                    
                    var dialog = new NirZonshine.NINA.TwoPointPolarAlignment.Views.ManualRotationWindow {
                        DataContext = vm,
                        Owner = System.Windows.Application.Current.MainWindow
                    };
                    
                    // M-2 Fix: Dispose dialogCts after dialog closes to release unmanaged timer resources
                    var dialogCts = new System.Threading.CancellationTokenSource();
                    try {
                        vm.FinishRequested += (s, e) => dialog.DialogResult = true;
                        dialog.Closed += (s, e) => dialogCts.Cancel();
                    
                        vm.SimOffsetRequested += (s, offset) => { context.CurrentSimulationOffset = offset; };
                        vm.SimResetRequested += (s, e) => { context.CurrentSimulationOffset = 0.0; };
                    
                        var manualProgress = new Progress<NirZonshine.NINA.TwoPointPolarAlignment.Domain.ManualTrackingProgress>(p => {
                            if (p.StatusText != null) vm.StatusText = p.StatusText;
                            if (p.Thumbnail != null) LastFrame = p.Thumbnail;
                            if (p.TargetDegrees > 0) {
                                vm.CurrentDegrees = p.CurrentDegrees;
                                vm.IsLocked = p.IsLocked;
                            }
                        });
 
                        var trackingTask = controller.ExecuteManualTrackingAsync(context, targetDegrees, direction, initialCoords, sequence, captureSolver, manualProgress, dialogCts.Token);
                    
                        dialog.ShowDialog();
                        dialogCts.Cancel();
                        try { await trackingTask; } catch { }
                    } finally {
                        dialogCts.Dispose();
                    }
                });
            };
            
            controller.OnInterventionRequested = async (args) => {
                return ShowNinaStyledMessageBox(args.Title, args.Message, args.IsYesNo);
            };
 
            // Progress<T> is now passed in from StartAlignment() (constructed on UI thread for correct SynchronizationContext)
 
            var context = new AlignmentWorkflowContext {
                ActiveDirection = Direction,
                ActivePreRotate = Method == RotationMethod.Automatic && StartingPoint == StartingPointMode.PreRotateHalfRange,
                LastStoppedCoordinates = lastStoppedCoordinates,
                LastStoppedDirection = lastStoppedDirection,
            };
            
            // Clear lastStoppedCoordinates and lastStoppedDirection so they only apply to the immediately following start command
            lastStoppedCoordinates = null;
            lastStoppedDirection = null;

            await controller.ExecuteWorkflowAsync(context, cts.Token, progress, thumbnail => {
                System.Windows.Application.Current.Dispatcher.Invoke(() => { LastFrame = thumbnail; });
            });
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

