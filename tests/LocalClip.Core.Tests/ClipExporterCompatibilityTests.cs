using LocalClip.Core.Media;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;

namespace LocalClip.Core.Tests;

public class ClipExporterCompatibilityTests
{
    [Fact]
    public async Task FailedNvencUsesCpuAndPromotesOnlyAValidatedShareFile()
    {
        using var fixture = new ExportFixture();
        Assert.False(fixture.Settings.UseTwoPassSoftwareIfHardwareOvershoots);
        fixture.Settings.AllowSoftwareFallback = false; // This preference controls recording, not explicit exports.
        var exporter = new FixtureExporter([new(false, 128), new(true, 100_000)]);
        var result = await exporter.ExportForDiscordAsync(fixture.Clip, fixture.Settings);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(new[] { false, true }, exporter.SoftwareAttempts);
        Assert.Equal(2, result.AttemptsUsed);
        Assert.Equal(1, exporter.ProbeCalls);
        Assert.Equal(1, exporter.DecodeCalls);
        Assert.Equal(100_000, new FileInfo(result.ExportFilePath).Length);
        Assert.Equal(fixture.OriginalBytes, File.ReadAllBytes(fixture.Clip.OriginalFilePath));
        Assert.Empty(Directory.EnumerateFiles(fixture.Settings.ExportsFolder, ".flick-export-*"));
    }

    [Fact]
    public async Task CpuFallbackStaysSelectedWhenItsOutputNeedsASmallerBitrate()
    {
        using var fixture = new ExportFixture();
        var exporter = new FixtureExporter([new(false, 64), new(true, 3_000_000), new(true, 100_000)]);
        var result = await exporter.ExportForDiscordAsync(fixture.Clip, fixture.Settings);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(new[] { false, true, true }, exporter.SoftwareAttempts);
        Assert.True(exporter.Budgets[2].VideoBitrateBps < exporter.Budgets[1].VideoBitrateBps);
        Assert.InRange(result.FileSizeBytes, 1, 2_000_000);
        Assert.Empty(Directory.EnumerateFiles(fixture.Settings.ExportsFolder, ".flick-export-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OvershootPreferenceRemainsIndependentFromHardwareFailure(bool useSoftwareForOvershoot)
    {
        using var fixture = new ExportFixture();
        fixture.Settings.UseTwoPassSoftwareIfHardwareOvershoots = useSoftwareForOvershoot;
        var exporter = new FixtureExporter([new(true, 3_000_000), new(true, 3_000_000), new(true, 100_000)]);
        var result = await exporter.ExportForDiscordAsync(fixture.Clip, fixture.Settings);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(new[] { false, false, useSoftwareForOvershoot }, exporter.SoftwareAttempts);
    }

    [Fact]
    public async Task AllEncoderFailuresStopAtThreeAttemptsAndRemovePartialOutputs()
    {
        using var fixture = new ExportFixture();
        var exporter = new FixtureExporter([new(false, 128), new(false, 128), new(false, 128)]);
        var result = await exporter.ExportForDiscordAsync(fixture.Clip, fixture.Settings);
        Assert.False(result.Success);
        Assert.Equal(3, result.AttemptsUsed);
        Assert.Equal(new[] { false, true, true }, exporter.SoftwareAttempts);
        Assert.Empty(Directory.EnumerateFiles(fixture.Settings.ExportsFolder));
        Assert.Equal(fixture.OriginalBytes, File.ReadAllBytes(fixture.Clip.OriginalFilePath));
    }

    [Fact]
    public async Task CancellationAfterHardwareFailureDoesNotStartAFallbackOrLeaveAPartialFile()
    {
        using var fixture = new ExportFixture();
        using var cancellation = new CancellationTokenSource();
        var exporter = new FixtureExporter([new(false, 128)], cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            exporter.ExportForDiscordAsync(fixture.Clip, fixture.Settings, ct: cancellation.Token));
        Assert.Equal(new[] { false }, exporter.SoftwareAttempts);
        Assert.Empty(Directory.EnumerateFiles(fixture.Settings.ExportsFolder));
    }

    [Fact]
    public async Task CpuFileThatFailsDecodeIsNeverPromotedForSharing()
    {
        using var fixture = new ExportFixture();
        var exporter = new FixtureExporter([new(false, 128), new(true, 100_000)]) { DecodeClean = false };
        var result = await exporter.ExportForDiscordAsync(fixture.Clip, fixture.Settings);
        Assert.False(result.Success);
        Assert.Equal(new[] { false, true }, exporter.SoftwareAttempts);
        Assert.Equal(1, exporter.DecodeCalls);
        Assert.Empty(Directory.EnumerateFiles(fixture.Settings.ExportsFolder));
    }

    private sealed record EncodeOutcome(bool Success, int Bytes);

    // Run the real export retry/budget/verification/promotion flow, replacing
    // only native media operations. These bytes are test fixtures, never video.
    private sealed class FixtureExporter(IReadOnlyList<EncodeOutcome> outcomes,
        CancellationTokenSource? cancelAfterEncode = null) : ClipExporter
    {
        public List<bool> SoftwareAttempts { get; } = [];
        public List<BitrateBudget> Budgets { get; } = [];
        public int ProbeCalls { get; private set; }
        public int DecodeCalls { get; private set; }
        public bool DecodeClean { get; init; } = true;

        protected override Task<bool> RunFfmpegEncodeAsync(string inputPath, string outputPath,
            double startSec, double durationSec, BitrateBudget budget, string audioFilter,
            bool useSoftware, CancellationToken ct)
        {
            var outcome = outcomes[SoftwareAttempts.Count];
            SoftwareAttempts.Add(useSoftware);
            Budgets.Add(budget);
            File.WriteAllBytes(outputPath, new byte[outcome.Bytes]);
            cancelAfterEncode?.Cancel();
            return Task.FromResult(outcome.Success);
        }

        protected override Task<ProbeResult> ProbeExportAsync(string filePath, CancellationToken ct)
        {
            ProbeCalls++;
            return Task.FromResult(new ProbeResult {
                IsValid = true, DurationSeconds = 10, VideoCodec = "h264",
                AudioTracks = [new AudioTrackInfo { Codec = "aac" }]
            });
        }

        protected override Task<bool> VerifyFullDecodeAsync(string filePath, CancellationToken ct)
        { DecodeCalls++; return Task.FromResult(DecodeClean); }
    }

    private sealed class ExportFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "flick-export-fallback-" + Guid.NewGuid().ToString("N"));
        public byte[] OriginalBytes { get; } = [1, 2, 3, 4];
        public AppSettings Settings { get; }
        public ClipMetadata Clip { get; }
        public ExportFixture()
        {
            Directory.CreateDirectory(_root);
            string source = Path.Combine(_root, "source.mkv");
            File.WriteAllBytes(source, OriginalBytes);
            Settings = AppSettings.CreateDefault();
            Settings.ExportsFolder = Path.Combine(_root, "Exports");
            Settings.DiscordTargetSizeMb = 2;
            Clip = new ClipMetadata {
                OriginalFilePath = source, DurationSeconds = 10, TrimEndSeconds = 10,
                Width = 1920, Height = 1080, AudioTracks = [new AudioTrackInfo { TrackIndex = 0, Codec = "aac" }]
            };
        }
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
