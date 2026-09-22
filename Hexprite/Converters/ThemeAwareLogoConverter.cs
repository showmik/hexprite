using System;
using System.Globalization;
using System.Windows.Data;

namespace Hexprite.Converters
{
    /// <summary>
    /// Converts theme name to appropriate logo source path.
    /// Returns light logo for dark themes, regular logo for light themes.
    /// </summary>
    public class ThemeAwareLogoConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string themeName)
            {
                // Dark, Dim, and Flipper themes need the light/inverted logo
                if (themeName == "Dark" || themeName == "Dim" || themeName == "Flipper")
                {
                    return "pack://application:,,,/Assets/LogoLight.png";
                }
            }

            // Light theme use the regular logo
            return "pack://application:,,,/Assets/Logo.png";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException("ThemeAwareLogoConverter does not support ConvertBack");
        }
    }
}
