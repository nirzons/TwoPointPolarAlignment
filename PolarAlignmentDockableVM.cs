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
        private bool isTaskExecuting = false;
        private readonly System.Threading.SemaphoreSlim _hardwareInterlock = new System.Threading.SemaphoreSlim(1, 1);

        private async Task<T> ExecuteHardwareOperationAsync<T>(Func<Task<T>> operation, System.Threading.CancellationToken token, string operationName = "Hardware Operation") {
            bool acquired = false;
            try {
                acquired = await _hardwareInterlock.WaitAsync(TimeSpan.FromSeconds(30), token);
            } catch (OperationCanceledException) {
                throw; // Graceful abort when the user cancels during wait
            }

            if (!acquired) {
                throw new HardwareTeardownTimeoutException($"Hardware driver hung for more than 30 seconds during {operationName}. Safety abort triggered.");
            }

            try {
                return await operation();
            } finally {
                _hardwareInterlock.Release();
            }
        }

        private async Task ExecuteHardwareOperationAsync(Func<Task> operation, System.Threading.CancellationToken token, string operationName = "Hardware Operation") {
            bool acquired = false;
            try {
                acquired = await _hardwareInterlock.WaitAsync(TimeSpan.FromSeconds(30), token);
            } catch (OperationCanceledException) {
                throw; // Graceful abort when the user cancels during wait
            }

            if (!acquired) {
                throw new HardwareTeardownTimeoutException($"Hardware driver hung for more than 30 seconds during {operationName}. Safety abort triggered.");
            }

            try {
                await operation();
            } finally {
                _hardwareInterlock.Release();
            }
        }

        private System.Threading.CancellationTokenSource alignmentCts;
        private Vector3D calculatedPolarAxis;
        private Vector3D measurement2Vector;
        private Vector3D initialPolarAxis;
        private double simTimeFactor = -1.0;
        private bool blinkToggle = false;
        private double currentSimulationOffset = 0.0;
        private bool isAltitudePriority = false;
        private bool isAzimuthPriority = false;
        private bool hasRoughFinderSimTriggered = false;
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
        private bool hasExecutedBefore = false;
        private Coordinates homePosition;
        private double recordedDec = 0;
        private double recordedRA = 0;
        private double recordedHA = 0;
        private bool hasRecordedPosition = false;
        private bool hasSuccessfulAlignmentReached = false;
        private bool wasPreviousRunReversed = false;
        private Coordinates coordinates1;
        private double angle1;
        private Coordinates coordinates2;
        private double angle2;
        private double lstMeasurement2;
        private readonly NirZonshine.NINA.TwoPointPolarAlignment.Solvers.IPolarSolver _polarSolver = new NirZonshine.NINA.TwoPointPolarAlignment.Solvers.TwoPointPolarSolver();
        private AlignmentCalibrationState _activeCalibration;

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
            set {
                if (value) {
                    // User manual interaction resets the simulation trigger so it can fire exactly once more if needed
                    hasRoughFinderSimTriggered = false;
                }
                _settingsManager.EnableOnePointAlignment = value;
            }
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

        public bool CanStart => CanRun && !IsRunning && !isTaskExecuting;

        public bool CanHome => IsRunning || IsMountConnected;

        public ICommand StartAlignmentCommand => startAlignmentCommand ??= new RelayCommand(o => {
            if (!IsRunning && !isTaskExecuting) {
                StartAlignment();
            }
        });

        private ICommand stopAlignmentCommand;
        public ICommand StopAlignmentCommand => stopAlignmentCommand ??= new RelayCommand(o => {
            StopAlignment();
        });

        public void StopAlignment() {
            if (IsRunning) {
                IsRunning = false;
                Log("Stop requested by user. Aborting alignment sequence...");
                try {
                    alignmentCts?.Cancel();
                } catch { }
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
            } else if (IsMountConnected && !isTaskExecuting) {
                isTaskExecuting = true;
                RaisePropertyChanged(nameof(CanStart));
                Task.Run(async () => {
                    SetStatus("Homing", StatusWarningColor);
                    try {
                        if (homePosition != null) {
                            Log($"Slewing back to verified Home Position (RA: {homePosition.RAString}, Dec: {homePosition.DecString}) as requested by HOME button...");
                            await ExecuteHardwareOperationAsync(() => telescopeMediator.SlewToCoordinatesAsync(homePosition, System.Threading.CancellationToken.None), System.Threading.CancellationToken.None, "Slew to Home");
                        } else {
                            Log("No previous Home state recorded yet. Dispatching native FindHome command to mount controller...");
                            // Pass default status tracking progress and no explicit token cancellation
                            await ExecuteHardwareOperationAsync(() => telescopeMediator.FindHome(new Progress<global::NINA.Core.Model.ApplicationStatus>(), System.Threading.CancellationToken.None), System.Threading.CancellationToken.None, "Find Home");
                        }
                        try { telescopeMediator.SetTrackingEnabled(false); } catch { }
                        Log("Successfully returned to Home Position.");
                    } catch (Exception ex) {
                        Log($"[Error] Failed to complete Home directive: {ex.Message}");
                    } finally {
                        SetStatus("Ready to Start", StatusIdleColor);
                        isTaskExecuting = false;
                        RaisePropertyChanged(nameof(CanStart));
                    }
                });
            }
        }

        public void Log(string message) {
            Logs += $"\n[{DateTime.Now:HH:mm:ss}] {message}";
            global::NINA.Core.Utility.Logger.Info($"[2-Point Polar Alignment] {message}");
        }

        private void ShowManualRotationDialog(double targetDegrees, RotationDirection direction, Coordinates initialCoords, CaptureSequence sequence, ICaptureSolver captureSolver, bool isSimulation) {
            var dialog = new System.Windows.Window {
                Title = "Manual RA Rotation Live Tracking",
                Width = 500,
                Height = 420,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                WindowStyle = System.Windows.WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false
            };

            try {
                dialog.Owner = System.Windows.Application.Current.MainWindow;
            } catch { }

            // Main container with drop shadow
            var mainBorder = new System.Windows.Controls.Border {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x22)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x50)),
                BorderThickness = new System.Windows.Thickness(1),
                CornerRadius = new System.Windows.CornerRadius(12),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 30, ShadowDepth = 0, Opacity = 0.6 }
            };

            var grid = new System.Windows.Controls.Grid();
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(45) }); // Header
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) }); // Main Body
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto }); // Sim Bar

            // 1. Header
            var header = new System.Windows.Controls.Border {
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
                CornerRadius = new System.Windows.CornerRadius(12, 12, 0, 0),
                Padding = new System.Windows.Thickness(20, 0, 20, 0)
            };
            var headerTxt = new System.Windows.Controls.TextBlock {
                Text = "⟳  Manual RA Rotation — Live Tracking",
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                FontSize = 16,
                FontWeight = System.Windows.FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White
            };
            header.Child = headerTxt;
            System.Windows.Controls.Grid.SetRow(header, 0);
            grid.Children.Add(header);

            // 2. Content Body
            var bodyStack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(25, 20, 25, 15) };

            var instrText = new System.Windows.Controls.TextBlock {
                Text = $"Rotate the mount {direction} to the target angle. Tighten clutches, then click Finish.",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                TextAlignment = System.Windows.TextAlignment.Center,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                Margin = new System.Windows.Thickness(0, 0, 0, 15)
            };
            bodyStack.Children.Add(instrText);

            // Rotation Status Plate
            var statusPlate = new System.Windows.Controls.Border {
                Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x18)),
                CornerRadius = new System.Windows.CornerRadius(8),
                Padding = new System.Windows.Thickness(15),
                BorderThickness = new System.Windows.Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x36)),
                Margin = new System.Windows.Thickness(0, 0, 0, 20)
            };
            var statusStack = new System.Windows.Controls.StackPanel();

            var currentAnglePanel = new System.Windows.Controls.StackPanel { 
                Orientation = System.Windows.Controls.Orientation.Horizontal, 
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center 
            };
            var curAngleTxt = new System.Windows.Controls.TextBlock {
                Text = "0.0°",
                FontSize = 42,
                FontWeight = System.Windows.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x4C, 0x4C)) // Start Red
            };
            var targetAngleTxt = new System.Windows.Controls.TextBlock {
                Text = $" / {targetDegrees:F1}°",
                FontSize = 24,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.Gray,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Margin = new System.Windows.Thickness(5, 0, 0, 8)
            };
            currentAnglePanel.Children.Add(curAngleTxt);
            currentAnglePanel.Children.Add(targetAngleTxt);
            statusStack.Children.Add(currentAnglePanel);

            var liveStatusTxt = new System.Windows.Controls.TextBlock {
                Text = "Waiting for first capture...",
                FontSize = 11,
                FontStyle = System.Windows.FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x99)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new System.Windows.Thickness(0, 5, 0, 10)
            };
            statusStack.Children.Add(liveStatusTxt);

            var progressBar = new System.Windows.Controls.ProgressBar {
                Height = 10,
                Minimum = 0,
                Maximum = targetDegrees,
                Value = 0,
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x28)),
                BorderThickness = new System.Windows.Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x4C, 0x4C))
            };
            statusStack.Children.Add(progressBar);
            statusPlate.Child = statusStack;
            bodyStack.Children.Add(statusPlate);

            // Finish Button
            var finishButton = new System.Windows.Controls.Button {
                Content = "Finish — Clutches Locked",
                Height = 45,
                FontSize = 16,
                FontWeight = System.Windows.FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            // Re-use premium button template logic
            var btnTemplate = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button));
            var btnBdr = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            btnBdr.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new System.Windows.CornerRadius(6));
            btnBdr.SetValue(System.Windows.Controls.Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)));
            btnBdr.Name = "pnlBdr";
            var gridFact = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Grid));
            var glowFact = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            glowFact.SetValue(System.Windows.Controls.Border.BackgroundProperty, System.Windows.Media.Brushes.White);
            glowFact.SetValue(System.Windows.UIElement.OpacityProperty, 0.0);
            glowFact.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new System.Windows.CornerRadius(6));
            glowFact.Name = "glow";
            var cpFact = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
            cpFact.SetValue(System.Windows.FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            cpFact.SetValue(System.Windows.FrameworkElement.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
            gridFact.AppendChild(glowFact);
            gridFact.AppendChild(cpFact);
            btnBdr.AppendChild(gridFact);
            btnTemplate.VisualTree = btnBdr;
            var hTrig = new System.Windows.Trigger { Property = System.Windows.UIElement.IsMouseOverProperty, Value = true };
            hTrig.Setters.Add(new System.Windows.Setter(System.Windows.UIElement.OpacityProperty, 0.15, "glow"));
            btnTemplate.Triggers.Add(hTrig);
            finishButton.Template = btnTemplate;
            finishButton.Click += (s, e) => dialog.DialogResult = true;
            bodyStack.Children.Add(finishButton);

            System.Windows.Controls.Grid.SetRow(bodyStack, 1);
            grid.Children.Add(bodyStack);

            // 3. Simulation Tool Bar
            if (isSimulation) {
                var simBar = new System.Windows.Controls.Border {
                    Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x1A, 0x1A)),
                    Padding = new System.Windows.Thickness(10, 8, 10, 8),
                    CornerRadius = new System.Windows.CornerRadius(0, 0, 12, 12),
                    BorderThickness = new System.Windows.Thickness(0, 1, 0, 0),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x20, 0x20))
                };
                var simGrid = new System.Windows.Controls.Grid();
                simGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                simGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
                
                var simLabel = new System.Windows.Controls.TextBlock {
                    Text = "⚙️ SIMULATION DEBUG PANEL",
                    Foreground = Brushes.IndianRed,
                    FontWeight = System.Windows.FontWeights.Bold,
                    FontSize = 10,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                
                var simControlStack = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                
                var btnStep = new System.Windows.Controls.Button { Content = "Add Sim Rotation (+5°)", Padding = new System.Windows.Thickness(8, 2, 8, 2), FontSize = 10 };
                btnStep.Click += (s, e) => {
                    currentSimulationOffset += 5.0;
                    Log($"[Simulator Override] Artificial rotation incremented. Total offset: {currentSimulationOffset:F1}°");
                };
                
                var btnReset = new System.Windows.Controls.Button { Content = "Reset", Margin = new System.Windows.Thickness(5, 0, 0, 0), Padding = new System.Windows.Thickness(8, 2, 8, 2), FontSize = 10 };
                btnReset.Click += (s, e) => { currentSimulationOffset = 0.0; };
                
                simControlStack.Children.Add(btnStep);
                simControlStack.Children.Add(btnReset);
                
                System.Windows.Controls.Grid.SetColumn(simLabel, 0);
                System.Windows.Controls.Grid.SetColumn(simControlStack, 1);
                simGrid.Children.Add(simLabel);
                simGrid.Children.Add(simControlStack);
                simBar.Child = simGrid;
                
                System.Windows.Controls.Grid.SetRow(simBar, 2);
                grid.Children.Add(simBar);
            }

            mainBorder.Child = grid;
            dialog.Content = mainBorder;

            // ==================== BACKGROUND TRACKING LOOP ====================
            var cts = new System.Threading.CancellationTokenSource();
            dialog.Closed += (s, e) => cts.Cancel();

            Task.Run(async () => {
                int frameCounter = 0;
                var solveProgress = new Progress<PlateSolveProgress>(p => {
                    if (p.Thumbnail != null) {
                        System.Windows.Application.Current.Dispatcher.Invoke(() => { LastFrame = p.Thumbnail; });
                    }
                });
                var appProgress = new Progress<ApplicationStatus>();
                
                // Parse current user binning setting
                int binVal = 1;
                if (!string.IsNullOrEmpty(Binning) && Binning.Length >= 1) {
                    int.TryParse(Binning.Substring(0, 1), out binVal);
                }

                // Get setup from profile
                var profile = profileService.ActiveProfile;
                CaptureSolverParameter solverParam = new CaptureSolverParameter {
                    Attempts = 1, // Single quick shot per cycle
                    ReattemptDelay = TimeSpan.FromSeconds(1),
                    FocalLength = profile.TelescopeSettings.FocalLength,
                    PixelSize = cameraMediator.GetInfo()?.PixelSize ?? 0,
                    Binning = binVal, // Adopt user-selected binning!
                    Coordinates = initialCoords, // Use initial as starting hint for instant solve
                    BlindFailoverEnabled = true, // Keep enabled per prior agreement
                    DisableNotifications = true,
                    SearchRadius = 15.0, // Reverted to standard radius per user instruction
                    Regions = 5000.0,
                    MaxObjects = 500
                };
                
                await Task.Delay(1000); // Initial UI settling time

                while (!cts.IsCancellationRequested) {
                    try {
                        frameCounter++;
                        dialog.Dispatcher.BeginInvoke(() => { liveStatusTxt.Text = $"Capturing tracking image #{frameCounter}..."; });

                        PlateSolveResult solveResult = null;
                        
                        if (isSimulation) {
                            await Task.Delay(1500, cts.Token); // simulate camera readout
                            // Construct simulated dynamic coordinate based on cumulative manual offset
                            double offHrs = currentSimulationOffset / 15.0;
                            double currentSimRA = initialCoords.RA + (direction == RotationDirection.East ? offHrs : -offHrs);
                            if (currentSimRA < 0) currentSimRA += 24.0;
                            if (currentSimRA >= 24.0) currentSimRA -= 24.0;
                            
                            solveResult = new PlateSolveResult {
                                Success = true,
                                Coordinates = new Coordinates(currentSimRA, initialCoords.Dec, initialCoords.Epoch, Coordinates.RAType.Hours)
                            };
                        } else {
                            // Real background solve
                            // Explicitly use CancellationToken.None wrapping inside custom source to avoid killing sequence on dialog close
                            solveResult = await ExecuteHardwareOperationAsync(() => captureSolver.Solve(sequence, solverParam, solveProgress, appProgress, cts.Token), cts.Token, "Solve Capture");
                        }

                        if (solveResult != null && solveResult.Success && !cts.IsCancellationRequested) {
                            var liveCoords = solveResult.Coordinates;
                            
                            // Calculate precise Great Circle distance between M1 and Current
                            double lat1 = initialCoords.Dec * Math.PI / 180.0;
                            double lat2 = liveCoords.Dec * Math.PI / 180.0;
                            double dLon = (liveCoords.RA - initialCoords.RA) * 15.0 * Math.PI / 180.0;
                            
                            double cosDistance = Math.Sin(lat1) * Math.Sin(lat2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
                            cosDistance = Math.Clamp(cosDistance, -1.0, 1.0);
                            double distRadians = Math.Acos(cosDistance);
                            
                            // Convert spherical separation into RA Rotation degrees:
                            // Sin(Dist/2) = Cos(Dec) * Sin(Rot/2) => Sin(Rot/2) = Sin(Dist/2) / Cos(Dec)
                            double cosDec = Math.Cos(initialCoords.Dec * Math.PI / 180.0);
                            double rotDeltaDegrees = 0.0;
                            
                            if (Math.Abs(cosDec) > 0.01) { // Safety threshold near pole
                                double sinRotHalf = Math.Sin(distRadians / 2.0) / cosDec;
                                sinRotHalf = Math.Clamp(sinRotHalf, -1.0, 1.0);
                                rotDeltaDegrees = 2.0 * Math.Asin(sinRotHalf) * 180.0 / Math.PI;
                            } else {
                                // At the exact pole, the simple RA differential is the rotation
                                double simpleDiff = Math.Abs(liveCoords.RA - initialCoords.RA) * 15.0;
                                if (simpleDiff > 180.0) simpleDiff = 360.0 - simpleDiff;
                                rotDeltaDegrees = simpleDiff;
                            }

                            // UI dispatch to update progress
                            dialog.Dispatcher.BeginInvoke(() => {
                                curAngleTxt.Text = $"{rotDeltaDegrees:F1}°";
                                progressBar.Value = Math.Min(targetDegrees, rotDeltaDegrees);
                                liveStatusTxt.Text = "Tracking lock active. Last solve successful.";
                                
                                double diffToTarget = Math.Abs(rotDeltaDegrees - targetDegrees);
                                
                                // Adaptive coloring based on distance
                                if (diffToTarget < 1.0) { // Locked in!
                                    curAngleTxt.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)); // Green
                                    progressBar.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                                    liveStatusTxt.Text = "✨ Target angle reached! Ready to lock.";
                                } else if (diffToTarget < 10.0) {
                                    curAngleTxt.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCE, 0x56)); // Yellow
                                    progressBar.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCE, 0x56));
                                } else {
                                    curAngleTxt.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x4C, 0x4C)); // Red
                                    progressBar.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x4C, 0x4C));
                                }
                            });
                        } else {
                            dialog.Dispatcher.BeginInvoke(() => { liveStatusTxt.Text = "Tracking update skipped: Plate solve failed (stars might be streaked). Contining..."; });
                        }
                        
                        // Pace the loop
                        await Task.Delay(800, cts.Token);

                    } catch (OperationCanceledException) {
                        break; // Window closed, exit loop
                    } catch (Exception ex) {
                        // Fault tolerant loop - do not terminate on solve runtime error
                        dialog.Dispatcher.BeginInvoke(() => { liveStatusTxt.Text = $"Loop warning: Attempting reconnect..."; });
                        await Task.Delay(1500, cts.Token);
                    }
                }
            }, cts.Token);

            dialog.ShowDialog();
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

                var header = new System.Windows.Controls.Border {
                    Background = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
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
                    Template = CreateNinaBtnTemplate(Color.FromRgb(0x22, 0xC5, 0x5E))
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
            if (isTaskExecuting) {
                return;
            }
            isTaskExecuting = true;
            IsRunning = true;
            requestedHome = false;
            alignmentCts = new System.Threading.CancellationTokenSource();
            RaisePropertyChanged(nameof(CanStart));
            Task.Run(async () => {
                try {
                    await StartAlignmentAsync();
                } catch (OperationCanceledException) {
                    Log("Alignment sequence successfully aborted.");
                    Notification.ShowSuccess("2-Point Polar Alignment: Sequence aborted successfully!");
                } catch (Exception ex) {
                    Log($"[Error] Alignment failed: {ex.Message}");
                    Notification.ShowError($"Alignment failed: {ex.Message}");
                } finally {
                    IsRunning = false;
                    isTaskExecuting = false;
                    simTimeFactor = -1.0;
                    RaisePropertyChanged(nameof(CanStart));
                    alignmentCts?.Dispose();
                    alignmentCts = null;
                }
            });
        }

        private async Task StartAlignmentAsync() {
            var cts = alignmentCts ?? throw new InvalidOperationException("Alignment CTS was not initialized.");
            
            var controller = new NirZonshine.NINA.TwoPointPolarAlignment.Workflow.AlignmentWorkflowController(
                _profileService, cameraMediator, telescopeMediator, plateSolverFactory, imagingMediator, filterWheelMediator, _polarSolver, _settingsManager
            );
            
            controller.OnManualRotationRequested = async (targetDegrees, direction, initialCoords, sequence, captureSolver, isSimulation) => {
                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    ShowManualRotationDialog(targetDegrees, direction, initialCoords, sequence, captureSolver, isSimulation);
                });
            };
            
            controller.OnInterventionRequested = async (args) => {
                return ShowNinaStyledMessageBox(args.Title, args.Message, args.IsYesNo);
            };

            var progress = new Progress<AlignmentProgressReport>(report => {
                if (report.LogMessage != null) Log(report.LogMessage);
                if (report.StatusText != null && report.StatusColorHex != null) {
                    SetStatus(report.StatusText, CreateFrozenBrush(report.StatusColorHex));
                }
                if (report.AltitudeError != null) AltitudeError = report.AltitudeError;
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
                
                if (report.HasSuccessfulAlignmentReached) {
                    hasSuccessfulAlignmentReached = true;
                }
            });

            var context = new AlignmentWorkflowContext {
                ActiveDirection = Direction,
                ActivePreRotate = Method == RotationMethod.Automatic && StartingPoint == StartingPointMode.PreRotateHalfRange,
            };

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

