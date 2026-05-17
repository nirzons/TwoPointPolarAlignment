namespace NirZonshine.NINA.TwoPointPolarAlignment.Domain {
    public class AlignmentProgressReport {
        public string LogMessage { get; set; }
        public string StatusText { get; set; }
        public string StatusColorHex { get; set; } // e.g. #72BDFF
        public bool? IsReversedFlowActive { get; set; }
        
        public string AzimuthError { get; set; }
        public string AltitudeError { get; set; }
        public double TotalErrorValue { get; set; }
        public string TotalError { get; set; }
        public string TotalErrorRating { get; set; }
        public string TotalErrorRatingColorHex { get; set; }
        
        public string AzimuthInstruction { get; set; }
        public string AltitudeInstruction { get; set; }
        
        public bool IsAltitudePriority { get; set; }
        public bool IsAzimuthPriority { get; set; }
        
        public bool HasSuccessfulAlignmentReached { get; set; }
    }
}
