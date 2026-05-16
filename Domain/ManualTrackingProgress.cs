using System.Windows.Media;

namespace NirZonshine.NINA.TwoPointPolarAlignment.Domain {
    public class ManualTrackingProgress {
        public double TargetDegrees { get; set; }
        public double CurrentDegrees { get; set; }
        public string StatusText { get; set; }
        public bool IsLocked { get; set; }
        public ImageSource Thumbnail { get; set; }
    }
}
