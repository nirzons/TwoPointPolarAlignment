using System;
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
        private int plateSolveRetries = 5;
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
        private System.Threading.CancellationTokenSource alignmentCts;
        private Vector3D calculatedPolarAxis;
        private Vector3D measurement2Vector;
        private Vector3D initialPolarAxis;
        private double simTimeFactor = -1.0;
        private bool blinkToggle = false;
        private double currentSimulationOffset = 0.0;
        private bool enableOnePointAlignment = false;
        private bool isAltitudePriority = false;
        private bool isAzimuthPriority = false;
        private bool hasRoughFinderSimTriggered = false;
        private AltitudeKnobDirection altKnobDirection = AltitudeKnobDirection.UpArrow;
        private bool isBlindSolvingActive = false;

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

        [ImportingConstructor]
        public PolarAlignmentDockableVM(IProfileService profileService, ICameraMediator cameraMediator, ITelescopeMediator telescopeMediator, IPlateSolverFactory plateSolverFactory, IImagingMediator imagingMediator, IFilterWheelMediator filterWheelMediator) : base(profileService) {
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
            LoadSettings();
            
            if (profileService != null) {
                profileService.ProfileChanged += ProfileService_ProfileChanged;
            }
        }

        public override bool IsTool => true;
        
        public bool IsCameraConnected => (currentCameraInfo?.Connected ?? cameraMediator?.GetInfo()?.Connected ?? false);
        
        public bool IsMountConnected => (currentTelescopeInfo?.Connected ?? telescopeMediator?.GetInfo()?.Connected ?? false);

        public bool CanRun => IsCameraConnected && (Method == RotationMethod.Manual || IsMountConnected);

        public bool ShowMountWarning => !IsMountConnected && Method != RotationMethod.Manual;

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            System.Windows.Application.Current.Dispatcher.Invoke(() => {
                LoadSettings();
            });
        }

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
            get => rotationAmount;
            set {
                rotationAmount = value;
                RaisePropertyChanged(nameof(RotationAmount));
                SaveSettings();
            }
        }

        public RotationMethod Method {
            get => method;
            set {
                method = value;
                RaisePropertyChanged(nameof(Method));
                RaisePropertyChanged(nameof(ShowMountWarning));
                RaisePropertyChanged(nameof(CanRun));
                RaisePropertyChanged(nameof(CanStart));
                SaveSettings();
            }
        }

        public RotationDirection Direction {
            get => direction;
            set {
                direction = value;
                RaisePropertyChanged(nameof(Direction));
                SaveSettings();
            }
        }

        public StartingPointMode StartingPoint {
            get => startingPoint;
            set {
                startingPoint = value;
                RaisePropertyChanged(nameof(StartingPoint));
                SaveSettings();
            }
        }

        public double ExposureTime {
            get => exposureTime;
            set {
                exposureTime = value;
                RaisePropertyChanged(nameof(ExposureTime));
                SaveSettings();
            }
        }

        public int Gain {
            get => gain;
            set {
                gain = value;
                RaisePropertyChanged(nameof(Gain));
                SaveSettings();
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
                SaveSettings();
            }
        }

        public AltitudeKnobDirection AltKnobDirection {
            get => altKnobDirection;
            set {
                altKnobDirection = value;
                RaisePropertyChanged(nameof(AltKnobDirection));
                SaveSettings();
                
                // Force re-render of current guidance strings if loop is running
                if (calculatedPolarAxis.X != 0 || calculatedPolarAxis.Y != 0 || calculatedPolarAxis.Z != 0) {
                    UpdateErrorViewFromPolarAxis(calculatedPolarAxis, lastFrame == null ? 0 : recordedRA);
                }
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
            get => binning;
            set {
                binning = value;
                RaisePropertyChanged(nameof(Binning));
                SaveSettings();
            }
        }

        public int Offset {
            get => offset;
            set {
                offset = value;
                RaisePropertyChanged(nameof(Offset));
                SaveSettings();
            }
        }

        public double TelescopeMoveRate {
            get => telescopeMoveRate;
            set {
                telescopeMoveRate = value;
                RaisePropertyChanged(nameof(TelescopeMoveRate));
                SaveSettings();
            }
        }

        public int PlateSolveRetries {
            get => plateSolveRetries;
            set {
                plateSolveRetries = value;
                RaisePropertyChanged(nameof(PlateSolveRetries));
                SaveSettings();
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
            get => enableOnePointAlignment;
            set {
                enableOnePointAlignment = value;
                if (value) {
                    // User manual interaction resets the simulation trigger so it can fire exactly once more if needed
                    hasRoughFinderSimTriggered = false;
                }
                RaisePropertyChanged(nameof(EnableOnePointAlignment));
                SaveSettings();
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
                    try {
                        if (homePosition != null) {
                            Log($"Slewing back to verified Home Position (RA: {homePosition.RAString}, Dec: {homePosition.DecString}) as requested by HOME button...");
                            await telescopeMediator.SlewToCoordinatesAsync(homePosition, System.Threading.CancellationToken.None);
                        } else {
                            Log("No previous Home state recorded yet. Dispatching native FindHome command to mount controller...");
                            // Pass default status tracking progress and no explicit token cancellation
                            await telescopeMediator.FindHome(new Progress<global::NINA.Core.Model.ApplicationStatus>(), System.Threading.CancellationToken.None);
                        }
                        try { telescopeMediator.SetTrackingEnabled(false); } catch { }
                        Log("Successfully returned to Home Position.");
                    } catch (Exception ex) {
                        Log($"[Error] Failed to complete Home directive: {ex.Message}");
                    } finally {
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
                            solveResult = await captureSolver.Solve(sequence, solverParam, solveProgress, appProgress, cts.Token);
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
            var token = cts.Token;
            simTimeFactor = -1.0;
            RotationDirection activeDirection = Direction;
            bool activePreRotate = Method == RotationMethod.Automatic && StartingPoint == StartingPointMode.PreRotateHalfRange;
            bool skipHomeSlew = false;
            IsReversedFlowActive = false;
            currentSimulationOffset = 0.0;
            
            // Generate stable random misalignment biases scaled specifically to user's requested distribution factors
            var rand = new Random();
            double simDegBiasRA = (rand.NextDouble() > 0.5 ? 1.0 : -1.0) * (0.25 + rand.NextDouble() * 0.25) / 2.0; // Re-increased by 2x from previous state
            double simDegBiasDec = (rand.NextDouble() > 0.5 ? 1.0 : -1.0) * (0.25 + rand.NextDouble() * 0.25) * 3.0; // Increased by 3x
            double manualSimBiasRA = simDegBiasRA / 15.0; 
            double manualSimBiasDec = simDegBiasDec; 

            Logs = $"[{DateTime.Now:HH:mm:ss}] [System] Starting alignment sequence...";
            Log("Initiating Phase A (Pre-flight Checks)...");

            // 1. Verify Camera Connection
            bool isCameraConnected = IsCameraConnected;
            if (!isCameraConnected) {
                Log("Error: Camera is not connected!");
                Notification.ShowError("2-Point Polar Alignment Error: Camera is not connected!");
                return;
            }

            // 2. Verify Mount Connection (skipped in Manual mode — mount is not required)
            bool isMountConnected = IsMountConnected;
            if (!isMountConnected && Method != RotationMethod.Manual) {
                Log("Error: Telescope Mount is not connected!");
                Notification.ShowError("2-Point Polar Alignment Error: Telescope Mount is not connected!");
                return;
            }
            if (!isMountConnected && Method == RotationMethod.Manual) {
                Log("Manual mode active — mount connection not required. Proceeding with camera-only operation.");
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

            // Check for special restart condition: successful alignment reached, and mount has not moved since
            bool isSpecialOppositeRestart = false;
            try {
                var currentPositionCheck = telescopeMediator.GetCurrentPosition();
                if (hasSuccessfulAlignmentReached && currentPositionCheck != null && hasRecordedPosition) {
                    var infoCheck = telescopeMediator.GetInfo();
                    double lstCheck = infoCheck?.SiderealTime ?? 0;
                    double currentHA = lstCheck - currentPositionCheck.RA;
                    if (currentHA < 0) currentHA += 24.0;
                    double haDiff = Math.Abs(currentHA - recordedHA);
                    if (haDiff > 12.0) haDiff = 24.0 - haDiff;

                    double raDiff = Math.Abs(currentPositionCheck.RA - recordedRA);
                    if (raDiff > 12.0) raDiff = 24.0 - raDiff;

                    double decDiff = Math.Abs(currentPositionCheck.Dec - recordedDec);
                    if (decDiff < 0.25 && (haDiff < 0.05 || raDiff < 0.05)) {
                        isSpecialOppositeRestart = true;
                    }
                }

                if (isSpecialOppositeRestart && currentPositionCheck != null) {
                    if (wasPreviousRunReversed) {
                        Log("Special Restart detected! Previous run was reversed. Restoring true direction internally directly from current position...");
                        IsReversedFlowActive = false;
                        activeDirection = Direction;
                        skipHomeSlew = true;
                        activePreRotate = false;
                    } else {
                        Log("Special Restart detected! Mount has not moved since previous successful alignment. Executing reversed flow internally directly from current position...");
                        IsReversedFlowActive = true;
                        activeDirection = Direction == RotationDirection.East ? RotationDirection.West : RotationDirection.East;
                        skipHomeSlew = true;
                        activePreRotate = false;
                    }
                    hasExecutedBefore = true;
                } else {
                    hasSuccessfulAlignmentReached = false;
                    IsReversedFlowActive = false;
                }
            } catch {
                hasSuccessfulAlignmentReached = false;
            }

            // Save original tracking state and disable tracking for polar alignment
            bool originalTracking = false;
            if (IsMountConnected) {
                try {
                    originalTracking = telescopeMediator.GetInfo()?.TrackingEnabled ?? false;
                    if (originalTracking) {
                        telescopeMediator.SetTrackingEnabled(false);
                        Log("Disabling telescope tracking for polar alignment sequence.");
                    }
                } catch { }
            }

            try {
                // ==================== PHASE B: Initial Positioning & Verification ====================
            bool isSimulation = (cameraMediator.GetInfo()?.Name?.Contains("Simulator", StringComparison.OrdinalIgnoreCase) ?? false) ||
                                (telescopeMediator.GetInfo()?.Name?.Contains("Simulator", StringComparison.OrdinalIgnoreCase) ?? false);

            currentSimulationOffset = 0.0;
            if (isSimulation) {
                Log("Simulator detected! Running in Simulation Mode.");
            }

            Log("Initiating Phase B (Initial Positioning & Verification)...");

            var currentPosition = IsMountConnected ? telescopeMediator.GetCurrentPosition() : null;
            if (currentPosition == null && Method == RotationMethod.Automatic) {
                throw new InvalidOperationException("Could not retrieve current telescope position for Automatic mode.");
            }

            if (Method == RotationMethod.Automatic) {
                if (IsReversedFlowActive || skipHomeSlew) {
                    Log($"Special Restart verified at current position (RA: {currentPosition.RAString}, Dec: {currentPosition.DecString}). Starting fresh alignment run directly from here.");
                } else if (!hasExecutedBefore) {
                    bool isNearPole = Math.Abs(Math.Abs(currentPosition.Dec) - 90.0) < 1.0;
                    if (!isNearPole) {
                        string errMsg = "Telescope is not in the home position. Please move the telescope to the home position first.";
                        Log($"Error: {errMsg}");
                        throw new InvalidOperationException(errMsg);
                    }

                    homePosition = new Coordinates(currentPosition.RA, currentPosition.Dec, currentPosition.Epoch, Coordinates.RAType.Hours);
                    RaisePropertyChanged(nameof(CanHome));
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

                    bool isNearHome = false;
                    if (homePosition != null) {
                        double raDiff = Math.Abs(currentRA - homePosition.RA);
                        if (raDiff > 12.0) raDiff = 24.0 - raDiff;
                        isNearHome = Math.Abs(currentDec - homePosition.Dec) < 1.0 && raDiff < (1.0 / 15.0);
                    } else {
                        isNearHome = Math.Abs(Math.Abs(currentDec) - 90.0) < 1.0;
                    }

                    if (isNearHome) {
                        if (homePosition == null) {
                            homePosition = new Coordinates(currentRA, currentDec, currentPosition?.Epoch ?? Epoch.JNOW, Coordinates.RAType.Hours);
                            RaisePropertyChanged(nameof(CanHome));
                        }
                        hasRecordedPosition = false;
                        Log($"Telescope verified at Home position (RA: {homePosition.RAString}, Dec: {homePosition.DecString}). Starting a fresh alignment run.");
                    } else {
                        double haDiff = Math.Abs(currentHA - recordedHA);
                        if (haDiff > 12.0) haDiff = 24.0 - haDiff;

                        double raDiff = Math.Abs(currentRA - recordedRA);
                        if (raDiff > 12.0) raDiff = 24.0 - raDiff;

                        double toleranceHours = 0.25 / 15.0;
                        bool isValidRetry = hasRecordedPosition && 
                                            Math.Abs(currentDec - recordedDec) < 0.25 && 
                                            (haDiff < toleranceHours || raDiff < toleranceHours);

                        if (isValidRetry) {
                            Log($"Valid retry detected (Dec: {currentDec:F2}°, RA: {currentRA:F4}h, HA: {currentHA:F2}h matches recorded Dec: {recordedDec:F2}°, RA: {recordedRA:F4}h, HA: {recordedHA:F4}h).");
                        }

                        if (homePosition != null) {
                            Log($"Telescope is not at the home position. Automatically slewing back to verified Home Position (RA: {homePosition.RAString}, Dec: {homePosition.DecString})...");
                            await telescopeMediator.SlewToCoordinatesAsync(homePosition, CancellationToken.None);
                            try { telescopeMediator.SetTrackingEnabled(false); } catch { }
                            Log("Successfully returned to Home Position.");
                            currentPosition = homePosition;
                        } else {
                            string errMsg = "Telescope has been moved from its recorded position. Please move the telescope to the home position and start again.";
                            Log($"Error: {errMsg}");
                            hasExecutedBefore = false;
                            hasRecordedPosition = false;
                            throw new InvalidOperationException(errMsg);
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
                await filterWheelMediator.ChangeFilter(targetFilterInfo, token, filterProgress);
                Log($"Filter successfully changed to {Filter}.");
            }

            if (!IsRunning) throw new OperationCanceledException("Alignment sequence was stopped by user.");
            // 1. Mount Slewing (Pre-rotation if requested)
            if (activePreRotate) {
                double offsetDegrees = RotationAmount / 2.0;
                double offsetHours = offsetDegrees / 15.0;
                Log($"Pre-rotating RA by {offsetDegrees:F1}° in the opposite direction of {activeDirection}...");

                double targetRA = currentPosition.RA + (activeDirection == RotationDirection.East ? -offsetHours : offsetHours);
                if (targetRA < 0) targetRA += 24.0;
                if (targetRA >= 24.0) targetRA -= 24.0;

                Coordinates targetCoords = new Coordinates(targetRA, currentPosition.Dec, currentPosition.Epoch, Coordinates.RAType.Hours);
                await telescopeMediator.SlewToCoordinatesAsync(targetCoords, token);
                try { telescopeMediator.SetTrackingEnabled(false); } catch { }
                Log("Pre-rotation completed successfully.");
            } else {
                Log("Starting at current/home position.");
            }


            // ==================== PHASE C: First Measurement ====================
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

            PlateSolveResult result = null;
            int maxAttempts = PlateSolveRetries;

            while (true) {
                Log("Initiating Phase C (First Measurement)...");

                // 2. Camera Exposure and Plate Solving
                Log("Capturing starting image and solving...");
                result = null; // reset for attempt loop
                for (int attempt = 1; attempt <= maxAttempts; attempt++) {
                    if (!IsRunning) throw new OperationCanceledException("Alignment sequence was stopped by user.");
                    if (isSimulation) {
                        Log($"[Simulator] Simulating camera exposure and plate solve (Attempt {attempt}/{maxAttempts})...");
                        await Task.Delay(3000);
                        var simPosition = telescopeMediator.GetCurrentPosition() ?? new Coordinates(12.0, 45.0, Epoch.JNOW, Coordinates.RAType.Hours);
                        // Apply bias to raw solved coordinates to establish simulated physical polar misalignment
                        double injectedRA = simPosition.RA + manualSimBiasRA;
                        if (injectedRA < 0) injectedRA += 24.0; if (injectedRA >= 24.0) injectedRA -= 24.0;
                        
                        double injectedDec = simPosition.Dec + manualSimBiasDec;
                        if (EnableOnePointAlignment && !hasRoughFinderSimTriggered) {
                            // Intentionally inject 12° deviation ONCE to trigger rescue engine verification
                            injectedDec = (injectedDec >= 0) ? 78.0 : -78.0;
                            hasRoughFinderSimTriggered = true; // Clear trigger so loop can advance normally after rescue
                            Log("[Simulator Injection] Rough Finder enabled. Forcing solved coordinate to 12° from pole to trigger rescue intercept.");
                        }

                        result = new PlateSolveResult {
                            Success = true,
                            Coordinates = new Coordinates(injectedRA, injectedDec, simPosition.Epoch, Coordinates.RAType.Hours),
                            PositionAngle = 45.0
                        };
                        break;
                    } else {
                        var profile = profileService.ActiveProfile;
                        var plateSolveSettings = profile.GetType().GetProperty("PlateSolveSettings")?.GetValue(profile) as IPlateSolveSettings;
                        if (plateSolveSettings == null) {
                            throw new InvalidOperationException("Could not retrieve active plate solve settings.");
                        }

                        IPlateSolver solver = plateSolverFactory.GetPlateSolver(plateSolveSettings);
                        ICaptureSolver captureSolver = plateSolverFactory.GetCaptureSolver(solver, null, imagingMediator, filterWheelMediator);

                        currentPosition = IsMountConnected ? telescopeMediator.GetCurrentPosition() : null;
                        CaptureSolverParameter solverParam = new CaptureSolverParameter {
                            Attempts = 1,
                            ReattemptDelay = TimeSpan.FromSeconds(2),
                            FocalLength = profile.TelescopeSettings.FocalLength,
                            PixelSize = cameraMediator.GetInfo()?.PixelSize ?? 0,
                            Binning = binVal,
                            SearchRadius = 15.0,
                            Regions = 5000.0,
                            MaxObjects = 500,
                            Coordinates = currentPosition,
                            BlindFailoverEnabled = true,
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
                            result = await captureSolver.Solve(sequence, solverParam, solveProgress, appProgress, token);
                            if (result != null && result.Success) {
                                break;
                            }
                        } catch (Exception ex) {
                            global::NINA.Core.Utility.Logger.Error($"[2-Point Polar Alignment] Internal solve error (Attempt {attempt}): {ex.Message}");
                        }
                    }

                    if (attempt < maxAttempts) {
                        Log($"[Warning] Plate solve attempt {attempt}/{maxAttempts} failed. Retrying capture and solve on same location...");
                        await Task.Delay(1000);
                    }
                }

                bool phaseCFailed = (result == null || !result.Success);
                string failureReason = phaseCFailed ? $"Initial plate solve failed after {maxAttempts} consecutive attempts. Ensure exposure settings are correct." : string.Empty;

                if (!phaseCFailed) {
                    coordinates1 = result.Coordinates;
                    angle1 = result.PositionAngle;

                    double distanceToPole = 90.0 - Math.Abs(coordinates1.Dec);
                    if (distanceToPole > 10.0) {
                        phaseCFailed = true;
                        failureReason = $"Telescope is pointed too far ({distanceToPole:F1}°) from the celestial pole. Limit is 10.0° for automatic alignment.";
                    }
                }

                if (phaseCFailed) {
                    if (await AttemptRescueIfNeeded(failureReason, token)) {
                        Log("Rough Finder Rescue cycle completed successfully. Sequence halted per operator feedback so user can initialize final high-precision run manually.");
                        return; // Do not auto-continue; stop cycle gracefully.
                    } else {
                        throw new InvalidOperationException(failureReason);
                    }
                }

                Log($"Solved initial coordinates successfully: RA {coordinates1.RAString}, Dec {coordinates1.DecString}, Orientation Angle: {angle1:F2}°");

                // Safety check: verify solved coordinates are not too far from the mount's physical position
                var physicalPos1 = IsMountConnected ? telescopeMediator.GetCurrentPosition() : null;
                if (Method == RotationMethod.Automatic && physicalPos1 != null) {
                    double safetyThreshold = 10.0; // simplified hard threshold as requested

                    double lat1 = physicalPos1.Dec * Math.PI / 180.0;
                    double lat2 = coordinates1.Dec * Math.PI / 180.0;
                    double dLon = (physicalPos1.RA - coordinates1.RA) * 15.0 * Math.PI / 180.0;

                    double cosTheta = Math.Sin(lat1) * Math.Sin(lat2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
                    cosTheta = Math.Clamp(cosTheta, -1.0, 1.0);
                    double separation = Math.Acos(cosTheta) * 180.0 / Math.PI;

                    Log($"Solver/Mount Separation (M1): {separation:F2}° (Safety Threshold: {safetyThreshold:F1}°).");

                    if (separation > safetyThreshold) {
                        string errMsg = $"Safety Intercept: The plate solved position is too far from the mount's reported position ({separation:F2}° separation exceeds the {safetyThreshold:F1}° safety threshold). Alignment aborted for safety.";
                        Log($"Error: {errMsg}");
                        throw new InvalidOperationException(errMsg);
                    }
                }

                break; // Phase C condition established
            }

            Log("Saved Measurement 1 -> RA 1, Dec 1, Angle 1.");
            Notification.ShowSuccess("2-Point Polar Alignment: Phase C (First Measurement) completed successfully!");

            await Task.Delay(1000);

            // ==================== PHASE D: The Rotation ====================
            Log("Initiating Phase D (The Rotation)...");

            if (!IsRunning) throw new OperationCanceledException("Alignment sequence was stopped by user.");
            // Universally calculate theoretical destination coordinates to drive slewing AND plate solver hints
            // We explicitly use CURRENT PHYSICAL mount positions where available to ensure the Dec parameter
            // supplied in the Slew command exactly matches the mount's current state, preventing accidental Dec axis motion.
            var currentPhysicalBase = IsMountConnected ? telescopeMediator.GetCurrentPosition() : null;
            var rotBasePos = currentPhysicalBase ?? coordinates1;
            double rotOffsetHours = RotationAmount / 15.0;
            double rotTargetRA = rotBasePos.RA + (activeDirection == RotationDirection.East ? rotOffsetHours : -rotOffsetHours);
            if (rotTargetRA < 0) rotTargetRA += 24.0;
            if (rotTargetRA >= 24.0) rotTargetRA -= 24.0;
            Coordinates rotTargetCoords = new Coordinates(rotTargetRA, rotBasePos.Dec, rotBasePos.Epoch, Coordinates.RAType.Hours);

            if (Method == RotationMethod.Manual) {
                Log($"[Manual Rotation] Initializing Live Tracking subsystems...");
                ICaptureSolver trackingSolver = null;
                if (!isSimulation) {
                    try {
                        var profile = profileService.ActiveProfile;
                        var plateSolveSettings = profile.GetType().GetProperty("PlateSolveSettings")?.GetValue(profile) as IPlateSolveSettings;
                        if (plateSolveSettings != null) {
                            IPlateSolver solver = plateSolverFactory.GetPlateSolver(plateSolveSettings);
                            trackingSolver = plateSolverFactory.GetCaptureSolver(solver, null, imagingMediator, filterWheelMediator);
                        }
                    } catch (Exception solverEx) {
                        Log($"[Warning] Could not pre-initialize background solver: {solverEx.Message}. Attempting to run without live updates.");
                    }
                }

                Log($"Launching Live Tracking interface for {RotationAmount:F1}° {activeDirection} rotation.");
                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    ShowManualRotationDialog(RotationAmount, activeDirection, coordinates1, sequence, trackingSolver, isSimulation);
                });
                Log($"Manual rotation tracking concluded. Recorded simulation baseline: {currentSimulationOffset:F1}°");
            } else {
                Log($"Automatically slewing RA axis by {RotationAmount:F1}° {activeDirection} to theoretical target RA {rotTargetCoords.RAString}...");
                await telescopeMediator.SlewToCoordinatesAsync(rotTargetCoords, token);
                try { telescopeMediator.SetTrackingEnabled(false); } catch { }
                Log("Automatic rotation completed successfully.");
            }

            await Task.Delay(1000);

            // ==================== PHASE E: Second Measurement ====================
            Log("Initiating Phase E (Second Measurement)...");

            result = null;
            for (int attempt = 1; attempt <= maxAttempts; attempt++) {
                if (!IsRunning) throw new OperationCanceledException("Alignment sequence was stopped by user.");
                if (isSimulation) {
                    Log($"[Simulator] Generating virtual exposure for Measurement 2 (Attempt {attempt}/{maxAttempts})...");
                    await Task.Delay(3000);
                    var simPosition = telescopeMediator.GetCurrentPosition() ?? new Coordinates(12.0, 45.0, Epoch.JNOW, Coordinates.RAType.Hours);
                    
                    // Add constant bias for simulated alignment drift across both methods
                    double simRA = simPosition.RA + manualSimBiasRA;
                    if (simRA < 0) simRA += 24.0; if (simRA >= 24.0) simRA -= 24.0;
                    double simDec = simPosition.Dec + manualSimBiasDec;

                    if (Method == RotationMethod.Manual) {
                        // In manual mode, base strictly off M1 (which carries our offset) rotated by simulated manual travel
                        double baseRA = coordinates1?.RA ?? simPosition.RA;
                        double baseDec = coordinates1?.Dec ?? simPosition.Dec;
                        double offsetHrs = currentSimulationOffset / 15.0;
                        
                        simRA = baseRA + (activeDirection == RotationDirection.East ? offsetHrs : -offsetHrs);
                        if (simRA < 0) simRA += 24.0;
                        if (simRA >= 24.0) simRA -= 24.0;
                        
                        simDec = baseDec;
                        Log($"[Simulator Override] Derived M2 via rotation matrix relative to static M1 bias.");
                    }

                    result = new PlateSolveResult {
                        Success = true,
                        Coordinates = new Coordinates(simRA, simDec, simPosition.Epoch, Coordinates.RAType.Hours),
                        PositionAngle = angle1
                    };
                    break;
                } else {
                    Log($"Capturing second image and solving (Attempt {attempt}/{maxAttempts})...");
                    var profile = profileService.ActiveProfile;
                    var plateSolveSettings = profile.GetType().GetProperty("PlateSolveSettings")?.GetValue(profile) as IPlateSolveSettings;
                    if (plateSolveSettings == null) {
                        throw new InvalidOperationException("Could not retrieve active plate solve settings.");
                    }

                    IPlateSolver solver = plateSolverFactory.GetPlateSolver(plateSolveSettings);
                    ICaptureSolver captureSolver = plateSolverFactory.GetCaptureSolver(solver, null, imagingMediator, filterWheelMediator);

                    CaptureSolverParameter solverParam = new CaptureSolverParameter {
                        Attempts = 1,
                        ReattemptDelay = TimeSpan.FromSeconds(2),
                        FocalLength = profile.TelescopeSettings.FocalLength,
                        PixelSize = cameraMediator.GetInfo()?.PixelSize ?? 0,
                        Binning = binVal,
                        SearchRadius = 15.0,
                        Regions = 5000.0,
                        MaxObjects = 500,
                        Coordinates = rotTargetCoords,
                        BlindFailoverEnabled = true,
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
                        result = await captureSolver.Solve(sequence, solverParam, solveProgress, appProgress, token);
                        if (result != null && result.Success) {
                            break;
                        }
                    } catch (Exception ex) {
                        global::NINA.Core.Utility.Logger.Error($"[2-Point Polar Alignment] Internal solve error (Attempt {attempt}): {ex.Message}");
                    }
                }

                if (attempt < maxAttempts) {
                    Log($"[Warning] Plate solve attempt {attempt}/{maxAttempts} failed. Retrying capture and solve on same location...");
                    await Task.Delay(1000);
                }
            }

            if (result == null || !result.Success) {
                throw new InvalidOperationException($"Second plate solve failed after {maxAttempts} consecutive attempts. Ensure exposure settings are correct, focus is good, and stars are visible.");
            }

            coordinates2 = result.Coordinates;
            angle2 = result.PositionAngle;
            lstMeasurement2 = telescopeMediator.GetInfo()?.SiderealTime ?? 0.0;
            if (lstMeasurement2 == 0.0) lstMeasurement2 = coordinates2.RA;

            Log($"Solved second coordinates successfully: RA {coordinates2.RAString}, Dec {coordinates2.DecString}, Orientation Angle: {angle2:F2}°");

            // Safety check: verify second solved coordinates are not too far from the mount's physical position
            var physicalPos2 = IsMountConnected ? telescopeMediator.GetCurrentPosition() : null;
            if (Method == RotationMethod.Automatic && physicalPos2 != null) {
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

            // ==================== PHASE F: Calculation & Interactive Adjustment ====================
            Log("Initiating Phase F (Calculation & Interactive Adjustment)...");
            IsRunning = true;
            hasSuccessfulAlignmentReached = true;

            // Perform initial calculation
            CalculateErrors(coordinates1, angle1, coordinates2, angle2, isInitial: true);

            // Start fast 1-second cycle rating text pulsing task
            var blinkCts = alignmentCts; // Capture token before it can be nulled
            _ = Task.Run(async () => {
                try {
                    while (!blinkCts.Token.IsCancellationRequested) {
                        await Task.Delay(500, blinkCts.Token); // 500ms bright, 500ms dim (1s cycle)
                        blinkToggle = !blinkToggle;
                        if (TotalErrorValue <= 1.0) {
                            var ratingBrush = new SolidColorBrush(blinkToggle ? Color.FromRgb(0x00, 0xFF, 0x7F) : Color.FromArgb(0x33, 0x00, 0xFF, 0x7F));
                            ratingBrush.Freeze();
                            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => {
                                TotalErrorRatingColor = ratingBrush;
                            });
                        }
                    }
                } catch (OperationCanceledException) {
                    // Expected on stop — exit cleanly
                } catch (Exception ex) {
                    global::NINA.Core.Utility.Logger.Error($"[2-Point Polar Alignment] Blink task error: {ex.Message}");
                }
            }, blinkCts.Token);

            // Enter live loop
            int simStep = 0;
            while (IsRunning) {
                Log("Capturing live adjustment frame...");

                PlateSolveResult liveResult = null;
                if (isSimulation) {
                    await Task.Delay(2000);
                    simStep++;
                    simTimeFactor = Math.Max(0.05, 1.0 - (simStep / 22.5) * 0.95);

                    var simPos = telescopeMediator.GetCurrentPosition() ?? new Coordinates(12.0, 45.0, Epoch.JNOW, Coordinates.RAType.Hours);
                    // Continuous constant physical misalignment bias
                    double baseSimRA = simPos.RA + manualSimBiasRA;
                    if (baseSimRA < 0) baseSimRA += 24.0; if (baseSimRA >= 24.0) baseSimRA -= 24.0;
                    double baseSimDec = simPos.Dec + manualSimBiasDec;
                    
                    if (Method == RotationMethod.Manual) {
                        double startRA = coordinates1?.RA ?? simPos.RA;
                        double startDec = coordinates1?.Dec ?? simPos.Dec;
                        double offHrs = currentSimulationOffset / 15.0;
                        
                        baseSimRA = startRA + (activeDirection == RotationDirection.East ? offHrs : -offHrs);
                        if (baseSimRA < 0) baseSimRA += 24.0;
                        if (baseSimRA >= 24.0) baseSimRA -= 24.0;
                        
                        baseSimDec = startDec;
                    }

                    liveResult = new PlateSolveResult {
                        Success = true,
                        Coordinates = new Coordinates(
                            baseSimRA + (new Random().NextDouble() - 0.5) * 0.015, // Intensified random solved jitter
                            baseSimDec + (new Random().NextDouble() - 0.5) * 0.015,
                            Epoch.JNOW,
                            Coordinates.RAType.Hours
                        ),
                        PositionAngle = angle2
                    };
                } else {
                    var profile = profileService.ActiveProfile;
                    var plateSolveSettings = profile.GetType().GetProperty("PlateSolveSettings")?.GetValue(profile) as IPlateSolveSettings;
                    if (plateSolveSettings == null) {
                        throw new InvalidOperationException("Could not retrieve active plate solve settings.");
                    }

                    IPlateSolver solver = plateSolverFactory.GetPlateSolver(plateSolveSettings);
                    ICaptureSolver captureSolver = plateSolverFactory.GetCaptureSolver(solver, null, imagingMediator, filterWheelMediator);

                    currentPosition = IsMountConnected ? telescopeMediator.GetCurrentPosition() : null;
                    // In Manual/mountless mode, use last solved coordinates as the solver hint
                    var liveHintCoords = currentPosition ?? coordinates2;
                    CaptureSolverParameter solverParam = new CaptureSolverParameter {
                        Attempts = 1,
                        ReattemptDelay = TimeSpan.FromSeconds(2),
                        FocalLength = profile.TelescopeSettings.FocalLength,
                        PixelSize = cameraMediator.GetInfo()?.PixelSize ?? 0,
                        Binning = binVal,
                        SearchRadius = 15.0,
                        Regions = 5000.0,
                        MaxObjects = 500,
                        Coordinates = liveHintCoords,
                        BlindFailoverEnabled = (Method == RotationMethod.Manual),
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
                        liveResult = await captureSolver.Solve(sequence, solverParam, solveProgress, appProgress, token);
                    } catch (Exception ex) {
                        Log($"[Warning] Live solve failed: {ex.Message}. Retrying...");
                    }
                }

                if (liveResult != null && liveResult.Success) {
                    // Update calculation using new coordinates
                    CalculateErrors(coordinates1, angle1, liveResult.Coordinates, liveResult.PositionAngle, isInitial: false);
                } else {
                    Log("Could not solve live frame. Retrying on next loop...");
                }

                // Small delay between loops to prevent CPU spiking and allow responsive cancellation
                for (int i = 0; i < 10 && IsRunning; i++) {
                    await Task.Delay(100);
                }
            }

            Log("Alignment live loop finished. Cleaned up successfully.");
            Notification.ShowSuccess("2-Point Polar Alignment: Alignment completed successfully!");
            }
            finally {
                wasPreviousRunReversed = IsReversedFlowActive;
                IsReversedFlowActive = false;

                try {
                    telescopeMediator.SetTrackingEnabled(false);
                    Log("Disabled telescope tracking to ensure mount remains stationary.");
                } catch { }

                if (requestedHome && Method == RotationMethod.Automatic && homePosition != null) {
                    try {
                        var currentPos = telescopeMediator.GetCurrentPosition();
                        if (currentPos != null) {
                            double raDiff = Math.Abs(currentPos.RA - homePosition.RA);
                            if (raDiff > 12.0) raDiff = 24.0 - raDiff;
                            if (raDiff > 0.05 || Math.Abs(currentPos.Dec - homePosition.Dec) > 0.05) {
                                Log($"Slewing back to verified Home Position (RA: {homePosition.RAString}, Dec: {homePosition.DecString}) as requested by HOME button...");
                                await telescopeMediator.SlewToCoordinatesAsync(homePosition, System.Threading.CancellationToken.None);
                                try { telescopeMediator.SetTrackingEnabled(false); } catch { }
                                Log("Successfully returned to Home Position.");
                            }
                        }
                    } catch (Exception ex) {
                        global::NINA.Core.Utility.Logger.Error($"[2-Point Polar Alignment] Failed to return to home position: {ex.Message}");
                    }
                }
            }
        }

        private double GetLatitude() {
            try {
                var info = telescopeMediator?.GetInfo();
                if (info != null) {
                    var latProp = info.GetType().GetProperty("Latitude") ?? info.GetType().GetProperty("SiteLatitude");
                    if (latProp != null) {
                        return Convert.ToDouble(latProp.GetValue(info));
                    }
                }
            } catch { }

            try {
                var profile = profileService?.ActiveProfile;
                if (profile != null) {
                    var latProp = profile.GetType().GetProperty("Latitude");
                    if (latProp != null) {
                        return Convert.ToDouble(latProp.GetValue(profile));
                    }
                    var astrometrySettingsProp = profile.GetType().GetProperty("AstrometrySettings");
                    if (astrometrySettingsProp != null) {
                        var astrometrySettings = astrometrySettingsProp.GetValue(profile);
                        if (astrometrySettings != null) {
                            var latProp2 = astrometrySettings.GetType().GetProperty("Latitude");
                            if (latProp2 != null) {
                                return Convert.ToDouble(latProp2.GetValue(astrometrySettings));
                            }
                        }
                    }
                }
            } catch { }

            return 45.0; // Fallback
        }

        private async Task<bool> AttemptRescueIfNeeded(string failureReason, CancellationToken token) {
            if (!EnableOnePointAlignment) return false;
            Log($"[Intercept] Polar Alignment condition fail: {failureReason}");
            
            bool agree = ShowNinaStyledMessageBox(
                "Launch Rough Finder Rescue?",
                $"Standard 2-Point alignment requirements not met: {failureReason}\n\nWould you like to initialize the interactive Rough Finder Rescue Mode?\n\nThe application will perform a recurring tracking loop to guide you within 5 degrees of the pole.",
                isYesNo: true
            );
            
            if (agree) {
                await RunRoughFinderEngineAsync(token);
                return true;
            }
            return false;
        }

        private async Task RunRoughFinderEngineAsync(CancellationToken rescueToken) {
            Log("Initializing Rough Finder Rescue Engine Loop...");
            Notification.ShowSuccess("Rough Finder Rescue Engaged. Watch live telemetry to reach center target zone.");
            
            int binVal = 1;
            if (!string.IsNullOrEmpty(Binning) && Binning.Length >= 1) int.TryParse(Binning.Substring(0, 1), out binVal);

            CaptureSequence seq = new CaptureSequence {
                ExposureTime = ExposureTime, ImageType = "LIGHT", Gain = Gain,
                Binning = new BinningMode((short)binVal, (short)binVal),
                FilterType = new FilterInfo { Name = Filter }, Offset = Offset, Enabled = true, TotalExposureCount = 1
            };

            bool isSim = (cameraMediator.GetInfo()?.Name?.Contains("Simulator", StringComparison.OrdinalIgnoreCase) ?? false) ||
                         (telescopeMediator.GetInfo()?.Name?.Contains("Simulator", StringComparison.OrdinalIgnoreCase) ?? false);
            
            var profile = profileService.ActiveProfile;
            var ps = profile.GetType().GetProperty("PlateSolveSettings")?.GetValue(profile) as IPlateSolveSettings;
            IPlateSolver solver = plateSolverFactory.GetPlateSolver(ps);
            IPlateSolver blindSolver = plateSolverFactory.GetBlindSolver(ps);
            ICaptureSolver captureSolver = plateSolverFactory.GetCaptureSolver(solver, blindSolver, imagingMediator, filterWheelMediator);

            int simStep = 0;
            while (IsRunning) {
                Log("Rescue Loop: Capturing wide-net frame...");
                PlateSolveResult res = null;
                
                if (isSim) {
                    await Task.Delay(1500, rescueToken);
                    simStep++;
                    bool isN = GetLatitude() >= 0;
                    double sDec = isN ? Math.Min(89.95, 79.0 + simStep * 2.5) : Math.Max(-89.95, -79.0 - simStep * 2.5); // Simulates converging to the active pole
                    var p = telescopeMediator.GetCurrentPosition() ?? new Coordinates(12.0, sDec, Epoch.JNOW, Coordinates.RAType.Hours);
                    res = new PlateSolveResult { Success = true, Coordinates = new Coordinates(p.RA, sDec, Epoch.JNOW, Coordinates.RAType.Hours) };
                } else {
                    var currentPosition = IsMountConnected ? telescopeMediator.GetCurrentPosition() : null;
                    CaptureSolverParameter solverParam = new CaptureSolverParameter {
                        Attempts = 1, ReattemptDelay = TimeSpan.FromSeconds(1),
                        FocalLength = profile.TelescopeSettings.FocalLength,
                        PixelSize = cameraMediator.GetInfo()?.PixelSize ?? 0,
                        Binning = binVal,
                        SearchRadius = 30.0, // Wide radius override per roadmap
                        Regions = 5000.0, MaxObjects = 500,
                        Coordinates = currentPosition,
                        BlindFailoverEnabled = true, // Blind failover override per roadmap
                        DisableNotifications = true
                    };
                    var prog = new Progress<PlateSolveProgress>(p => { if (p.Thumbnail != null) System.Windows.Application.Current.Dispatcher.Invoke(() => { LastFrame = p.Thumbnail; }); });
                    var appStatusProg = new Progress<ApplicationStatus>(s => {
                        if (s.Status != null && (
                            s.Status.Contains("Blind", StringComparison.OrdinalIgnoreCase) || 
                            s.Status.Contains("Astrometry", StringComparison.OrdinalIgnoreCase) || 
                            s.Status.Contains("All Sky", StringComparison.OrdinalIgnoreCase) ||
                            s.Status.Contains("AllSky", StringComparison.OrdinalIgnoreCase))) {
                            System.Windows.Application.Current.Dispatcher.Invoke(() => { IsBlindSolvingActive = true; });
                        }
                    });
                    try {
                        res = await captureSolver.Solve(seq, solverParam, prog, appStatusProg, rescueToken);
                    } catch { } finally {
                        System.Windows.Application.Current.Dispatcher.Invoke(() => { IsBlindSolvingActive = false; });
                    }
                }

                if (res != null && res.Success) {
                    var currentCoords = res.Coordinates;
                    double distToPole = 90.0 - Math.Abs(currentCoords.Dec);
                    Log($"Rescue Position Lock: Dec {currentCoords.DecString} ({distToPole:F2}° from pole)");

                    Vector3D mockAxis = Vector3D.FromEquatorial(currentCoords);
                    double lat = GetLatitude();
                    if (lat >= 0 && mockAxis.Z < 0) mockAxis = new Vector3D(-mockAxis.X, -mockAxis.Y, -mockAxis.Z);
                    else if (lat < 0 && mockAxis.Z > 0) mockAxis = new Vector3D(-mockAxis.X, -mockAxis.Y, -mockAxis.Z);

                    calculatedPolarAxis = mockAxis;
                    UpdateErrorViewFromPolarAxis(calculatedPolarAxis, currentCoords.RA);

                    if (distToPole < 5.0) {
                        Log("🎯 TARGET ZONE ACQUIRED! Rescued into the < 5.0° radius region.");
                        ShowNinaStyledMessageBox(
                            "🎯 Target Zone Acquired",
                            "Rescue successful! The telescope is now within 5 degrees of the celestial pole.\n\nPlease press the standard START ALIGNMENT button again to begin the final high-precision measurement sequence."
                        );
                        break;
                    }
                } else {
                    Log("[Warning] Rescue Solver failed. Adjust scope manually toward true pole and re-check alignment.");
                }
                await Task.Delay(1000);
            }
        }

        private void CalculateErrors(Coordinates c1, double a1, Coordinates c2, double a2, bool isInitial = true) {
            Vector3D v1 = Vector3D.FromEquatorial(c1);
            Vector3D v2;

            if (isInitial) {
                v2 = Vector3D.FromEquatorial(c2);
                measurement2Vector = v2;

                // Robust 3D Rotation Matrix approach to find exact center of rotation (Mount's Polar Axis)
                // Frame 1
                Vector3D N1 = (new Vector3D(0, 0, 1) - v1.Z * v1).Normalize();
                Vector3D E1 = Vector3D.Cross(new Vector3D(0, 0, 1), v1).Normalize();
                double a1Rad = a1 * Math.PI / 180.0;
                Vector3D Y1 = (Math.Cos(a1Rad) * N1 + Math.Sin(a1Rad) * E1).Normalize();
                Vector3D X1 = Vector3D.Cross(Y1, v1).Normalize();

                // Frame 2
                Vector3D N2 = (new Vector3D(0, 0, 1) - v2.Z * v2).Normalize();
                Vector3D E2 = Vector3D.Cross(new Vector3D(0, 0, 1), v2).Normalize();
                double a2Rad = a2 * Math.PI / 180.0;
                Vector3D Y2 = (Math.Cos(a2Rad) * N2 + Math.Sin(a2Rad) * E2).Normalize();
                Vector3D X2 = Vector3D.Cross(Y2, v2).Normalize();

                // Rotation Matrix R = Frame2 * Frame1^T
                double r32 = X2.Z * X1.Y + Y2.Z * Y1.Y + v2.Z * v1.Y;
                double r23 = X2.Y * X1.Z + Y2.Y * Y1.Z + v2.Y * v1.Z;
                
                double r13 = X2.X * X1.Z + Y2.X * Y1.Z + v2.X * v1.Z;
                double r31 = X2.Z * X1.X + Y2.Z * Y1.X + v2.Z * v1.X;
                
                double r21 = X2.Y * X1.X + Y2.Y * Y1.X + v2.Y * v1.X;
                double r12 = X2.X * X1.Y + Y2.X * Y1.Y + v2.X * v1.Y;

                // The rotation axis is the eigenvector associated with eigenvalue 1, proportional to the skew-symmetric part
                Vector3D P = new Vector3D(r32 - r23, r13 - r31, r21 - r12).Normalize();

                // Ensure it points to the correct hemisphere (same as celestial pole we are aligning to)
                double siteLatitude = GetLatitude();
                if (siteLatitude >= 0 && P.Z < 0) {
                    P = new Vector3D(-P.X, -P.Y, -P.Z);
                } else if (siteLatitude < 0 && P.Z > 0) {
                    P = new Vector3D(-P.X, -P.Y, -P.Z);
                }

                calculatedPolarAxis = P;
                initialPolarAxis = calculatedPolarAxis;
            } else {
                // Correct live RA for earth's rotation since measurement 2
                double lstLive = 0.0;
                try { lstLive = telescopeMediator?.GetInfo()?.SiderealTime ?? 0.0; } catch { }
                if (lstLive == 0.0) lstLive = c2.RA; // Fallback

                double deltaLst = lstLive - lstMeasurement2;
                // Handle wrap-around
                if (deltaLst > 12.0) deltaLst -= 24.0;
                if (deltaLst < -12.0) deltaLst += 24.0;

                double correctedRa = c2.RA - deltaLst;
                if (correctedRa < 0) correctedRa += 24.0;
                if (correctedRa >= 24.0) correctedRa -= 24.0;

                Coordinates correctedC2 = new Coordinates(correctedRa, c2.Dec, c2.Epoch, Coordinates.RAType.Hours);
                v2 = Vector3D.FromEquatorial(correctedC2);

                // Live adjustment update (non-accumulative relative to initial polar axis)
                calculatedPolarAxis = (v2 - measurement2Vector + initialPolarAxis).Normalize();
            }

            UpdateErrorViewFromPolarAxis(calculatedPolarAxis, c2.RA);
        }

        private void UpdateErrorViewFromPolarAxis(Vector3D axis, double referenceRA) {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                // Convert axis to Dec and RA
                double decP = Math.Asin(Math.Clamp(axis.Z, -1.0, 1.0)) * 180.0 / Math.PI;
                double raP = Math.Atan2(axis.Y, axis.X) * 12.0 / Math.PI;
                if (raP < 0) raP += 24.0;

                // Get observer's Latitude
                double latitude = GetLatitude();
                double latRad = latitude * Math.PI / 180.0;

                // Get Local Sidereal Time (LST)
                double lst = 0.0;
                try {
                    lst = telescopeMediator?.GetInfo()?.SiderealTime ?? 0.0;
                } catch { }

                if (lst == 0.0) {
                    lst = referenceRA;
                }

                // Hour Angle of polar axis
                double haP = lst - raP;
                if (haP < 0) haP += 24.0;
                if (haP >= 24.0) haP -= 24.0;

                double decRad = decP * Math.PI / 180.0;
                double haRad = haP * 15.0 * Math.PI / 180.0;

                // Rigorous equatorial to horizontal conversion
                double sinAlt = Math.Sin(decRad) * Math.Sin(latRad) + Math.Cos(decRad) * Math.Cos(latRad) * Math.Cos(haRad);
                sinAlt = Math.Clamp(sinAlt, -1.0, 1.0);
                double altP = Math.Asin(sinAlt) * 180.0 / Math.PI;

                double cosAlt = Math.Cos(altP * Math.PI / 180.0);
                double azP = 0.0;
                if (Math.Abs(cosAlt) > 1e-6) {
                    double cosAz = (Math.Sin(decRad) * Math.Cos(latRad) - Math.Cos(decRad) * Math.Sin(latRad) * Math.Cos(haRad)) / cosAlt;
                    double sinAz = (-Math.Cos(decRad) * Math.Sin(haRad)) / cosAlt;
                    cosAz = Math.Clamp(cosAz, -1.0, 1.0);
                    azP = Math.Atan2(sinAz, cosAz) * 180.0 / Math.PI;
                    if (azP < 0) azP += 360.0;
                }

                // True Pole coordinates
                double trueAlt = Math.Abs(latitude);
                double trueAz = (latitude >= 0) ? 0.0 : 180.0;

                // Compute differences
                double altErrorDeg = altP - trueAlt;
                double azDiff = azP - trueAz;
                while (azDiff > 180.0) azDiff -= 360.0;
                while (azDiff < -180.0) azDiff += 360.0;
                double azErrorDeg = azDiff * Math.Cos(latRad);

                // Convert to arcminutes
                double altErrorArcmin = altErrorDeg * 60.0;
                double azErrorArcmin = azErrorDeg * 60.0;

                if (simTimeFactor > 0) {
                    double jitter = (new Random().NextDouble() - 0.5) * 0.5; // Visual jitter multiplier
                    altErrorArcmin = (altErrorArcmin * simTimeFactor) + jitter;
                    azErrorArcmin = (azErrorArcmin * simTimeFactor) + jitter;
                }

                // Format strings
                AltitudeError = FormatError(altErrorArcmin);
                AzimuthError = FormatError(azErrorArcmin);

                // Calculate Total Error (Euclidean norm)
                TotalErrorValue = Math.Sqrt(altErrorArcmin * altErrorArcmin + azErrorArcmin * azErrorArcmin);
                TotalError = FormatTotalError(TotalErrorValue);

                // Determine which axis is the major contributor to error (Priority Highlight)
                // Ignore tie-breaking if absolute error is practically aligned (< 0.2')
                double absAlt = Math.Abs(altErrorArcmin);
                double absAz = Math.Abs(azErrorArcmin);
                
                if (TotalErrorValue < 0.5) {
                    IsAltitudePriority = false;
                    IsAzimuthPriority = false;
                } else if (absAlt > absAz + 0.1) {
                    IsAltitudePriority = true;
                    IsAzimuthPriority = false;
                } else if (absAz > absAlt + 0.1) {
                    IsAltitudePriority = false;
                    IsAzimuthPriority = true;
                } else {
                    IsAltitudePriority = false;
                    IsAzimuthPriority = false;
                }

                // Derive directional instructions and ratings
                bool isNorthern = latitude >= 0;
                if (azErrorArcmin > 0) {
                    AzimuthInstruction = isNorthern ? "← Move Left" : "Move Right →";
                } else if (azErrorArcmin < 0) {
                    AzimuthInstruction = isNorthern ? "Move Right →" : "← Move Left";
                } else {
                    AzimuthInstruction = "Aligned";
                }

                if (altErrorArcmin > 0) {
                    // Standard "Down" direction
                    if (AltKnobDirection == AltitudeKnobDirection.UpArrow) {
                        AltitudeInstruction = "Move Down ↓";
                    } else if (AltKnobDirection == AltitudeKnobDirection.Clockwise) {
                        AltitudeInstruction = "Move Down ↺";
                    } else {
                        AltitudeInstruction = "Move Down ↻";
                    }
                } else if (altErrorArcmin < 0) {
                    // Standard "Up" direction
                    if (AltKnobDirection == AltitudeKnobDirection.UpArrow) {
                        AltitudeInstruction = "Move Up ↑";
                    } else if (AltKnobDirection == AltitudeKnobDirection.Clockwise) {
                        AltitudeInstruction = "Move Up ↻";
                    } else {
                        AltitudeInstruction = "Move Up ↺";
                    }
                } else {
                    AltitudeInstruction = "Aligned";
                }

                SolidColorBrush solidBrush;
                SolidColorBrush ratingBrush;

                if (TotalErrorValue <= 1.0) {
                    TotalErrorRating = "✨ Excellent Alignment";
                    solidBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x7F)); // Solid SpringGreen for Total Error (never blinks)
                    ratingBrush = new SolidColorBrush(blinkToggle ? Color.FromRgb(0x00, 0xFF, 0x7F) : Color.FromArgb(0x33, 0x00, 0xFF, 0x7F)); // Pulsing SpringGreen for Rating
                } else if (TotalErrorValue <= 3.0) {
                    TotalErrorRating = "🟢 Good Alignment";
                    solidBrush = new SolidColorBrush(Color.FromRgb(0x98, 0xFB, 0x98)); // PaleGreen
                    ratingBrush = solidBrush;
                } else if (TotalErrorValue <= 10.0) {
                    TotalErrorRating = "🟡 Fair Alignment";
                    solidBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)); // Gold
                    ratingBrush = solidBrush;
                } else {
                    TotalErrorRating = "🔴 Poor Alignment";
                    solidBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D)); // Light Red
                    ratingBrush = solidBrush;
                }

                solidBrush.Freeze();
                TotalErrorColor = solidBrush;

                ratingBrush.Freeze();
                TotalErrorRatingColor = ratingBrush;

                Log($"Calculated Alignment Errors: Alt {AltitudeError}, Az {AzimuthError} (Alt: {altErrorArcmin:F1}', Az: {azErrorArcmin:F1}'), Total: {TotalError} ({TotalErrorValue:F1}')");
            }));
        }

        private string FormatTotalError(double errorArcmin) {
            double absMin = Math.Abs(errorArcmin);
            int totalMinutes = (int)Math.Truncate(absMin);
            int seconds = (int)Math.Round((absMin - totalMinutes) * 60.0);
            if (seconds >= 60) {
                totalMinutes += 1;
                seconds -= 60;
            }
            int degrees = totalMinutes / 60;
            int minutes = totalMinutes % 60;

            if (degrees > 0) {
                return $"{degrees:D2}° {minutes:D2}' {seconds:D2}\"";
            }
            return $"{minutes:D2}' {seconds:D2}\"";
        }

        private string FormatError(double errorArcmin) {
            double absMin = Math.Abs(errorArcmin);
            int totalMinutes = (int)Math.Truncate(absMin);
            int seconds = (int)Math.Round((absMin - totalMinutes) * 60.0);
            if (seconds >= 60) {
                totalMinutes += 1;
                seconds -= 60;
            }
            int degrees = totalMinutes / 60;
            int minutes = totalMinutes % 60;

            string sign = errorArcmin < 0 ? "-" : "+";
            if (degrees > 0) {
                return $"{sign}{degrees:D2}° {minutes:D2}' {seconds:D2}\"";
            }
            return $"{sign}{minutes:D2}' {seconds:D2}\"";
        }

        public struct Vector3D {
            public double X;
            public double Y;
            public double Z;

            public Vector3D(double x, double y, double z) {
                X = x;
                Y = y;
                Z = z;
            }

            public static Vector3D FromEquatorial(Coordinates c) {
                double raRad = c.RA * 15.0 * Math.PI / 180.0; // RA in hours to radians
                double decRad = c.Dec * Math.PI / 180.0;      // Dec in degrees to radians
                return new Vector3D(
                    Math.Cos(decRad) * Math.Cos(raRad),
                    Math.Cos(decRad) * Math.Sin(raRad),
                    Math.Sin(decRad)
                );
            }

            public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

            public Vector3D Normalize() {
                double len = Length;
                if (len < 1e-9) return new Vector3D(0, 0, 1);
                return new Vector3D(X / len, Y / len, Z / len);
            }

            public static double Dot(Vector3D v1, Vector3D v2) {
                return v1.X * v2.X + v1.Y * v2.Y + v1.Z * v2.Z;
            }

            public static Vector3D Cross(Vector3D v1, Vector3D v2) {
                return new Vector3D(
                    v1.Y * v2.Z - v1.Z * v2.Y,
                    v1.Z * v2.X - v1.X * v2.Z,
                    v1.X * v2.Y - v1.Y * v2.X
                );
            }

            public static Vector3D operator +(Vector3D v1, Vector3D v2) {
                return new Vector3D(v1.X + v2.X, v1.Y + v2.Y, v1.Z + v2.Z);
            }

            public static Vector3D operator -(Vector3D v1, Vector3D v2) {
                return new Vector3D(v1.X - v2.X, v1.Y - v2.Y, v1.Z - v2.Z);
            }

            public static Vector3D operator *(double scalar, Vector3D v) {
                return new Vector3D(scalar * v.X, scalar * v.Y, scalar * v.Z);
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

        private void SaveSettings() {
            try {
                // Optionally delete the legacy standalone file if it exists
                string ninaFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NINA");
                string legacyFile = System.IO.Path.Combine(ninaFolder, "Plugins", "2-Point Polar Alignment", "settings.json");
                if (System.IO.File.Exists(legacyFile)) {
                    try { System.IO.File.Delete(legacyFile); } catch { }
                }

                var settingsObj = new PluginSettings {
                    ExposureTime = ExposureTime,
                    Gain = Gain,
                    RotationAmount = RotationAmount,
                    Method = Method,
                    Direction = Direction,
                    StartingPoint = StartingPoint,
                    Filter = Filter,
                    Binning = Binning,
                    Offset = Offset,
                    TelescopeMoveRate = TelescopeMoveRate,
                    PlateSolveRetries = PlateSolveRetries,
                    EnableOnePointAlignment = EnableOnePointAlignment,
                    AltKnobDirection = AltKnobDirection
                };
                
                if (profileService != null) {
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(settingsObj);
                    var pluginSettings = new global::NINA.Profile.PluginOptionsAccessor(profileService, Guid.Parse("0e9e3e58-42fc-4553-8e6e-aba061af4f54"));
                    pluginSettings.SetValueString("TwoPointPolarAlignment_Settings", json);
                }
            } catch (Exception ex) {
                global::NINA.Core.Utility.Logger.Error($"[2-Point Polar Alignment] Failed to save settings: {ex.Message}");
            }
        }

        private void LoadSettings() {
            try {
                if (profileService != null) {
                    var pluginSettings = new global::NINA.Profile.PluginOptionsAccessor(profileService, Guid.Parse("0e9e3e58-42fc-4553-8e6e-aba061af4f54"));
                    string json = pluginSettings.GetValueString("TwoPointPolarAlignment_Settings", string.Empty);
                    
                    // Fallback to legacy file if profile is empty
                    if (string.IsNullOrEmpty(json)) {
                        string ninaFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NINA");
                        string legacyFile = System.IO.Path.Combine(ninaFolder, "Plugins", "2-Point Polar Alignment", "settings.json");
                        if (System.IO.File.Exists(legacyFile)) {
                            json = System.IO.File.ReadAllText(legacyFile);
                        }
                    }

                    if (!string.IsNullOrEmpty(json)) {
                        var settingsObj = Newtonsoft.Json.JsonConvert.DeserializeObject<PluginSettings>(json);
                        if (settingsObj != null) {
                            exposureTime = settingsObj.ExposureTime;
                            RaisePropertyChanged(nameof(ExposureTime));
                            gain = settingsObj.Gain;
                            RaisePropertyChanged(nameof(Gain));
                            rotationAmount = settingsObj.RotationAmount;
                            RaisePropertyChanged(nameof(RotationAmount));
                            method = settingsObj.Method;
                            RaisePropertyChanged(nameof(Method));
                            direction = settingsObj.Direction;
                            RaisePropertyChanged(nameof(Direction));
                            startingPoint = settingsObj.StartingPoint;
                            RaisePropertyChanged(nameof(StartingPoint));
                            filter = settingsObj.Filter;
                            RaisePropertyChanged(nameof(Filter));
                            binning = settingsObj.Binning;
                            RaisePropertyChanged(nameof(Binning));
                            offset = settingsObj.Offset;
                            RaisePropertyChanged(nameof(Offset));
                            telescopeMoveRate = settingsObj.TelescopeMoveRate;
                            RaisePropertyChanged(nameof(TelescopeMoveRate));
                            plateSolveRetries = settingsObj.PlateSolveRetries;
                            RaisePropertyChanged(nameof(PlateSolveRetries));
                            enableOnePointAlignment = settingsObj.EnableOnePointAlignment;
                            RaisePropertyChanged(nameof(EnableOnePointAlignment));
                            altKnobDirection = settingsObj.AltKnobDirection;
                            RaisePropertyChanged(nameof(AltKnobDirection));
                        }
                    }
                }
            } catch (Exception ex) {
                global::NINA.Core.Utility.Logger.Error($"[2-Point Polar Alignment] Failed to load settings: {ex.Message}");
            }
        }

        public class PluginSettings {
            public double ExposureTime { get; set; } = 2.0;
            public int Gain { get; set; } = 0;
            public double RotationAmount { get; set; } = 90.0;
            public RotationMethod Method { get; set; } = RotationMethod.Automatic;
            public RotationDirection Direction { get; set; } = RotationDirection.East;
            public StartingPointMode StartingPoint { get; set; } = StartingPointMode.PreRotateHalfRange;
            public string Filter { get; set; } = "(Current)";
            public string Binning { get; set; } = "1x1";
            public int Offset { get; set; } = 0;
            public double TelescopeMoveRate { get; set; } = 3.0;
            public int PlateSolveRetries { get; set; } = 3;
            public bool EnableOnePointAlignment { get; set; } = false;
            public AltitudeKnobDirection AltKnobDirection { get; set; } = AltitudeKnobDirection.UpArrow;
        }
    }
}
