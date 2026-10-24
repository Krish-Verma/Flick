using System.Globalization;
using System.Windows.Data;

namespace LocalClip.App.Services;

public sealed class ShareAspectLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value as string) switch
    {
        "Vertical9x16" => "Portrait 9:16",
        "Square1x1" => "Square 1:1",
        "Original" => "Original shape",
        _ => "Choose shape"
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
