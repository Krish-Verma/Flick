using System.Windows;
using LocalClip.App.Services;

namespace LocalClip.App.Views;

/// <summary>Slice 1 review gallery. Not shown in normal Flick startup.</summary>
public partial class DesignSystemPreviewWindow : Window
{
    public DesignSystemPreviewWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => _ = FlickBackdropService.TryApplyMica(this);
        Closed += (_, _) => FlickBackdropService.Reset(this);
    }
}
