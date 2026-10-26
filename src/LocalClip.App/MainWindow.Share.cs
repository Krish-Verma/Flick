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
        // Share owns a finite complete card outside the Trim/Audio scroll host.
        // Keep playback controls clear when the preview can fit the card; use
        // the full editor allocation only when a compact viewport needs it.
        bool useEditorArea = share && PreviewSurface.ActualHeight < 360;
        // The compact card replaces this allocation. Keep its underlying
        // player/tools measured, but don't leave half-visible controls around
        // the card or keyboard stops behind it. Close restores the workspace.
        PreviewSurface.Visibility = PlaybackControlsRow.Visibility = EditorToolBar.Visibility =
            useEditorArea ? Visibility.Hidden : Visibility.Visible;
        Grid.SetRow(EditorContextLayer, useEditorArea ? 0 : 1);
        Grid.SetRowSpan(EditorContextLayer, useEditorArea ? 5 : 1);
        double available = useEditorArea ? ClipEditorPanel.ActualHeight : PreviewSurface.ActualHeight;
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
            "Export details", MessageBoxButton.OK, MessageBoxImage.Information);
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
