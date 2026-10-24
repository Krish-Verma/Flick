using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using SystemColors = System.Windows.SystemColors;

namespace LocalClip.App;

public partial class MainWindow
{
    private static readonly string[] SharePaletteKeys = ["ShareSurface", "ShareTile", "SharePreview", "Canvas",
        "Surface", "Raised", "Hover", "TextPrimary", "TextSecondary", "TextMuted", "Accent", "Border", "Divider",
        "BorderStrong", "Focus", "OnAccent", "PrimaryGradient", "PrimaryGradientHover"];

    private void ShareSystemParametersChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SystemParameters.HighContrast) || Dispatcher.HasShutdownStarted) return;
        Dispatcher.InvokeAsync(() => ApplyShareAccessiblePalette(SystemParameters.HighContrast));
    }

    // Tests can exercise this exact palette path without changing Windows settings.
    private void ApplyShareAccessiblePalette(bool highContrast)
    {
        _vm?.NotifyShareAccessibility();
        foreach (string key in SharePaletteKeys)
        {
            EditorContextLayer.Resources.Remove("Flick.Brush." + key);
            ShareMoreMenu.Resources.Remove("Flick.Brush." + key);
        }
        if (!highContrast) return;
        foreach (string key in SharePaletteKeys)
        {
            Brush brush = key switch
            {
                "TextPrimary" or "TextSecondary" or "TextMuted" or "Accent" or "Border" or "Divider" or "BorderStrong" => SystemColors.WindowTextBrush,
                "Focus" or "PrimaryGradient" or "PrimaryGradientHover" => SystemColors.HighlightBrush,
                "OnAccent" => SystemColors.HighlightTextBrush,
                _ => SystemColors.WindowBrush
            };
            EditorContextLayer.Resources["Flick.Brush." + key] = brush;
            ShareMoreMenu.Resources["Flick.Brush." + key] = brush;
        }
    }

    private void UpdateEditorContextHeight()
    {
        bool share = SharePanel.Visibility == Visibility.Visible;
        // Share owns a finite, complete card rather than participating in the
        // scrolling Trim/Audio host. The full editor allocation also fits the
        // custom size, recent exports and Cancel controls at minimum height.
        Grid.SetRow(EditorContextLayer, share ? 0 : 1);
        Grid.SetRowSpan(EditorContextLayer, share ? 5 : 1);
        double available = share ? ClipEditorPanel.ActualHeight : PreviewSurface.ActualHeight;
        EditorContextLayer.Width = share ? Math.Min(600, Math.Max(280, ClipEditorPanel.ActualWidth - 24)) : double.NaN;
        EditorContextLayer.MaxHeight = Math.Max(100, available - 24);
        if (!share) EditorContextScroll.MaxHeight = Math.Max(32, available - 100);
    }

    private void ShareMoreButton_Click(object sender, RoutedEventArgs args)
    {
        ShareMoreMenu.PlacementTarget = ShareMoreButton;
        ShareMoreMenu.IsOpen = true;
    }

    private void ShareDetails_Click(object sender, RoutedEventArgs args)
    {
        string message = _vm.ExportStatusMessage;
        if (string.IsNullOrWhiteSpace(message)) message = _vm.ShareTileHint;
        Services.FlickDialog.Show(message + "\n\n" + _vm.ExportAspectDescription + "\n" + _vm.ShareSizeHint,
            "Export Details", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShareFooter_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateEditorContextHeight();

    private void ShareTile_PreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _vm.CopyExportedFileToClipboard();
            args.Handled = true;
        }
        else if (args.Key == Key.Escape) { CloseEditorContext(); args.Handled = true; }
    }
}
