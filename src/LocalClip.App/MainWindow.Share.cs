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
        foreach (string key in SharePaletteKeys) EditorContextLayer.Resources.Remove("Flick.Brush." + key);
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
        }
    }

    private void UpdateEditorContextHeight()
    {
        bool share = SharePanel.Visibility == Visibility.Visible;
        // At minimum height the video row cannot contain a usable drawer.
        // Let Share use the editor's full area while keeping its fixed actions.
        bool useEditorArea = share && PreviewSurface.ActualHeight < 320;
        Grid.SetRow(EditorContextLayer, useEditorArea ? 0 : 1);
        Grid.SetRowSpan(EditorContextLayer, useEditorArea ? 5 : 1);
        double available = useEditorArea ? ClipEditorPanel.ActualHeight : PreviewSurface.ActualHeight;
        double chrome = share ? 90 + ShareActionFooter.ActualHeight : 100;
        EditorContextScroll.MaxHeight = Math.Max(32, available - chrome);
        EditorContextLayer.MaxHeight = Math.Max(100, available - 24);
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
