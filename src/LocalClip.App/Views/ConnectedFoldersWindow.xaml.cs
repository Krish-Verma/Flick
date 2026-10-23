using System;
using System.ComponentModel;
using System.Windows;
using LocalClip.App.ViewModels;
using LocalClip.Core.Models;

namespace LocalClip.App.Views;

/// <summary>
/// Explicit, in-place import and folder management. Never moves, duplicates,
/// mutates, or silently deletes files belonging to another recording app.
/// </summary>
public partial class ConnectedFoldersWindow : System.Windows.Controls.UserControl
{
    private readonly MainViewModel _vm;

    public event Action? CloseRequested;

    public void LeavePage()
    {
        // Cancel the pending scan, not recording or user's source files.
        if (_vm.IsImporting) _vm.CancelImport();
    }

    public ConnectedFoldersWindow(MainViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        InitializeComponent();
        DataContext = _vm;
        _vm.RefreshConnectedFolders();
        FolderList.SelectedIndex = _vm.ConnectedFolders.Count == 0 ? -1 : 0;
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsImporting)
        {
            MessageBox.Show("Wait for the current scan or cancel it first.",
                "Flick Import", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        using var picker = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose a folder containing existing video clips",
            UseDescriptionForTitle = true
        };
        if (picker.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        if (MessageBox.Show(
            "Link this folder and index its supported MP4/MKV videos? Original files stay in place, and nothing is uploaded or copied.",
            "Add Clip Folder", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;

        try
        {
            var result = await _vm.ImportFolderAsync(picker.SelectedPath,
                ChkIncludeSubfolders.IsChecked == true);
            _vm.RefreshConnectedFolders();
            if (result.FolderUnavailable)
                MessageBox.Show("Folder is currently unavailable. The link is preserved.",
                    "Import Folder", MessageBoxButton.OK, MessageBoxImage.Information);
            else if (result.TotalDiscovered == 0)
                MessageBox.Show("No supported MP4 or MKV files were found.",
                    "Import Folder", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show("Could not index the selected folder: " + ex.Message,
                "Flick Import", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void RescanFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FolderList.SelectedItem is not ConnectedClipFolder folder)
        {
            MessageBox.Show("Select a connected folder to rescan.", "Flick Library",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try { await _vm.RescanConnectedFolderAsync(folder.Id); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show("Rescan failed: " + ex.Message, "Flick Library",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FolderList.SelectedItem is not ConnectedClipFolder folder)
        {
            MessageBox.Show("Select a folder first.", "Flick Library",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_vm.IsImporting)
        {
            MessageBox.Show("Cancel the scan before unlinking a folder.", "Flick Library",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(
                $"Unlink '{folder.DisplayName}' from Flick? Its imported entries will disappear from the Flick library, but the videos and exports will remain untouched.",
                "Unlink Clip Folder", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;
        try
        {
            _vm.RemoveConnectedFolder(folder.Id);
            FolderList.SelectedIndex = _vm.ConnectedFolders.Count == 0 ? -1 : 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Unable to unlink the folder: " + ex.Message,
                "Flick Library", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CancelScan_Click(object sender, RoutedEventArgs e) => _vm.CancelImport();

    private void Folders_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;
        e.Handled = true;
        LeavePage();
        CloseRequested?.Invoke();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        LeavePage();
        CloseRequested?.Invoke();
    }
}
