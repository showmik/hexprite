using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Hexprite.Converters
{
    /// <summary>
    /// Converts a boolean value to an inverted Visibility (true -> Collapsed, false -> Visible).
    /// </summary>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class BooleanToInverseVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }

            return Visibility.Visible;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility != Visibility.Visible;
            }

            return false;
        }
    }
}
