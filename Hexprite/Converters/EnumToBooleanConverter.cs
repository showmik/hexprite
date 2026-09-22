using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Hexprite.Converters
{
    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            string checkValue = value.ToString() ?? string.Empty;
            string targetValue = parameter.ToString() ?? string.Empty;

            return checkValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return Binding.DoNothing;

            bool isChecked = (bool)value;
            if (isChecked)
            {
                return Enum.Parse(targetType, parameter.ToString() ?? string.Empty);
            }

            return Binding.DoNothing;
        }
    }

    public class FontExportFormatDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Core.FontExportFormat format)
            {
                return format switch
                {
                    Core.FontExportFormat.AdafruitGfx => "Adafruit GFX (Arduino / C++ Header)",
                    Core.FontExportFormat.U8g2Bdf => "U8g2 (BDF Bitmap Font)",
                    Core.FontExportFormat.Lvgl => "LVGL (lv_font_t v8/v9)",
                    Core.FontExportFormat.RawCArray => "Raw C Array (Byte Buffer)",
                    Core.FontExportFormat.FlipperZero => "Flipper Zero (u8g2/fbf)",
                    _ => format.ToString()
                };
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    public class DisplayTypeDescriptionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Core.DisplayType type)
            {
                return type switch
                {
                    Core.DisplayType.GenericWhite => "Default Dark",
                    Core.DisplayType.SSD1306Blue => "OLED Blue",
                    Core.DisplayType.SSD1306Green => "OLED Green",
                    Core.DisplayType.FlipperZero => "Flipper Zero",
                    Core.DisplayType.ePaper => "E-Paper",
                    _ => type.ToString()
                };
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
