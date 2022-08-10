using System;
using System.Collections.Generic;
using System.Windows.Data;

namespace InSalute.ValueConverters
{
    [ValueConversion(typeof(List<string>), typeof(string))]
    public class ListStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return targetType != typeof(string)
                ? throw new InvalidOperationException("The target must be a String")
                : string.Join("; ", ((List<string>)value).ToArray());
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
