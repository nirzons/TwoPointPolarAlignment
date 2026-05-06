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

        [ImportingConstructor]
        public PolarAlignmentDockableVM(IProfileService profileService) : base(profileService) {
            Title = "2-Point Polar Alignment";
            
            // Set a beautiful telescope lens crosshair vector icon instead of a generic one
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4M11,6H13V9H11V6M11,15H13V18H11V15M6,11H9V13H6V11M15,11H18V13H15V11M12,10A2,2 0 0,0 10,12A2,2 0 0,0 12,14A2,2 0 0,0 14,12A2,2 0 0,0 12,10Z"));
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
    }
}
