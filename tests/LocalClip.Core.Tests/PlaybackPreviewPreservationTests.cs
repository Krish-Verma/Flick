using LocalClip.Core.Media;

namespace LocalClip.Core.Tests;

public sealed class PlaybackPreviewPreservationTests
{
    [Fact]
    public async Task MissingEncoderDoesNotPruneAnyOfTwentyFivePreexistingCacheVideos()
    {
        string root = Path.Combine(Path.GetTempPath(), "Flick-Preview-Preservation-" + Guid.NewGuid().ToString("N"));
        string cache = Path.Combine(root, "PreviewCache");
        Directory.CreateDirectory(cache);
        try
        {
            // This directory may legally be an originals/export/import root.
            // Its name and these MP4 extensions establish no cache ownership.
            var originals = new Dictionary<string, byte[]>();
            for (int i = 0; i < 25; i++)
            {
                string file = Path.Combine(cache, $"user-video-{i:00}.mp4");
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes($"User-owned media sentinel {i}");
                File.WriteAllBytes(file, bytes);
                File.SetLastWriteTimeUtc(file, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i));
                originals.Add(file, bytes);
            }
            string original = originals.Keys.First(); // Old pruning deleted the selected oldest source.
            string missingEncoder = Path.Combine(root, "missing-owned-encoder.exe");
            var prober = new MetadataOnlyProber(missingEncoder);
            var error = await Assert.ThrowsAsync<FileNotFoundException>(() => prober.BuildPlaybackPreviewAsync(
                original, cacheDirectory: cache).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(missingEncoder, error.FileName);
            Assert.Equal(new[] { original }, prober.ProbedPaths);
            Assert.Equal(25, Directory.GetFiles(cache, "*", SearchOption.AllDirectories).Length);
            foreach (var (file, bytes) in originals)
                Assert.Equal(bytes, File.ReadAllBytes(file));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task FullDedicatedNamespaceRefusesAdmissionWithoutDeletingAnyExistingVideo()
    {
        string root = Path.Combine(Path.GetTempPath(), "Flick-Preview-Capacity-" + Guid.NewGuid().ToString("N"));
        string space = Path.Combine(root, MediaProber.PlaybackPreviewNamespace);
        Directory.CreateDirectory(space);
        try
        {
            string original = Path.Combine(root, "original.mp4");
            File.WriteAllBytes(original, [1, 2, 3]);
            for (int i = 0; i < MediaProber.MaximumCachedPlaybackPreviews; i++)
                File.WriteAllBytes(Path.Combine(space, $"existing-{i:00}.mp4"), [(byte)i, 42]);
            var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllBytes);
            var prober = new MetadataOnlyProber(Path.Combine(root, "missing-owned-encoder.exe"));
            var error = await Assert.ThrowsAsync<PlaybackPreviewCacheLimitException>(() =>
                prober.BuildPlaybackPreviewAsync(original, cacheDirectory: root).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(space, error.CacheDirectory);
            Assert.Equal(before.Count, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
            foreach (var (file, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(file));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ReservationAndPublicationNeverOverwriteAnExactPreexistingDestination()
    {
        string root = Path.Combine(Path.GetTempPath(), "Flick-Preview-Collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string existing = Path.Combine(root, "user-owned.mp4"), temporary = Path.Combine(root, ".preview-owned.mp4");
            byte[] userBytes = [1, 2, 3, 4], generated = [5, 6, 7, 8];
            File.WriteAllBytes(existing, userBytes);
            Assert.ThrowsAny<IOException>(() => MediaProber.ReservePlaybackPreviewFile(existing));
            Assert.Equal(userBytes, File.ReadAllBytes(existing));
            MediaProber.ReservePlaybackPreviewFile(temporary);
            File.WriteAllBytes(temporary, generated);
            Assert.ThrowsAny<IOException>(() => MediaProber.PublishPlaybackPreviewFile(temporary, existing));
            Assert.Equal(userBytes, File.ReadAllBytes(existing));
            Assert.Equal(generated, File.ReadAllBytes(temporary));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class MetadataOnlyProber(string missingEncoder) : MediaProber(
        ffprobePath: missingEncoder, ffmpegPath: missingEncoder)
    {
        public List<string> ProbedPaths { get; } = [];
        public override Task<ProbeResult> ProbeFileAsync(string path, CancellationToken cancellationToken = default)
        {
            ProbedPaths.Add(path);
            return Task.FromResult(new ProbeResult { IsValid = true, DurationSeconds = 30,
                VideoCodec = "hevc", Width = 1920, Height = 1080 });
        }
    }
}
