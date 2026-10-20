# Slice 5 source-contract check: not a claim of real Windows drag/drop.
$ErrorActionPreference = "Stop"
$root = (Get-Item $PSScriptRoot).Parent.FullName
function Repo([string]$path) {
    $full = Join-Path $root $path
    if (!(Test-Path -LiteralPath $full -PathType Leaf)) { throw "Missing: $path" }
    return Get-Content -LiteralPath $full -Raw
}
function Require([string]$text, [string]$pattern, [string]$message) {
    if ($text -notmatch $pattern) { throw "Slice 5 contract violation: $message" }
}
$main = Repo "src/LocalClip.App/MainWindow.xaml"
$code = Repo "src/LocalClip.App/MainWindow.xaml.cs"
$vm = Repo "src/LocalClip.App/ViewModels/MainViewModel.cs"
$settings = Repo "src/LocalClip.App/Views/SettingsWindow.xaml"
$settingsCs = Repo "src/LocalClip.App/Views/SettingsWindow.xaml.cs"
$targets = Repo "src/LocalClip.Core/Rules/ShareExportTargets.cs"
$ready = Repo "src/LocalClip.Core/Rules/ReadyShareFile.cs"
$naming = Repo "src/LocalClip.Core/Rules/ShareExportNaming.cs"
$exporter = Repo "src/LocalClip.Core/Media/ClipExporter.cs"
$tests = Repo "tests/LocalClip.Core.Tests/ShareExportTargetsTests.cs"

Require $targets 'DefaultMb = 20' "Default must be 20 decimal MB"
Require $targets '10, 20, 50, 100' "All standard export presets must exist"
Require $targets 'megabytes \* 1_000_000L' "File limits are decimal bytes, not MiB"
Require $ready 'ExportFingerprint.Compute' "Ready file must match selected trim/audio/fingerprint"
Require $ready 'exported.Length == 0' "Empty file must not be shareable"
Require $ready 'selected.ExportedFileSizeBytes' "Ready metadata size must match actual bytes"
Require $naming 'SHA256.HashData' "Separate imported clips require collision-resistant output names"
Require $exporter 'ShareExportNaming.ForOriginal' "Media exporter must use collision-resistant output names"
Require $main 'CmbExportCap' "Compact export cap chooser missing"
Require $main 'CustomCapPanel' "Custom limit must be explicit"
Require $main 'BtnCancelExport_Click' "Working cancel action missing"
Require $main 'IsIndeterminate="True"' "Milestone stages must not be misrepresented as estimated time"
Require $main 'CanStartExport' "Export must be disabled when no valid source exists"
Require $main 'DragHandle_PreviewMouseMove' "Real OLE drag must wait for pointer motion"
Require $main 'DataFormats.FileDrop|DragHandle' "Actual file drag tile missing"
Require $main 'AdvancedShareExpander' "Direct posting must be secondary"
Require $main 'IsExpanded="False"' "Direct posting must start collapsed"
if ($main -match 'x:Name="Radio20"|x:Name="Radio50"|x:Name="Radio100"') {
    throw "Previous oversized radio-button export toolbar remains"
}
Require $code 'TryGetReadyShareFile' "Drag must guard file validity"
Require $code 'DragDrop.DoDragDrop' "Real Windows OLE drag missing"
Require $code 'SystemParameters.MinimumHorizontalDragDistance' "Drag requires movement threshold"
Require $vm 'CancelExport\(' "Export cancel missing"
Require $vm '_exporter.ExportForDiscordAsync\(' "Actual native media export must remain"
Require $vm 'cancellation.Token' "Cancellation must reach worker"
Require $vm 'SelectedClip\?\.Id == clip.Id' "Export result must be tied to its originating clip"
Require $vm 'EnableDiscordWebhook' "Webhook requires opt-in"
Require $vm 'TryGetReadyShareFile' "Clipboard/webhook file requires readiness"
foreach ($category in @("Recording", "Audio", "Shortcuts", "Storage", "General", "Sharing")) {
    Require $settings ('Panel' + $category + '"') "Missing Settings section: $category"
    Require $settings ('Nav' + $category + '"') "Missing Settings navigation: $category"
}
Require $settings 'ChkEnableWebhook' "Optional webhook toggle missing"
Require $settings 'ChkClipSavedOverlay' "Independent overlay preference missing"
Require $settings 'ChkClipSavedSound' "Independent sound preference missing"
Require $settings 'TxtOriginalsFolder' "Saved Clips Folder missing"
Require $settings 'TxtExportsFolder' "Separate Export Folder missing"
Require $settings 'ManageFolders_Click' "Linked clip folder management missing"
Require $settingsCs 'staged.Save\(\)' "Settings must save staged configuration"
Require $settingsCs '_settings.DiscordTargetSizeMb = staged.DiscordTargetSizeMb;' "Share default must persist"
Require $settingsCs '_settings.ExportsFolder = staged.ExportsFolder;' "Export path must persist"
Require $settingsCs 'WindowsStartupRegistration.Apply' "Windows startup preference must be explicit"
Require $settingsCs 'Shortcut_PreviewKeyDown' "Shortcut capture must work without typing JSON"
Require $tests 'StaleOrMissingFilesNeverBecomeDraggable' "Stale-share regression test missing"
Write-Host "Flick GUI Slice 5 static contract PASS; Windows interaction and Discord composer still require hardware QA." -ForegroundColor Green
