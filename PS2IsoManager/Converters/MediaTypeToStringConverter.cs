using System.Globalization;
using System.Windows.Data;
using PS2IsoManager.Models;

namespace PS2IsoManager.Converters;

public class MediaTypeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is MediaType mt ? mt.ToString() : "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
