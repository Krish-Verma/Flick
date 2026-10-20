# Static source contract audit for Slice 3. Does not certify actual Windows UI.
$ErrorActionPreference = "Stop"
$root = (Get-Item $PSScriptRoot).Parent.FullName
function RepoText([string]$relative) {
    $file = Join-Path $root $relative
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Slice 3 missing required file: $relative"
    }
    return Get-Content -LiteralPath $file -Raw
}
function AssertContains([string]$text, [string]$pattern, [string]$reason) {
    if ($text -notmatch $pattern) { throw "Slice 3 contract violation: $reason" }
}
$storage = RepoText "src/LocalClip.Core/Storage/ClipStorage.cs"
$model = RepoText "src/LocalClip.Core/Models/ClipInfo.cs"
$folder = RepoText "src/LocalClip.Core/Models/ConnectedClipFolder.cs"
$paths = RepoText "src/LocalClip.Core/Storage/ClipLibraryPaths.cs"
$vm = RepoText "src/LocalClip.App/ViewModels/MainViewModel.cs"
$main = RepoText "src/LocalClip.App/MainWindow.xaml"
$dialog = RepoText "src/LocalClip.App/Views/ConnectedFoldersWindow.xaml"
$settings = RepoText "src/LocalClip.App/Views/SettingsWindow.xaml"
$settingsCs = RepoText "src/LocalClip.App/Views/SettingsWindow.xaml.cs"
$tests = RepoText "tests/LocalClip.Core.Tests/ConnectedFolderTests.cs"

AssertContains $model 'public bool IsImported' "Imported ownership must be explicit"
AssertContains $model 'public bool IsAvailable' "Offline library metadata must remain visible"
AssertContains $storage 'connected_sources.json' "Linked folders must persist"
AssertContains $storage 'SaveJsonAtomic' "Metadata saves must be atomic"
AssertContains $storage 'Keep OFFLINE entries across unplug/restart' "LoadIndex must preserve missing paths"
AssertContains $storage 'if \(clip.IsImported\)' "Delete imported video must unlink instead"
AssertContains $storage '_ignoredImportedPaths.Add' "Excluded imports must not return on rescan"
AssertContains $storage 'SemaphoreSlim\(1, 1\)' "Folder scanning must stay bounded"
AssertContains $storage 'MaximumMediaFilesPerScan' "Folder scans need a safety cap"
AssertContains $paths 'FileAttributes.ReparsePoint' "Folder scanning must avoid symlink/junction traversal"
AssertContains $paths 'Directory.EnumerateFileSystemEntries' "Folder discovery must avoid vendor cloud integration"
AssertContains $paths 'CancellationToken' "Folder traversal must be cancelable"
AssertContains $vm 'ClipSourceFilter' "Source filtering must be wired"
AssertContains $vm 'ImportFolderAsync' "Import UI requires real async operation"
AssertContains $vm '_importCancellation\?\.Cancel\(\)' "Capture must be able to preempt imports"
AssertContains $vm 'LibraryObservableCollection' "Bulk UI collection updates required"
AssertContains $main 'Flick.Library.FilterChip' "Library source chips missing"
AssertContains $main 'VirtualizingStackPanel.IsVirtualizing' "Library virtualization missing"
AssertContains $main 'BtnImportFolder_Click' "Import Folder entry is missing"
AssertContains $dialog 'Unlink Folder' "Connected folders need safe unlink"
AssertContains $dialog 'Cancel Scan' "Slow scans must be user-cancelable"
AssertContains $settings 'TxtExportsFolder' "Independent Export Folder missing"
AssertContains $settings 'ManageFolders_Click' "Settings must link to management"
AssertContains $settingsCs '_settings.ExportsFolder = exports;' "Export destination not persisted"
AssertContains $tests 'OfflineSourceAndMetadataSurviveRestart' "Offline retention regression missing"
AssertContains $tests 'RemovingImportedClipNeverDeletesOriginalOrExport' "Non-destructive unlink regression missing"
Write-Host "Flick GUI Slice 3 static contracts PASS — external source imports are local, bounded and non-destructive." -ForegroundColor Green
