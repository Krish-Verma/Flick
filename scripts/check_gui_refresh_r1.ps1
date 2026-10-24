# R1 source-only guard: Windows WPF compilation + physical QA are separate.
$ErrorActionPreference = "Stop"
$root = (Get-Item $PSScriptRoot).Parent.FullName
function Read([string]$path) {
    Get-Content -LiteralPath (Join-Path $root $path) -Raw
}
function Require([string]$source, [string]$pattern, [string]$reason) {
    if ($source -notmatch $pattern) { throw "Flick R1 source contract FAILED: $reason" }
}
$main = Read "src/LocalClip.App/MainWindow.xaml"
$mainCode = Read "src/LocalClip.App/MainWindow.xaml.cs"
$viewModel = Read "src/LocalClip.App/ViewModels/MainViewModel.cs"
$settings = Read "src/LocalClip.App/Views/SettingsWindow.xaml"
$settingsCode = Read "src/LocalClip.App/Views/SettingsWindow.xaml.cs"
$folders = Read "src/LocalClip.App/Views/ConnectedFoldersWindow.xaml"
$foldersCode = Read "src/LocalClip.App/Views/ConnectedFoldersWindow.xaml.cs"
Require $main 'x:Name="WorkspacePageLayer"' "Main window needs a native in-window page layer"
Require $main 'x:Name="WorkspacePageHost"' "Main window must lazy-host UserControl pages"
Require $mainCode 'WorkspacePageHost.Content = page' "Pages must be hosted, not modal"
Require $mainCode 'WorkspacePageHost.Content = null' "Offscreen pages need disposal"
Require $mainCode 'CloseSettingsPage' "Save/Cancel/uninstall must return in-place"
Require $mainCode 'CloseConnectedFolders' "Folder close must be in-place"
Require $mainCode 'ReturnToClipWorkspace' "Gallery/editor must remain the same workspace"
Require $mainCode 'CancelPendingPreview\(\)' "Hidden editor must cancel pending decoders"
Require $mainCode 'SuspendPlayback\(clearSource: true\)' "Hidden editor must unload MediaElement"
Require $mainCode 'RegisterHotkeys\(\)' "Explicit Save must continue registering hotkeys"
Require $mainCode 'StopCaptureForMicPrivacyAsync' "Microphone opt-out must release the device"
Require $mainCode 'FlickUninstallLauncher.TryLaunch' "Existing in-app uninstall must survive"
Require $mainCode 'LeavePage\(\)' "Folder scans must be cancelled on navigation"
Require $settings '^\s*<UserControl\s+x:Class="LocalClip.App.Views.SettingsWindow"' "Settings must not create another Window"
Require $folders '^\s*<UserControl\s+x:Class="LocalClip.App.Views.ConnectedFoldersWindow"' "Folders must not create another Window"
Require $settingsCode 'partial class SettingsWindow : UserControl' "Settings C# root must match XAML UserControl"
Require $foldersCode 'partial class ConnectedFoldersWindow : System.Windows.Controls.UserControl' "Folders C# root must match XAML"
Require $settingsCode 'public event Action<bool>\? CloseRequested' "Settings edits must have explicit staged Save/Cancel route"
Require $settingsCode 'staged.Save\(\)' "Settings must preserve atomic disk write"
Require $settingsCode '_isCaptureActive\(\)' "Settings must check current recording status when saving"
Require $mainCode 'new SettingsWindow\(_vm.Settings,\s*\(\) => _vm.IsCaptureActive(?:,|\))' "Settings must query live shared capture/test state when saving"
Require $viewModel 'private bool CaptureActive => CaptureStatus is "Starting" or "Buffering" or "Armed" or "Stopping"' "Normal engine transitions must still count as active capture"
Require $viewModel 'public bool IsCaptureActive => CaptureActive \|\| _gameAutomationBusy \|\| _isRecordingDiagnosticRunning \|\| _diagnosticCleanupUnconfirmed' "Live capture state must include game reservations, private tests and unconfirmed cleanup"
Require $mainCode 'bool captureActive = _vm.IsCaptureActive' "Microphone opt-out must recognize private tests after Settings opens"
Require $mainCode 'CancelRecordingDiagnosticAndWaitAsync\(\)' "Microphone opt-out must await private-test cleanup"
Require $settingsCode '_settings.ExportsFolder = staged.ExportsFolder' "Existing export preference must persist"
Require $foldersCode 'CloseRequested\?\.Invoke\(\)' "Folder close must return to parent page"
Require $foldersCode '_vm.CancelImport\(\)' "Leaving folder page must cancel active import"
Require $folders 'VirtualizingStackPanel.IsVirtualizing="True"' "Connected folders list remains virtualized"
# R1 specifically removes the two management windows. Other existing
# Flick-owned confirmations (e.g., Rename Clip) are redesigned in R7.
if ($mainCode -match '(?i)(settings|folders|dialog)\.ShowDialog\(\)' -or
    $mainCode -match 'new SettingsWindow\([^\r\n]*Owner' -or
    $mainCode -match 'new ConnectedFoldersWindow\([^\r\n]*Owner' -or
    $mainCode -match 'var dialog = new (SettingsWindow|ConnectedFoldersWindow)') {
    throw "Settings/ConnectedFolders modal Window was reintroduced"
}
if ($settingsCode -match 'DialogResult\s*=' -or $foldersCode -match 'Window_Closing') {
    throw "Management controls must not retain Window-only modal lifecycle"
}
if ($settings -match '^\s*<Window\b' -or $folders -match '^\s*<Window\b') {
    throw "Separate Flick-owned management Window roots are prohibited"
}
Write-Host "Flick GUI Refresh R1 single-window source contracts PASS (physical Windows QA still required)." -ForegroundColor Green
