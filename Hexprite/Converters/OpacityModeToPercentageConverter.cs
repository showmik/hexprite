using System;
using System.Globalization;
using System.Windows.Data;
using Hexprite.Core;

namespace Hexprite.Converters
{
    public class OpacityModeToPercentageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is LayerOpacityMode mode)
            {
                return mode switch
                {
                    LayerOpacityMode.Solid => "100%",
                    LayerOpacityMode.Dense => "75%",
                    LayerOpacityMode.Checkerboard => "50%",
                    LayerOpacityMode.Sparse => "25%",
                    _ => value.ToString(),
                };
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
