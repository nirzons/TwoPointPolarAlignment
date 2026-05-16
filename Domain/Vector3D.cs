using System;
using System.Runtime.CompilerServices;
using NINA.Astrometry;

namespace NirZonshine.NINA.TwoPointPolarAlignment.Domain {
    public struct Vector3D {
        public double X;
        public double Y;
        public double Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3D(double x, double y, double z) {
            X = x;
            Y = y;
            Z = z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3D FromEquatorial(Coordinates c) {
            double raRad = c.RA * 15.0 * Math.PI / 180.0; // RA in hours to radians
            double decRad = c.Dec * Math.PI / 180.0;      // Dec in degrees to radians
            return new Vector3D(
                Math.Cos(decRad) * Math.Cos(raRad),
                Math.Cos(decRad) * Math.Sin(raRad),
                Math.Sin(decRad)
            );
        }

        public double Length {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Math.Sqrt(X * X + Y * Y + Z * Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3D Normalize() {
            double len = Length;
            if (len < 1e-9) return new Vector3D(0, 0, 1);
            return new Vector3D(X / len, Y / len, Z / len);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Dot(Vector3D v1, Vector3D v2) {
            return v1.X * v2.X + v1.Y * v2.Y + v1.Z * v2.Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3D Cross(Vector3D v1, Vector3D v2) {
            return new Vector3D(
                v1.Y * v2.Z - v1.Z * v2.Y,
                v1.Z * v2.X - v1.X * v2.Z,
                v1.X * v2.Y - v1.Y * v2.X
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3D operator +(Vector3D v1, Vector3D v2) {
            return new Vector3D(v1.X + v2.X, v1.Y + v2.Y, v1.Z + v2.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3D operator -(Vector3D v1, Vector3D v2) {
            return new Vector3D(v1.X - v2.X, v1.Y - v2.Y, v1.Z - v2.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3D operator *(double scalar, Vector3D v) {
            return new Vector3D(scalar * v.X, scalar * v.Y, scalar * v.Z);
        }
    }
}
