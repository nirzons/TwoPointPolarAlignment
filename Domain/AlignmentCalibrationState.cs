namespace NirZonshine.NINA.TwoPointPolarAlignment.Domain {
    public class AlignmentCalibrationState {
        public Vector3D InitialPolarAxis { get; }
        public Vector3D Measurement2Vector { get; }
        public double LstMeasurement2 { get; }

        public AlignmentCalibrationState(Vector3D initialPolarAxis, Vector3D measurement2Vector, double lstMeasurement2) {
            InitialPolarAxis = initialPolarAxis;
            Measurement2Vector = measurement2Vector;
            LstMeasurement2 = lstMeasurement2;
        }
    }
}
