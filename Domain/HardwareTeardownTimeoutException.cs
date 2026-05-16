using System;

namespace NirZonshine.NINA.TwoPointPolarAlignment.Domain {
    public class HardwareTeardownTimeoutException : Exception {
        public HardwareTeardownTimeoutException() : base() { }
        public HardwareTeardownTimeoutException(string message) : base(message) { }
        public HardwareTeardownTimeoutException(string message, Exception innerException) : base(message, innerException) { }
    }
}
