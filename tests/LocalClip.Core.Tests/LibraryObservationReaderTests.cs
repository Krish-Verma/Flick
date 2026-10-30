using LocalClip.Core.Models;
using LocalClip.Core.Storage;

namespace LocalClip.Core.Tests;

public class LibraryObservationReaderTests
{
    [Fact]
    public async Task OneBlockedReadCannotSpawnMoreWorkersAndOnlyLatestQueuedPurposeRuns()
    {
        using var reader = new LibraryObservationReader();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();
        int active = 0, maximumActive = 0;
        string Read(string name, bool held = false)
        {
            int concurrent = Interlocked.Increment(ref active);
            maximumActive = Math.Max(maximumActive, concurrent);
            try
            {
                lock (order) order.Add(name);
                if (held) { started.TrySetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); }
                return name;
            }
            finally { Interlocked.Decrement(ref active); }
        }
        Task<string> first = reader.ReadAsync(LibraryObservationPriority.Availability, _ => Read("active", true), default);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task<string> history = reader.ReadAsync(LibraryObservationPriority.RecentExports, _ => Read("history"), default);
            Task<string> oldAvailability = reader.ReadAsync(LibraryObservationPriority.Availability, _ => Read("old-availability"), default);
            Task<string> availability = reader.ReadAsync(LibraryObservationPriority.Availability, _ => Read("availability"), default);
            Task<string> oldSelection = reader.ReadAsync(LibraryObservationPriority.SelectedClip, _ => Read("old-selection"), default);
            Task<string> selection = reader.ReadAsync(LibraryObservationPriority.SelectedClip, _ => Read("selection"), default);
            Assert.True(oldAvailability.IsCanceled);
            Assert.True(oldSelection.IsCanceled);
            Assert.Equal(1, Volatile.Read(ref active));
            release.Set();
            await Task.WhenAll(first, history, availability, selection).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, maximumActive);
            Assert.Equal(new[] { "active", "selection", "availability", "history" }, order);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task DisposalCancelsPresentationPromisesWithoutWaitingForUncancelableNativeRead()
    {
        var reader = new LibraryObservationReader();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queuedRan = false;
        Task<bool> read = reader.ReadAsync(LibraryObservationPriority.Availability, _ =>
        {
            started.TrySetResult();
            try { Assert.True(release.Wait(TimeSpan.FromSeconds(5))); return true; }
            finally { finished.TrySetResult(); }
        }, default);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task<bool> queued = reader.ReadAsync(LibraryObservationPriority.SelectedClip, _ => queuedRan = true, default);
            reader.Dispose();
            Assert.True(read.IsCanceled);
            Assert.True(queued.IsCanceled);
            Assert.False(finished.Task.IsCompleted); // Dispose returned while the native-style read is still held.
            Assert.False(queuedRan);
            release.Set();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.Set(); reader.Dispose(); }
    }

    [Fact]
    public void UiUnknownAndStalePathObservationsNeverPerformAFreshFileProbe()
    {
        var clip = new ClipMetadata { OriginalFilePath = "invalid\0path" };
        Assert.False(clip.HasKnownAvailability);
        Assert.False(clip.IsAvailableForUi);
        Assert.Equal("Checking…", clip.LibraryStatusForUi);
        Assert.False(clip.ApplyAvailabilityObservation("old path", true));
        Assert.False(clip.HasKnownAvailability);
        var notifications = new List<string?>();
        clip.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        Assert.True(clip.ApplyAvailabilityObservation(clip.OriginalFilePath, false));
        Assert.True(clip.HasKnownAvailability);
        Assert.False(clip.IsAvailableForUi);
        Assert.Equal("Offline", clip.LibraryStatusForUi);
        Assert.Contains(nameof(ClipMetadata.IsAvailableForUi), notifications);
        Assert.True(clip.ApplyAvailabilityObservation(clip.OriginalFilePath, true));
        Assert.True(clip.IsAvailableForUi);
        clip.OriginalFilePath = "replacement\0path";
        Assert.False(clip.HasKnownAvailability);
        Assert.False(clip.IsAvailableForUi);
        Assert.Equal("Checking…", clip.LibraryStatusForUi);
        Assert.False(clip.ApplyAvailabilityObservation("invalid\0path", false));
    }

    [Fact]
    public void NonUiFreshFallbackAndLegacyStatusRemainCompatible()
    {
        string file = Path.Combine(Path.GetTempPath(), "Flick-availability-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clip = new ClipMetadata { OriginalFilePath = file };
            Assert.False(clip.IsAvailable);
            File.WriteAllBytes(file, [1]);
            Assert.True(clip.IsAvailable);
            Assert.False(clip.HasKnownAvailability); // A non-UI fallback is not an asynchronous UI observation.
            clip.RefreshAvailability();
            Assert.True(clip.IsAvailableForUi);
            File.Delete(file);
            clip.RefreshAvailability();
            Assert.False(clip.IsAvailable);
            Assert.Equal("Offline", clip.LibraryStatus);
        }
        finally { File.Delete(file); }
    }
}
