# Flick GUI Slice 4 source contract check. Does not test actual WPF media playback.
$ErrorActionPreference = "Stop"
$root = (Get-Item $PSScriptRoot).Parent.FullName
function RepoText([string]$path) {
    $file = Join-Path $root $path
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing: $path" }
    return Get-Content -LiteralPath $file -Raw
}
function Require([string]$content, [string]$pattern, [string]$message) {
    if ($content -notmatch $pattern) { throw "Slice 4 contract violation: $message" }
}
$main = RepoText "src/LocalClip.App/MainWindow.xaml"
$code = RepoText "src/LocalClip.App/MainWindow.xaml.cs"
$trim = RepoText "src/LocalClip.App/Views/Components/TrimRangeControl.xaml"
$trimCs = RepoText "src/LocalClip.App/Views/Components/TrimRangeControl.xaml.cs"
$math = RepoText "src/LocalClip.Core/Rules/TrimTimelineMath.cs"
$validator = RepoText "src/LocalClip.Core/Rules/TrimValidator.cs"
$vm = RepoText "src/LocalClip.App/ViewModels/MainViewModel.cs"
$media = RepoText "src/LocalClip.Core/Media/MediaProber.cs"
$policy = RepoText "src/LocalClip.Core/Rules/PlaybackPreviewPolicy.cs"
$model = RepoText "src/LocalClip.Core/Models/ClipInfo.cs"
$tests = RepoText "tests/LocalClip.Core.Tests/TrimTimelineTests.cs"
$moreTests = RepoText "tests/LocalClip.Core.Tests/Slice4EditingTests.cs"

Require $main 'components:TrimRangeControl' "Single timeline missing"
Require $main 'TrimTimeline_RangeCommitted' "Trim commit event not wired"
Require $main 'CanEditClip' "Cannot gate editing on export/offline state"
Require $main 'AudioExpander' "Expandable audio panel missing"
Require $main 'IsExpanded="False"' "Audio panel should start collapsed"
Require $main 'BtnPreviewMode' "Original/Export Preview mode menu missing"
Require $main 'Flick.VideoAspectHeight' "Preview must use responsive aspect sizing"
Require $main 'PreviewFallbackPanel' "Playback failure state missing"
Require $main 'BuildPlayablePreview_Click' "Manual preview copy option missing"
Require $main 'Grid.Row="1"' "Sticky Discord export dock missing"
Require $main 'DragHandle_MouseLeftButtonDown' "Native Discord drag entry missing"
if ($main -match 'x:Name="SliderTrimStart"' -or $main -match 'x:Name="SliderTrimEnd"') {
    throw "Old separate trim slider layout must be removed"
}
Require $trim 'AutomationProperties.Name="Trim in handle"' "Keyboard-accessible trim In handle missing"
Require $trim 'AutomationProperties.Name="Trim out handle"' "Keyboard-accessible trim Out handle missing"
Require $trimCs 'Handle_PreviewKeyDown' "Keyboard trim movement missing"
Require $trimCs 'TimeBox_PreviewKeyDown' "Precise start/end input missing"
Require $trimCs 'RangeCommitted\?\.Invoke' "Trim save only after user commit"
Require $math 'MinSelectionSeconds = 0.25' "Quarter-second trim safety missing"
Require $validator 'double.IsFinite' "NaN/infinity validation missing"
Require $vm '_audioEditCommitTimer' "Audio writes must be debounced"
Require $vm 'AudioTrackPropertyChanged' "Audio Ready invalidation must follow real model edits"
Require $vm 'AudioTrackRoles.Reset' "Reset Mix must respect recorded source provenance"
Require $vm 'FlushPendingAudioEdits' "Pending edits must be saved when changing clips/exiting"
Require $vm 'ApplyTrim' "Validated non-destructive trim changes missing"
Require $vm 'CanEditClip' "Editable state gate missing"
Require $model 'INotifyPropertyChanged' "Audio row values must notify UI"
Require $code 'CreateLocalPlaybackPreviewAsync' "Owned compatibility preview is not wired"
$previewVm = RepoText "src/LocalClip.App/ViewModels/MainViewModel.Audio.cs"
Require $previewVm 'BuildPlaybackPreviewAsync' "Shared owned worker must invoke the real FFmpeg preview"
Require $previewVm '_audioPreviewOperation.StartAsync' "Both preview modes must share worker ownership"
Require $code 'IsCaptureActive' "Gameplay must preempt preview conversion"
Require $code 'SuspendPlayback' "Preview should not keep decoding while minimized"
Require $media 'PreviewCache' "Local playback cache missing"
Require $media 'TimeSpan.FromSeconds\(90\)' "Preview encoder must have timeout"
Require $media 'JobObject' "Preview FFmpeg process must be owned"
Require $media 'CancellationToken' "Preview conversion must be cancelable"
Require $media 'PlaybackPreviewPolicy' "Preview conversion budget missing"
Require $code 'CancelPendingPreview' "Mode/clip switches must cancel compatibility jobs"
$audioDefaults = RepoText "src/LocalClip.Core/Rules/AudioMixDefaults.cs"
Require $audioDefaults 'ShouldMute' "Conservative source-aware audio defaults missing"
Require $policy 'MaximumSeconds = 180' "Compatibility preview limit missing"
Require $tests 'RejectsNonsensicalTimes' "Trim time parser tests missing"
Require $moreTests 'AudioMixChangesNotifyImmediately' "Audio UI tests missing"
Write-Host "Flick GUI Slice 4 static contracts PASS; Windows playback/visual checks are still required." -ForegroundColor Green
