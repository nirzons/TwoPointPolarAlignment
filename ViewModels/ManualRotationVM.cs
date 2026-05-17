using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using NINA.Core.Model;
using NirZonshine.NINA.TwoPointPolarAlignment.Domain;

namespace NirZonshine.NINA.TwoPointPolarAlignment.ViewModels {
    public class ManualRotationVM : INotifyPropertyChanged {
        // W-1 Fix: Pre-frozen static brushes for cross-thread safety and allocation avoidance
        private static readonly Brush GreenBrush = CreateFrozenBrush(0x22, 0xC5, 0x5E);
        private static readonly Brush YellowBrush = CreateFrozenBrush(0xFF, 0xCE, 0x56);
        private static readonly Brush RedBrush = CreateFrozenBrush(0xFF, 0x4C, 0x4C);

        private static Brush CreateFrozenBrush(byte r, byte g, byte b) {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
        public event PropertyChangedEventHandler PropertyChanged;

        private double _targetDegrees;
        private double _currentDegrees;
        private string _statusText = "Waiting for first capture...";
        private bool _isLocked;
        private string _instructionText;
        private bool _isSimulation;
        private double _simOffset;

        public double TargetDegrees {
            get => _targetDegrees;
            set { _targetDegrees = value; OnPropertyChanged(); }
        }

        public double CurrentDegrees {
            get => _currentDegrees;
            set { 
                _currentDegrees = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(DiffToTarget));
                OnPropertyChanged(nameof(CurrentAngleForeground));
                OnPropertyChanged(nameof(ProgressBarForeground));
            }
        }

        public string StatusText {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public bool IsLocked {
            get => _isLocked;
            set { _isLocked = value; OnPropertyChanged(); }
        }

        public string InstructionText {
            get => _instructionText;
            set { _instructionText = value; OnPropertyChanged(); }
        }

        public bool IsSimulation {
            get => _isSimulation;
            set { _isSimulation = value; OnPropertyChanged(); }
        }

        public double SimOffset {
            get => _simOffset;
            set { _simOffset = value; OnPropertyChanged(); }
        }

        public double DiffToTarget => Math.Abs(CurrentDegrees - TargetDegrees);

        public Brush CurrentAngleForeground {
            get {
                if (DiffToTarget < 1.0) return GreenBrush;
                if (DiffToTarget < 10.0) return YellowBrush;
                return RedBrush;
            }
        }

        public Brush ProgressBarForeground => CurrentAngleForeground;

        public ICommand FinishCommand { get; }
        public ICommand AddSimRotationCommand { get; }
        public ICommand ResetSimRotationCommand { get; }

        public event EventHandler FinishRequested;
        public event EventHandler<double> SimOffsetRequested;
        public event EventHandler SimResetRequested;

        public ManualRotationVM() {
            FinishCommand = new RelayCommand(o => FinishRequested?.Invoke(this, EventArgs.Empty));
            AddSimRotationCommand = new RelayCommand(o => {
                SimOffset += 5.0;
                SimOffsetRequested?.Invoke(this, SimOffset);
            });
            ResetSimRotationCommand = new RelayCommand(o => {
                SimOffset = 0.0;
                SimResetRequested?.Invoke(this, EventArgs.Empty);
            });
        }

        public void UpdateProgress(ManualTrackingProgress p) {
            TargetDegrees = p.TargetDegrees;
            CurrentDegrees = p.CurrentDegrees;
            StatusText = p.StatusText;
            IsLocked = p.IsLocked;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
