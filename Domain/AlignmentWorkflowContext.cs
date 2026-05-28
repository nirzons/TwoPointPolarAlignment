using System;
using NINA.Astrometry;

namespace NirZonshine.NINA.TwoPointPolarAlignment.Domain {
    public class AlignmentWorkflowContext {
        public Coordinates Coordinates1 { get; set; }
        public double Angle1 { get; set; }
        public Coordinates Coordinates2 { get; set; }
        public double Angle2 { get; set; }
        public double LstMeasurement2 { get; set; }
        public double LstMeasurement1 { get; set; }
        
        public RotationDirection ActiveDirection { get; set; }
        public bool ActivePreRotate { get; set; }
        public bool SkipHomeSlew { get; set; }
        public bool IsSimulation { get; set; }
        public double CurrentSimulationOffset { get; set; }
        
        public double ManualSimBiasRA { get; set; }
        public double ManualSimBiasDec { get; set; }
        
        public bool HasRoughFinderSimTriggered { get; set; }
        
        public Coordinates LastStoppedCoordinates { get; set; }
        public RotationDirection? LastStoppedDirection { get; set; }
    }
}
