using System;
using System.Globalization;
using System.Windows.Data;

namespace NirZonshine.NINA.TwoPointPolarAlignment.Converters {
    public class NullableNumberToStringConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value == null) return "(Current)";
            if (value is double d) return d > 0 ? d.ToString(culture) : "(Current)";
            if (value is int i) return i >= 0 ? i.ToString(culture) : "(Current)";
            return value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            string str = value?.ToString()?.Trim();
            if (string.IsNullOrEmpty(str) || string.Equals(str, "(Current)", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }

            if (targetType == typeof(double?) || targetType == typeof(double)) {
                if (double.TryParse(str, NumberStyles.Any, culture, out double d)) {
                    return d;
                }
                return null;
            }

            if (targetType == typeof(int?) || targetType == typeof(int)) {
                if (int.TryParse(str, NumberStyles.Any, culture, out int i)) {
                    return i;
                }
                return null;
            }

            return null;
        }
    }
}
