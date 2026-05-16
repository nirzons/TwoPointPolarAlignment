using System;
using NINA.Astrometry;
using NirZonshine.NINA.TwoPointPolarAlignment.Domain;
using NirZonshine.NINA.TwoPointPolarAlignment.Solvers;

namespace ScratchApp {
    class Program {
        static void Main(string[] args) {
            Console.WriteLine("--- Testing TwoPointPolarSolver Offline ---");
            try {
                var solver = new TwoPointPolarSolver();
                
                // Simulated coordinates for testing
                var c1 = new Coordinates(18.0, -89.0, Epoch.JNOW, Coordinates.RAType.Hours);
                var c2 = new Coordinates(20.0, -89.0, Epoch.JNOW, Coordinates.RAType.Hours);
                double a1 = 45.0;
                double a2 = 45.0;
                double siteLat = -45.0;
                double lstLive = 20.0;
                
                var state = solver.Calibrate(c1, a1, c2, a2, lstLive, siteLat);
                Console.WriteLine($"[Calibrate] Initial Polar Axis Vector: X={state.InitialPolarAxis.X:F5}, Y={state.InitialPolarAxis.Y:F5}, Z={state.InitialPolarAxis.Z:F5}");

                var err = solver.EvaluateLiveError(c2, lstLive, state, siteLat);
                Console.WriteLine($"[Evaluate] Total Error: {err.TotalErrorArcmin:F2} arcmin");
                Console.WriteLine($"[Evaluate] Altitude Error: {err.AltitudeErrorArcmin:F2} arcmin");
                Console.WriteLine($"[Evaluate] Azimuth Error: {err.AzimuthErrorArcmin:F2} arcmin");
                
                Console.WriteLine("--- Test Passed Successfully ---");
            } catch (Exception ex) {
                Console.WriteLine($"ERROR: {ex.Message}");
            }
        }
    }
}
