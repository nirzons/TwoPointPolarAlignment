namespace NirZonshine.NINA.TwoPointPolarAlignment.Domain {
    public class AlignmentError {
        public double AltitudeErrorArcmin { get; }
        public double AzimuthErrorArcmin { get; }
        public double TotalErrorArcmin { get; }
        public Vector3D CalculatedPolarAxis { get; }

        public AlignmentError(double altError, double azError, double totalError, Vector3D calculatedPolarAxis) {
            AltitudeErrorArcmin = altError;
            AzimuthErrorArcmin = azError;
            TotalErrorArcmin = totalError;
            CalculatedPolarAxis = calculatedPolarAxis;
        }
    }
}
