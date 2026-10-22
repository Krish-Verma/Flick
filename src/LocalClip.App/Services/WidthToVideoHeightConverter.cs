using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LocalClip.App.Services;

/// <summary>
/// Retains a generous 16:9 preview surface using only WPF layout values.
/// No extra graphics shader, frame decoder, or rendering process.
/// </summary>
public sealed class WidthToVideoHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double width = value is double measured && double.IsFinite(measured) ? measured : 0;
        return Math.Clamp(width * 9d / 16d, 220d, 460d);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
