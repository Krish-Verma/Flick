using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using LocalClip.App.ViewModels;
using LocalClip.Core.Models;
using LocalClip.Core.Storage;
using Xunit;

namespace LocalClip.App.Tests;

/// <summary>Existing isolated WPF fixture; a held injected stat proves the dispatcher stays available.</summary>
internal static class LibraryObservationAssertions
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Settle(MainViewModel vm) => Pump(vm.WhenLibraryPresentationSettledAsync());

    public static void Run(MainViewModel vm)
    {
        Settle(vm);
        var dispatcher = Dispatcher.CurrentDispatcher;
        FieldInfo Field(string name) => typeof(MainViewModel).GetField(name, Hidden)!;
        string[] fields = ["_storage", "_allClips", "_selectedClip", "_libraryPathExists", "_searchQuery", "_clipSourceFilter",
            "_favoritesOnly", "_gameFilter", "_librarySort", "_libraryPeriod", "_tagsDraft", "_gameDraft", "_editorSaveState",
            "_isExportReady", "_readyExportFilePath", "_exportStatusMessage"];
        var before = fields.ToDictionary(name => name, name => Field(name).GetValue(vm));
        var gamesBefore = vm.GameGroups.ToArray();
        var recentBefore = vm.RecentExports.ToArray();
        var recentIds = (HashSet<string>)Field("_recentExportIds").GetValue(vm)!;
        var idsBefore = recentIds.ToArray();
        string preferences = JsonSerializer.Serialize(vm.Settings);
        string root = Path.Combine(Path.GetTempPath(), "Flick-Availability-UI-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "slow-original.mkv"), offline = Path.Combine(root, "offline.mkv");
        var first = new ClipMetadata { Id = "availability-first", Title = "First", OriginalFilePath = source,
            ThumbnailPath = Path.Combine(root, "poster.png"), DurationSeconds = 10, TrimEndSeconds = 10, CreatedAt = DateTime.Now };
        var second = new ClipMetadata { Id = "availability-second", Title = "Second", OriginalFilePath = offline,
            ThumbnailPath = first.ThumbnailPath, DurationSeconds = 10, TrimEndSeconds = 10, CreatedAt = DateTime.Now.AddSeconds(-1) };
        var storage = new ClipStorage(new AppSettings(), metadataDirectory: Path.Combine(root, "Metadata"));
        storage.AddOrUpdateClip(first); storage.AddOrUpdateClip(second);
        string index = Path.Combine(root, "Metadata", "clips_index.json");
        byte[] indexBefore = File.ReadAllBytes(index);
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int sourceReads = 0, falseApplied = 0, selectedEvents = 0;
        first.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ClipMetadata.IsAvailableForUi) && first.HasKnownAvailability && !first.IsAvailableForUi) falseApplied++;
        };
        System.ComponentModel.PropertyChangedEventHandler changed = (_, args) =>
        { if (args.PropertyName == nameof(MainViewModel.SelectedClip)) selectedEvents++; };
        try
        {
            typeof(MainViewModel).GetMethod("UnsubscribeAudioEvents", Hidden)!.Invoke(vm, null);
            Field("_selectedClip").SetValue(vm, null);
            Field("_storage").SetValue(vm, storage);
            Field("_libraryPathExists").SetValue(vm, (Func<string, bool>)(path =>
            {
                Assert.False(dispatcher.CheckAccess());
                if (path == source && Interlocked.Increment(ref sourceReads) == 1)
                {
                    started.TrySetResult();
                    Assert.True(release.Wait(TimeSpan.FromSeconds(8)));
                    return false; // Obsolete result must never become an Offline presentation.
                }
                return path != offline;
            }));
            vm.ClearLibrarySearch(); vm.ClearLibraryFilters();
            vm.ReloadClips();
            PumpUntil(() => started.Task.IsCompleted, dispatcher);
            Assert.Equal(2, vm.FilteredClips.Count);
            Assert.False(first.HasKnownAvailability);
            Assert.Equal("Checking…", first.LibraryStatusForUi);
            Assert.False(first.IsAvailableForUi);
            // The UI can change selection/reload while the one physical stat is held.
            vm.SelectedClip = second;
            vm.PropertyChanged += changed;
            vm.ReloadClips();
            Assert.Equal(0, selectedEvents);
            release.Set();
            Settle(vm);
            Assert.True(first.HasKnownAvailability);
            Assert.True(first.IsAvailableForUi);
            Assert.Equal(0, falseApplied);
            Assert.True(second.HasKnownAvailability);
            Assert.False(second.IsAvailableForUi);
            Assert.Equal("Offline", second.LibraryStatusForUi);
            Assert.Equal(0, selectedEvents); // Availability never impersonates a new selection/close-editor command.
            Assert.True(vm.SelectedClipAvailabilityRevision > 0);
            Assert.Equal(indexBefore, File.ReadAllBytes(index));
            Assert.Equal(preferences, JsonSerializer.Serialize(vm.Settings));
            Assert.Equal(source, first.OriginalFilePath);
            Assert.False(vm.CaptureHost.IsRunning);
        }
        finally
        {
            release.Set();
            vm.PropertyChanged -= changed;
            Settle(vm);
            typeof(MainViewModel).GetMethod("UnsubscribeAudioEvents", Hidden)!.Invoke(vm, null);
            vm.GameGroups.Clear(); foreach (string game in gamesBefore) vm.GameGroups.Add(game);
            vm.RecentExports.Clear(); foreach (var recent in recentBefore) vm.RecentExports.Add(recent);
            recentIds.Clear(); foreach (string id in idsBefore) recentIds.Add(id);
            foreach (var (name, value) in before) Field(name).SetValue(vm, value);
            typeof(MainViewModel).GetMethod("SubscribeAudioEvents", Hidden)!.Invoke(vm, null);
            vm.ApplyFilter();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void Pump(Task task)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var guard = new DispatcherTimer(DispatcherPriority.Send, dispatcher) { Interval = TimeSpan.FromSeconds(12) };
            guard.Tick += (_, _) => frame.Continue = false;
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            guard.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { guard.Stop(); }
        }
        Assert.True(task.IsCompleted, "Read-only library presentation did not settle while pumping the dispatcher.");
        task.GetAwaiter().GetResult();
    }
    private static void PumpUntil(Func<bool> done, Dispatcher dispatcher)
    {
        var elapsed = Stopwatch.StartNew();
        while (!done() && elapsed.Elapsed < TimeSpan.FromSeconds(3))
        { dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); Thread.Sleep(1); }
        Assert.True(done(), "The owned stat fixture did not start.");
    }
}
