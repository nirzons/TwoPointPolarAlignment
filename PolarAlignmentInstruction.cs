using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Utility.Notification;
using NINA.Sequencer.SequenceItem;
using System;
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

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            Notification.ShowSuccess("2-Point Polar Alignment Instruction Executed (Stub)");
            return Task.CompletedTask;
        }

        public override object Clone() {
            return new PolarAlignmentInstruction(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(PolarAlignmentInstruction)}";
        }
    }
}
