using System.Windows;
using System.Windows.Controls;

namespace LocalClip.App.Views.Components;

/// <summary>
/// Reusable notification chrome only. Showing/hiding overlays, focus safety and
/// confirmation sounds are intentionally part of Slice 2, not Slice 1.
/// </summary>
public partial class FlickClipToast : UserControl
{
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(
        nameof(Heading), typeof(string), typeof(FlickClipToast),
        new PropertyMetadata("Clip Saved"));

    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
        nameof(Detail), typeof(string), typeof(FlickClipToast),
        new PropertyMetadata("Last Round Clutch · 0:29"));

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

    public FlickClipToast()
    {
        InitializeComponent();
    }
}
