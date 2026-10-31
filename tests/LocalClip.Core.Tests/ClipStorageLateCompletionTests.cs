using LocalClip.Core.Media;
using LocalClip.Core.Models;
using LocalClip.Core.Storage;

namespace LocalClip.Core.Tests;

public sealed class ClipStorageLateCompletionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovedOrUnlinkedClipCannotBeRestoredByHeldThumbnail(bool unlink)
    {
        string root = Path.Combine(Path.GetTempPath(), "Flick-Late-Thumbnail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var held = new HeldThumbnail();
        Task<string>? work = null;
        try
        {
            var settings = new AppSettings();
            string metadata = Path.Combine(root, "Metadata"), source = Path.Combine(root, "source.mkv");
            byte[] original = [1, 2, 3, 4]; File.WriteAllBytes(source, original);
            var storage = new ClipStorage(settings, held, metadata);
            var folder = storage.ConnectFolder(root);
            var clip = new ClipMetadata { OriginalFilePath = source, DurationSeconds = 10,
                TrimEndSeconds = 10, SourceKind = "Imported", ConnectedFolderId = folder.Id };
            storage.AddOrUpdateClip(clip);
            work = storage.EnsureThumbnailAsync(clip.Id);
            await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (unlink) Assert.True(storage.DisconnectFolder(folder.Id));
            else Assert.True(storage.DeleteClip(clip.Id));
            held.Release.TrySetResult();
            Assert.Equal("", await work.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Empty(storage.GetAllClips());
            Assert.Empty(new ClipStorage(settings, metadataDirectory: metadata).GetAllClips());
            Assert.Equal(original, File.ReadAllBytes(source));
        }
        finally
        {
            held.Release.TrySetResult();
            if (work != null) await work.WaitAsync(TimeSpan.FromSeconds(5));
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ConditionalCompletionRejectsChangedSourceButUpdatesAnUnchangedMember()
    {
        string root = Path.Combine(Path.GetTempPath(), "Flick-Late-Commit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var storage = new ClipStorage(new AppSettings(), metadataDirectory: root);
            var clip = new ClipMetadata { OriginalFilePath = Path.Combine(root, "original.mkv") };
            storage.AddOrUpdateClip(clip);
            string original = clip.OriginalFilePath;
            Assert.True(storage.TryUpdateExistingClip(clip, original, "", current => current.Title = "Committed"));
            clip.OriginalFilePath = Path.Combine(root, "replacement.mkv");
            Assert.False(storage.TryUpdateExistingClip(clip, original, "", current => current.Title = "Wrong source"));
            Assert.Equal("Committed", clip.Title);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task UnconfirmedProbeExitIsNotDowngradedToAnUnavailableImportedClip()
    {
        string root = Path.Combine(Path.GetTempPath(), "Flick-Scan-Cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "source.mkv"), [1]);
            var storage = new ClipStorage(new AppSettings(), new UnconfirmedProbe(), Path.Combine(root, "Metadata"));
            var folder = storage.ConnectFolder(root);
            await Assert.ThrowsAsync<LocalClip.Core.Audio.MediaWorkerCleanupException>(() => storage.ScanConnectedFolderAsync(folder.Id));
            Assert.Empty(storage.GetAllClips());
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class UnconfirmedProbe : MediaProber
    {
        public override Task<ProbeResult> ProbeFileAsync(string path, CancellationToken ct = default) =>
            Task.FromException<ProbeResult>(new LocalClip.Core.Audio.MediaWorkerCleanupException());
    }

    private sealed class HeldThumbnail : MediaProber
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task<string> GenerateThumbnailAsync(string source, string output,
            double seekSeconds = 1, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
            await File.WriteAllBytesAsync(output, [5, 6, 7], cancellationToken);
            return output;
        }
    }
}
