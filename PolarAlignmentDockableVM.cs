using NINA.WPF.Base.ViewModel;
using NINA.Profile.Interfaces;
using System.ComponentModel.Composition;
using System.Windows.Media;

namespace NirZonshine.NINA.TwoPointPolarAlignment {

    [Export(typeof(global::NINA.Equipment.Interfaces.ViewModel.IDockableVM))]
    public class PolarAlignmentDockableVM : DockableVM {

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

        [ImportingConstructor]
        public PolarAlignmentDockableVM(IProfileService profileService) : base(profileService) {
            Title = "2-Point Polar Alignment";
            
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
    }
}
