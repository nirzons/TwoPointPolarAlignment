using NINA.Astrometry;
using NirZonshine.NINA.TwoPointPolarAlignment.Domain;

namespace NirZonshine.NINA.TwoPointPolarAlignment.Solvers {
    public interface IPolarSolver {
        AlignmentCalibrationState Calibrate(Coordinates c1, double a1, Coordinates c2, double a2, double lstMeasurement2, double siteLatitude);
        AlignmentError EvaluateLiveError(Coordinates liveCoordinates, double lstLive, AlignmentCalibrationState calibration, double siteLatitude);
        AlignmentError CalculateErrorFromAxis(Vector3D axis, double referenceRA, double lstLive, double siteLatitude, Epoch sourceEpoch = Epoch.J2000);
    }
}
