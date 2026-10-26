using System.Windows;
using System.Windows.Controls;

namespace LocalClip.App;

public partial class MainWindow
{
    private Button? _libraryPanelOrigin;

    private void LibraryPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind } button) return;
        var panel = kind == "Filters" ? LibraryFiltersPanel : LibraryDetailsPanel;
        bool open = panel.Visibility != Visibility.Visible;
        CloseLibraryPanels();
        if (!open) return;
        _libraryPanelOrigin = button;
        panel.Visibility = Visibility.Visible;
        System.Windows.Automation.AutomationProperties.SetHelpText(button, "Open");
        (kind == "Filters" ? BtnCloseLibraryFiltersPanel : BtnCloseLibraryDetailsPanel).Focus();
    }

    private void CloseLibraryPanel_Click(object sender, RoutedEventArgs e) => CloseLibraryPanels();

    private void CloseLibraryPanels()
    {
        LibraryFiltersPanel.Visibility = LibraryDetailsPanel.Visibility = Visibility.Collapsed;
        if (_libraryPanelOrigin != null)
        {
            System.Windows.Automation.AutomationProperties.SetHelpText(_libraryPanelOrigin, "Closed");
            _libraryPanelOrigin.Focus();
        }
        _libraryPanelOrigin = null;
    }
}
