using System;

namespace ScratchApp {
    class Program {
        static void Main(string[] args) {
            // Solved coordinates from logs
            double dec1 = 84.6411; // 84° 38' 28"
            double ra1 = 5.0664;   // 05:03:59
            double dec2 = 85.7114; // 85° 42' 41"
            double ra2 = 5.7319;   // 05:43:55

            double a1 = 251.4941;
            double a2 = 172.0775;

            double latitude = 32.5667; // 32° 34' 00"
            double lst = 10.9444;      // 10:56:40

            // Coordinates vectors
            Vector3D v1 = FromEquatorial(ra1, dec1);
            Vector3D v2 = FromEquatorial(ra2, dec2);

            Console.WriteLine("--- Math Solver ---");
            Console.WriteLine($"v1: {v1.X:F6}, {v1.Y:F6}, {v1.Z:F6}");
            Console.WriteLine($"v2: {v2.X:F6}, {v2.Y:F6}, {v2.Z:F6}");

            double theta12 = Math.Acos(Math.Clamp(Dot(v1, v2), -1.0, 1.0));
            Console.WriteLine($"Theta12: {theta12 * 180.0 / Math.PI:F4}°");

            // Candidates for omega:
            // 1. Solved PA difference: a2 - a1
            // 2. Physical rotation: -90.0
            // 3. Physical rotation: 90.0
            double[] omegaCandidates = { a2 - a1, a1 - a2, -90.0, 90.0 };
            string[] omegaNames = { "PA Diff (a2 - a1)", "PA Diff (a1 - a2)", "Physical -90", "Physical 90" };

            for (int i = 0; i < omegaCandidates.Length; i++) {
                double omega = omegaCandidates[i];
                double omegaRad = omega * Math.PI / 180.0;

                double sinHalfOmega = Math.Sin(omegaRad / 2.0);
                double sinRho = Math.Sin(theta12 / 2.0) / sinHalfOmega;
                sinRho = Math.Clamp(sinRho, -1.0, 1.0);
                double cosRho = Math.Sqrt(1.0 - sinRho * sinRho);

                Vector3D u_b = (v1 + v2).Normalize();
                Vector3D u_p = Cross(v1, v2).Normalize();

                double cosHalfTheta = Math.Cos(theta12 / 2.0);
                double c1 = cosRho / cosHalfTheta;
                double c2 = (Math.Sin(omegaRad) * sinRho * sinRho) / Math.Sin(theta12);

                // Try both signs of c2
                double[] c2Candidates = { c2, -c2 };
                foreach (var c2Val in c2Candidates) {
                    Vector3D pAxis = (c1 * u_b + c2Val * u_p).Normalize();

                    double decP = Math.Asin(pAxis.Z) * 180.0 / Math.PI;
                    double raP = Math.Atan2(pAxis.Y, pAxis.X) * 12.0 / Math.PI;
                    if (raP < 0) raP += 24.0;

                    double haP = lst - raP;
                    if (haP < 0) haP += 24.0;
                    if (haP >= 24.0) haP -= 24.0;

                    double latRad = latitude * Math.PI / 180.0;
                    double decRad = decP * Math.PI / 180.0;
                    double haRad = haP * 15.0 * Math.PI / 180.0;

                    double sinAlt = Math.Sin(decRad) * Math.Sin(latRad) + Math.Cos(decRad) * Math.Cos(latRad) * Math.Cos(haRad);
                    double altP = Math.Asin(Math.Clamp(sinAlt, -1.0, 1.0)) * 180.0 / Math.PI;

                    double cosAlt = Math.Cos(altP * Math.PI / 180.0);
                    double azP = 0.0;
                    if (Math.Abs(cosAlt) > 1e-6) {
                        double cosAz = (Math.Sin(decRad) * Math.Cos(latRad) - Math.Cos(decRad) * Math.Sin(latRad) * Math.Cos(haRad)) / cosAlt;
                        double sinAz = (-Math.Cos(decRad) * Math.Sin(haRad)) / cosAlt;
                        azP = Math.Atan2(sinAz, cosAz) * 180.0 / Math.PI;
                        if (azP < 0) azP += 360.0;
                    }

                    double altError = altP - latitude;
                    double azDiff = azP; // relative to North (0)
                    if (azDiff > 180.0) azDiff -= 360.0;
                    if (azDiff < -180.0) azDiff += 360.0;
                    double azErrorUncompressed = azDiff;
                    double azErrorCompressed = azDiff * Math.Cos(latRad);

                    double totalUncompressed = Math.Sqrt(altError * altError + azErrorUncompressed * azErrorUncompressed);
                    double totalCompressed = Math.Sqrt(altError * altError + azErrorCompressed * azErrorCompressed);

                    // We print all trials so we can inspect manually!
                    Console.WriteLine($"Omega: {omegaNames[i]} ({omega:F1}°), c2 sign: {(c2Val == c2 ? "+" : "-")}");
                    Console.WriteLine($"  Alt Error: {altError * 60.0:F1}' ({-altError:F4}°) | Az Uncomp: {azErrorUncompressed * 60.0:F1}' | Az Comp: {azErrorCompressed * 60.0:F1}' | Tot Comp: {totalCompressed * 60.0:F1}'");
                }
            }
        }

        static Vector3D FromEquatorial(double ra, double dec) {
            double raRad = ra * 15.0 * Math.PI / 180.0;
            double decRad = dec * Math.PI / 180.0;
            return new Vector3D(
                Math.Cos(decRad) * Math.Cos(raRad),
                Math.Cos(decRad) * Math.Sin(raRad),
                Math.Sin(decRad)
            );
        }

        static double Dot(Vector3D v1, Vector3D v2) {
            return v1.X * v2.X + v1.Y * v2.Y + v1.Z * v2.Z;
        }

        static Vector3D Cross(Vector3D v1, Vector3D v2) {
            return new Vector3D(
                v1.Y * v2.Z - v1.Z * v2.Y,
                v1.Z * v2.X - v1.X * v2.Z,
                v1.X * v2.Y - v1.Y * v2.X
            );
        }

        class Vector3D {
            public double X, Y, Z;
            public Vector3D(double x, double y, double z) { X = x; Y = y; Z = z; }
            public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
            public Vector3D Normalize() {
                double len = Length;
                return new Vector3D(X / len, Y / len, Z / len);
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
    }
}
