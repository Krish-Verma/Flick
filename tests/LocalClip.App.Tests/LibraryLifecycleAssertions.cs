using System.IO;
using System.Reflection;
using System.Windows.Threading;
using LocalClip.App.ViewModels;
using LocalClip.Core.Storage;
using Xunit;

namespace LocalClip.App.Tests;

/// <summary>Held owned operations exercise the real favorite/export/quit continuations; no codec or capture process.</summary>
internal static class LibraryLifecycleAssertions
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(MainWindow window, MainViewModel vm)
    {
        var before = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        try
        {
            using var fixture = new ResearchPresentationAssertions.OwnedLibrary(window, vm);
            FavoriteAndPosterDoNotStrandReadiness(vm, fixture);
            foreach (bool cancel in new[] { false, true })
                foreach (bool changeSelection in new[] { true, false })
                    ExportTeardownBelongsToTheCorrectClip(vm, fixture, cancel, changeSelection);
            RefusedQuitRestartsCancelledObservations(vm, fixture);
        }
        finally { SynchronizationContext.SetSynchronizationContext(before); }
    }

    private static object? Get(MainViewModel vm, string name) => typeof(MainViewModel).GetField(name, Hidden)!.GetValue(vm);
    private static void Invoke(MainViewModel vm, string name, params object?[] args) => typeof(MainViewModel).GetMethod(name, Hidden)!.Invoke(vm, args);

    private static void FavoriteAndPosterDoNotStrandReadiness(MainViewModel vm, ResearchPresentationAssertions.OwnedLibrary fixture)
    {
        vm.SelectedClip = fixture.First;
        LibraryObservationAssertions.Settle(vm);
        fixture.PrepareReadySentinel();
        fixture.First.LastExportedAtUtc = DateTimeOffset.UtcNow;
        var reader = (LibraryObservationReader)Get(vm, "_libraryReader")!;
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task blocker = reader.ReadAsync(LibraryObservationPriority.Availability, _ =>
        { started.TrySetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(8))); return true; }, default);
        try
        {
            LibraryObservationAssertions.PumpUntil(() => started.Task.IsCompleted, Dispatcher.CurrentDispatcher);
            vm.RecheckShareReadiness();
            Invoke(vm, "RefreshRecentExports");
            vm.ToggleFavorite(); // Writes only the fixture's index and changes metadata while reads are queued.
            fixture.First.ThumbnailPath += ".replacement"; // Thumbnail fencing is independent from file validity.
            release.Set();
            LibraryObservationAssertions.Pump(blocker);
            LibraryObservationAssertions.Settle(vm);
            Assert.True(vm.IsExportReady);
            Assert.Contains(vm.RecentExports, item => item.ClipId == fixture.First.Id);
            Assert.StartsWith("Ready to share", vm.ExportStatusMessage);
        }
        finally { release.Set(); LibraryObservationAssertions.Pump(blocker); LibraryObservationAssertions.Settle(vm); }

        // Actual trim changes still invalidate a prepared file and an in-flight old check.
        vm.RecheckShareReadiness();
        Assert.True(vm.ApplyTrim(1, 8));
        LibraryObservationAssertions.Settle(vm);
        Assert.False(vm.IsExportReady);
        Assert.False(fixture.First.IsExportReady);
    }

    private static void ExportTeardownBelongsToTheCorrectClip(MainViewModel vm,
        ResearchPresentationAssertions.OwnedLibrary fixture, bool cancel, bool changeSelection)
    {
        File.WriteAllBytes(fixture.First.OriginalFilePath, [1, 2, 3, 4]);
        // The previous poster replacement does not need generation for this state-only fixture.
        fixture.First.ThumbnailPath = fixture.Offline.ThumbnailPath;
        vm.SelectedClip = fixture.First;
        vm.RecheckShareReadiness();
        LibraryObservationAssertions.Settle(vm);
        Assert.True(vm.CanStartExport);
        File.Delete(fixture.First.OriginalFilePath); // Fresh exporter guard fails before invoking any codec process.
        var pending = (HashSet<Task>)Get(vm, "_thumbnailOperations")!;
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.Empty(pending);
        pending.Add(cleanup.Task);
        Task? export = null;
        try
        {
            export = vm.ExportForDiscordAsync();
            Assert.True(vm.IsExporting);
            Assert.Equal(fixture.First.Id, vm.ExportingClipId);
            if (changeSelection)
            {
                vm.SelectedClip = fixture.Offline;
                Assert.Equal("Another clip is exporting", vm.ShareTileStateText);
            }
            if (cancel) vm.CancelExport();
            pending.Remove(cleanup.Task);
            fixture.Set("_thumbnailStopPending", false);
            cleanup.TrySetResult();
            LibraryObservationAssertions.Pump(export);
            LibraryObservationAssertions.Settle(vm);
            Assert.False(vm.IsExporting);
            Assert.Null(vm.ExportingClipId);
            if (changeSelection)
            {
                Assert.Same(fixture.Offline, vm.SelectedClip);
                Assert.True(fixture.Offline.HasKnownAvailability);
                Assert.StartsWith("Original video offline", vm.ExportStatusMessage);
            }
            else Assert.StartsWith(cancel ? "Export cancelled" : "Export failed", vm.ExportStatusMessage);
            Assert.False(vm.CaptureHost.IsRunning);
        }
        finally
        {
            pending.Remove(cleanup.Task); fixture.Set("_thumbnailStopPending", false); cleanup.TrySetResult();
            if (export != null) LibraryObservationAssertions.Pump(export);
            LibraryObservationAssertions.Settle(vm);
        }
    }

    private static void RefusedQuitRestartsCancelledObservations(MainViewModel vm, ResearchPresentationAssertions.OwnedLibrary fixture)
    {
        fixture.First.OriginalFilePath += ".unknown";
        vm.SelectedClip = fixture.First;
        LibraryObservationAssertions.Settle(vm);
        // Change the source again to start from a genuinely unknown cache.
        fixture.First.OriginalFilePath += ".pending";
        var reader = (LibraryObservationReader)Get(vm, "_libraryReader")!;
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task blocker = reader.ReadAsync(LibraryObservationPriority.Availability, _ =>
        { started.TrySetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); return true; }, default);
        object? completionBefore = Get(vm, "_diagnosticExportCompletion"), busyBefore = Get(vm, "_isExportingDiagnostics");
        var unfinishedExport = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            LibraryObservationAssertions.PumpUntil(() => started.Task.IsCompleted, Dispatcher.CurrentDispatcher);
            vm.RecheckShareReadiness();
            Assert.False(fixture.First.HasKnownAvailability);
            int selectedGeneration = (int)Get(vm, "_selectedLibraryGeneration")!;
            fixture.Set("_diagnosticExportCompletion", unfinishedExport);
            fixture.Set("_isExportingDiagnostics", true);
            Task<bool> quit = vm.PrepareToQuitAsync();
            LibraryObservationAssertions.Pump(quit); // Actual owned diagnostic-export timeout refuses quit after 3s.
            Assert.False(quit.GetAwaiter().GetResult());
            Assert.False((bool)Get(vm, "_isPreparingToQuit")!);
            Assert.True((int)Get(vm, "_selectedLibraryGeneration")! > selectedGeneration + 1);
            Assert.IsType<DispatcherTimer>(Get(vm, "_pollTimer")).Stop();
            (Get(vm, "_gameTimer") as DispatcherTimer)?.Stop();
            fixture.Set("_diagnosticExportCompletion", completionBefore);
            fixture.Set("_isExportingDiagnostics", busyBefore);
            release.Set();
            LibraryObservationAssertions.Pump(blocker);
            LibraryObservationAssertions.Settle(vm);
            Assert.True(fixture.First.HasKnownAvailability);
            Assert.False(fixture.First.IsAvailableForUi);
            Assert.StartsWith("Original video offline", vm.ExportStatusMessage);
            Assert.False(vm.CaptureHost.IsRunning);
        }
        finally
        {
            release.Set(); unfinishedExport.TrySetResult();
            fixture.Set("_diagnosticExportCompletion", completionBefore); fixture.Set("_isExportingDiagnostics", busyBefore);
            Assert.IsType<DispatcherTimer>(Get(vm, "_pollTimer")).Stop(); (Get(vm, "_gameTimer") as DispatcherTimer)?.Stop();
            LibraryObservationAssertions.Pump(blocker); LibraryObservationAssertions.Settle(vm);
        }
    }
}
