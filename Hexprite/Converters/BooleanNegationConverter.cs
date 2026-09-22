using System;
using System.Globalization;
using System.Windows.Data;

namespace Hexprite.Converters
{
    /// <summary>
    /// Converts a boolean value to its negation.
    /// True becomes False, False becomes True.
    /// </summary>
    public class BooleanNegationConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return true; // Default to enabled if value is not a boolean
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
        }
    }
}
