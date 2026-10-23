using System.IO;
using System.Security.Cryptography;
using LocalClip.App.Services;
using LocalClip.Core.Updates;
using Xunit;

namespace LocalClip.App.Tests;

public class UpdateInstallerVerificationTests
{
    [Fact]
    public async Task ChangedBytesAndSizesAreRejectedBeforeExecution()
    {
        using var fixture = new InstallerFixture();
        var release = fixture.Release("v1.0.4-preview.1");
        var wrongHash = new DownloadedUpdate(fixture.Path, new string('0', 64), release);
        await Assert.ThrowsAsync<InvalidDataException>(() => FlickUpdateService.PrepareInstallationAsync(wrongHash, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => FlickUpdateService.PrepareInstallationAsync(
            fixture.Download(release with { InstallerSize = release.InstallerSize + 1 }), CancellationToken.None));
    }

    [Fact]
    public async Task UnsignedStableCannotMasqueradeAsPreviewThroughBuildMetadata()
    {
        using var fixture = new InstallerFixture();
        await Assert.ThrowsAsync<InvalidDataException>(() => FlickUpdateService.PrepareInstallationAsync(
            fixture.Download(fixture.Release("v1.0.4+test-with-hyphens")), CancellationToken.None));
    }

    [Fact]
    public async Task UnsignedPreviewRequiresCallerConsentAndHoldsDenyWriteDeleteLock()
    {
        using var fixture = new InstallerFixture();
        using (var ready = await FlickUpdateService.PrepareInstallationAsync(
            fixture.Download(fixture.Release("v1.0.4-preview.1")), CancellationToken.None))
        {
            Assert.True(ready.IsUnsignedPreview); // UI must explicitly warn/confirm; no installer is run here.
            Assert.Throws<IOException>(() => File.Open(fixture.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));
            Assert.Throws<IOException>(() => File.Delete(fixture.Path));
        }
        File.Delete(fixture.Path);
        Assert.False(File.Exists(fixture.Path));
    }

    [Fact]
    public async Task CancellationAndOutOfStagingPathsNeverBecomeExecutable()
    {
        using var fixture = new InstallerFixture();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FlickUpdateService.PrepareInstallationAsync(
            fixture.Download(fixture.Release("v1.0.4-preview.1")), cancelled.Token));
        await Assert.ThrowsAsync<InvalidDataException>(() => FlickUpdateService.PrepareInstallationAsync(
            new DownloadedUpdate(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Flick-Setup.exe"), fixture.Hash, fixture.Release("v1.0.4-preview.1")), CancellationToken.None));
    }

    private sealed class InstallerFixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(FlickUpdateService.DownloadDirectory, "test-" + Guid.NewGuid().ToString("N"));
        public string Path { get; }
        public string Hash { get; }
        public InstallerFixture()
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "Flick-Setup.exe");
            // The CI-compiled managed PE is unsigned. Never invoke this fixture.
            File.Copy(typeof(FlickUpdateService).Assembly.Location, Path);
            Hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path)));
        }
        public AvailableUpdate Release(string tag) => new(tag.TrimStart('v'),
            "https://github.com/Krish-Verma/Flick/releases/tag/" + tag,
            "https://api.github.com/repos/Krish-Verma/Flick/releases/assets/1",
            "https://api.github.com/repos/Krish-Verma/Flick/releases/assets/2",
            new FileInfo(Path).Length, tag);
        public DownloadedUpdate Download(AvailableUpdate release) => new(Path, Hash, release);
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
}
