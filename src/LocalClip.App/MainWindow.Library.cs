using System;
using System.Windows;

namespace LocalClip.App;

public partial class MainWindow
{
    private void LibraryLayout_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateLibraryScrollBounds();
    private void LibraryDetails_ExpandedChanged(object sender, RoutedEventArgs e) => UpdateLibraryScrollBounds();

    private void UpdateLibraryScrollBounds()
    {
        if (LibraryLayoutRoot == null || LibraryHeaderScroll == null || LibraryDetailsScroll == null || ClipDetailsExpander == null) return;
        double available = LibraryLayoutRoot.ActualHeight;
        if (!double.IsFinite(available) || available <= 0) return;

        // Both scroll regions are siblings of the virtualized list. A tall
        // form or filter group must not consume the list's complete viewport.
        double listReserve = Math.Min(160, Math.Max(96, available * .30));
        double controls = Math.Max(0, available - listReserve);
        double details = ClipDetailsExpander.IsExpanded
            ? Math.Min(300, controls * .62)
            : Math.Min(112, controls * .45);
        LibraryDetailsScroll.MaxHeight = details;
        LibraryHeaderScroll.MaxHeight = Math.Max(0, controls - details);
    }
}
