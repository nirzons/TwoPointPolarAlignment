using System;
using NINA.Astrometry;
using NirZonshine.NINA.TwoPointPolarAlignment.Domain;

namespace NirZonshine.NINA.TwoPointPolarAlignment.Solvers {
    public class TwoPointPolarSolver : IPolarSolver {
        
        public AlignmentCalibrationState Calibrate(Coordinates c1, double a1, Coordinates c2, double a2, double lstMeasurement2, double siteLatitude) {
            Vector3D v1 = Vector3D.FromEquatorial(c1);
            Vector3D v2 = Vector3D.FromEquatorial(c2);

            // Position angles from plate solving are defined relative to celestial north (0,0,1).
            // At Dec exactly ±90°, the tangent plane frame N/E degenerates (N→0, E→0).
            // Guard: if either measurement is too close to the exact pole, the math is indeterminate.
            if (Math.Abs(v1.Z) > 0.99999 || Math.Abs(v2.Z) > 0.99999) {
                throw new InvalidOperationException(
                    "Plate solve returned coordinates too close to the exact celestial pole (|Dec| > 89.999°). " +
                    "The solver math requires observations slightly away from the pole. " +
                    "If using a simulator, ensure the simulated bias moves coordinates off the exact pole.");
            }

            Vector3D N1 = (new Vector3D(0, 0, 1) - v1.Z * v1).Normalize();
            Vector3D E1 = Vector3D.Cross(new Vector3D(0, 0, 1), v1).Normalize();
            double a1Rad = a1 * System.Math.PI / 180.0;
            Vector3D Y1 = (System.Math.Cos(a1Rad) * N1 + System.Math.Sin(a1Rad) * E1).Normalize();
            Vector3D X1 = Vector3D.Cross(Y1, v1).Normalize();

            Vector3D N2 = (new Vector3D(0, 0, 1) - v2.Z * v2).Normalize();
            Vector3D E2 = Vector3D.Cross(new Vector3D(0, 0, 1), v2).Normalize();
            double a2Rad = a2 * System.Math.PI / 180.0;
            Vector3D Y2 = (System.Math.Cos(a2Rad) * N2 + System.Math.Sin(a2Rad) * E2).Normalize();
            Vector3D X2 = Vector3D.Cross(Y2, v2).Normalize();

            double r32 = X2.Z * X1.Y + Y2.Z * Y1.Y + v2.Z * v1.Y;
            double r23 = X2.Y * X1.Z + Y2.Y * Y1.Z + v2.Y * v1.Z;
            
            double r13 = X2.X * X1.Z + Y2.X * Y1.Z + v2.X * v1.Z;
            double r31 = X2.Z * X1.X + Y2.Z * Y1.X + v2.Z * v1.X;
            
            double r21 = X2.Y * X1.X + Y2.Y * Y1.X + v2.Y * v1.X;
            double r12 = X2.X * X1.Y + Y2.X * Y1.Y + v2.X * v1.Y;

            Vector3D pRaw = new Vector3D(r32 - r23, r13 - r31, r21 - r12);
            if (pRaw.Length < 1e-6) {
                throw new InvalidOperationException(
                    "Cannot determine polar axis: the two measurement points are too close together. " +
                    "Ensure the mount actually rotated between measurements.");
            }
            Vector3D P = pRaw.Normalize();

            if (siteLatitude >= 0 && P.Z < 0) {
                P = new Vector3D(-P.X, -P.Y, -P.Z);
            } else if (siteLatitude < 0 && P.Z > 0) {
                P = new Vector3D(-P.X, -P.Y, -P.Z);
            }

            return new AlignmentCalibrationState(P, v2, lstMeasurement2);
        }

        public AlignmentError EvaluateLiveError(Coordinates liveCoordinates, double lstLive, AlignmentCalibrationState calibration, double siteLatitude) {
            double deltaLst = lstLive - calibration.LstMeasurement2;
            if (deltaLst > 12.0) deltaLst -= 24.0;
            if (deltaLst < -12.0) deltaLst += 24.0;

            double correctedRa = liveCoordinates.RA - deltaLst;
            if (correctedRa < 0) correctedRa += 24.0;
            if (correctedRa >= 24.0) correctedRa -= 24.0;

            Coordinates correctedC2 = new Coordinates(correctedRa, liveCoordinates.Dec, liveCoordinates.Epoch, Coordinates.RAType.Hours);
            Vector3D v2Live = Vector3D.FromEquatorial(correctedC2);

            Vector3D calculatedPolarAxis = (v2Live - calibration.Measurement2Vector + calibration.InitialPolarAxis).Normalize();

            return CalculateErrorFromAxis(calculatedPolarAxis, liveCoordinates.RA, lstLive, siteLatitude);
        }

        public AlignmentError CalculateErrorFromAxis(Vector3D axis, double referenceRA, double lstLive, double siteLatitude) {
            double decP = System.Math.Asin(System.Math.Clamp(axis.Z, -1.0, 1.0)) * 180.0 / System.Math.PI;
            double raP = System.Math.Atan2(axis.Y, axis.X) * 12.0 / System.Math.PI;
            if (raP < 0) raP += 24.0;

            double latRad = siteLatitude * System.Math.PI / 180.0;
            double lst = lstLive == 0.0 ? referenceRA : lstLive;

            double haP = lst - raP;
            if (haP < 0) haP += 24.0;
            if (haP >= 24.0) haP -= 24.0;

            double decRad = decP * System.Math.PI / 180.0;
            double haRad = haP * 15.0 * System.Math.PI / 180.0;

            double sinAlt = System.Math.Sin(decRad) * System.Math.Sin(latRad) + System.Math.Cos(decRad) * System.Math.Cos(latRad) * System.Math.Cos(haRad);
            sinAlt = System.Math.Clamp(sinAlt, -1.0, 1.0);
            double altP = System.Math.Asin(sinAlt) * 180.0 / System.Math.PI;

            double cosAlt = System.Math.Cos(altP * System.Math.PI / 180.0);
            double azP = 0.0;
            if (System.Math.Abs(cosAlt) > 1e-6) {
                double cosAz = (System.Math.Sin(decRad) * System.Math.Cos(latRad) - System.Math.Cos(decRad) * System.Math.Sin(latRad) * System.Math.Cos(haRad)) / cosAlt;
                double sinAz = (-System.Math.Cos(decRad) * System.Math.Sin(haRad)) / cosAlt;
                cosAz = System.Math.Clamp(cosAz, -1.0, 1.0);
                azP = System.Math.Atan2(sinAz, cosAz) * 180.0 / System.Math.PI;
                if (azP < 0) azP += 360.0;
            }

            double trueAlt = System.Math.Abs(siteLatitude);
            double trueAz = (siteLatitude >= 0) ? 0.0 : 180.0;

            double altErrorDeg = altP - trueAlt;
            double azDiff = azP - trueAz;
            while (azDiff > 180.0) azDiff -= 360.0;
            while (azDiff < -180.0) azDiff += 360.0;
            double azErrorDeg = azDiff * System.Math.Cos(latRad);

            double altErrorArcmin = altErrorDeg * 60.0;
            double azErrorArcmin = azErrorDeg * 60.0;
            double totalErrorValue = System.Math.Sqrt(altErrorArcmin * altErrorArcmin + azErrorArcmin * azErrorArcmin);

            return new AlignmentError(altErrorArcmin, azErrorArcmin, totalErrorValue, axis);
        }
    }
}
