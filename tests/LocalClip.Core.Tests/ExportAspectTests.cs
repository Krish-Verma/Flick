using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using Xunit;

namespace LocalClip.Core.Tests;

public sealed class ExportAspectTests
{
    [Theory]
    [InlineData(1920,1080)] [InlineData(1080,1920)] [InlineData(641,359)] [InlineData(854,480)]
    public void FitPresetsAreBoundedEvenAndHaveExactAspect(int width, int height)
    {
        foreach (string preset in ExportAspectPresets.All)
        {
            var canvas = ExportAspectPresets.Canvas(preset, width, height);
            Assert.True(canvas.Width <= width && canvas.Height <= height);
            Assert.True(ExportAspectPresets.MatchesDimensions(preset, canvas.Width, canvas.Height));
            string filter = ExportAspectPresets.VideoFilter(preset, canvas.Width, canvas.Height);
            Assert.Contains("force_original_aspect_ratio=decrease", filter);
            if (preset != ExportAspectPresets.Original) Assert.Contains("pad=", filter);
            Assert.DoesNotContain("crop=", filter);
        }
    }

    [Fact]
    public void AspectChangesFingerprintAndNameButOrganizationDoesNot()
    {
        string path = Path.Combine(Path.GetTempPath(), "nonexistent-clip.mkv");
        var clip = new ClipMetadata { OriginalFilePath = path, AudioTracks = [] };
        string Fingerprint(string aspect) => ExportFingerprint.Compute(path, 0, 10, clip.AudioTracks, 20, aspect);
        string original = Fingerprint(ExportAspectPresets.Original);
        clip.Tags = ["favorite moment"]; clip.GameName = "Arena"; clip.IsFavorite = true;
        Assert.Equal(original, Fingerprint(ExportAspectPresets.Original));
        Assert.NotEqual(original, Fingerprint(ExportAspectPresets.Vertical9x16));
        Assert.NotEqual(Fingerprint(ExportAspectPresets.Square1x1), Fingerprint(ExportAspectPresets.Vertical9x16));
        Assert.NotEqual(ShareExportNaming.ForOriginal(path, 20), ShareExportNaming.ForOriginal(path, 20, ExportAspectPresets.Square1x1));
        Assert.Throws<ArgumentException>(() => Fingerprint("crop-unknown"));
        Assert.Throws<ArgumentException>(() => ExportAspectPresets.Canvas(ExportAspectPresets.Vertical9x16, 8, 8));
    }
}
