using System;
using System.Globalization;
using System.Windows.Data;

namespace Hexprite.Converters
{
    /// <summary>
    /// Two-way converter between an enum property and a ComboBox SelectedIndex.
    /// The ComboBox items must be declared in the same order as the enum's underlying values.
    /// WPF does not convert an enum to the int SelectedIndex on its own: without this converter
    /// the combo shows blank for every non-default value.
    /// </summary>
    public class EnumToIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Enum e)
                return System.Convert.ToInt32(e, CultureInfo.InvariantCulture);
            return -1;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not int index || index < 0)
                return Binding.DoNothing;

            var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (!enumType.IsEnum)
                return Binding.DoNothing;

            var result = Enum.ToObject(enumType, index);
            return Enum.IsDefined(enumType, result) ? result : Binding.DoNothing;
        }
    }
}
