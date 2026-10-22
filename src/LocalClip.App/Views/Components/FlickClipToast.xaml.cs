using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LocalClip.App.Views.Components;

/// <summary>Compact, full-squircle brand chrome; no second circle or separator.</summary>
public partial class FlickClipToast : UserControl
{
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(
        nameof(Heading), typeof(string), typeof(FlickClipToast),
        new PropertyMetadata("Clip Saved"));

    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
        nameof(Detail), typeof(string), typeof(FlickClipToast),
        new PropertyMetadata("Last Round Clutch · 0:29"));

    public static readonly DependencyProperty IsErrorProperty = DependencyProperty.Register(
        nameof(IsError), typeof(bool), typeof(FlickClipToast),
        new PropertyMetadata(false, OnErrorChanged));

    public string Heading
    {
        get => (string)GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public bool IsError
    {
        get => (bool)GetValue(IsErrorProperty);
        set => SetValue(IsErrorProperty, value);
    }

    public FlickClipToast()
    {
        InitializeComponent();
        UpdateStatusGlyph();
    }

    private static void OnErrorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((FlickClipToast)d).UpdateStatusGlyph();

    private void UpdateStatusGlyph()
    {
        if (ResultGlyph is null) return;
        ResultGlyph.Data = Geometry.Parse(IsError
            ? "M 4,4 L 15,15 M 15,4 L 4,15"
            : "M 2,9 L 7,14 L 16,4");
        ResultGlyph.Stroke = (System.Windows.Media.Brush)FindResource(
            IsError ? "Flick.Brush.Danger" : "Flick.Brush.Success");
    }
}
