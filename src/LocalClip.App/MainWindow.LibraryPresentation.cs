using System.Windows;

namespace LocalClip.App;

public partial class MainWindow
{
    private void ClearClipSearch_Click(object sender, RoutedEventArgs args)
    {
        // Clear the delayed target as well as the model so a pending search
        // cannot restore the old query after the user chooses Clear.
        TxtClipSearch.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, "");
        TxtClipSearch.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        _vm.ClearLibrarySearch();
        TxtClipSearch.Focus();
    }

    private void ClearLibraryFilters_Click(object sender, RoutedEventArgs args)
    {
        _vm.ClearLibraryFilters();
        RadioAllClips.IsChecked = true;
    }

    private void ChooseCaptureSource_Click(object sender, RoutedEventArgs args)
    {
        CmbTargets.Focus();
        CmbTargets.IsDropDownOpen = true;
    }
}
