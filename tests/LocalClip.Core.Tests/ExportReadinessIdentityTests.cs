using LocalClip.Core.Models;
using LocalClip.Core.Rules;

namespace LocalClip.Core.Tests;

public class ExportReadinessIdentityTests
{
    [Fact]
    public void BrowsingMetadataAndPosterChangesDoNotInvalidateReadinessButRealEditsDo()
    {
        var clip = new ClipMetadata { OriginalFilePath = "invalid\0path", DurationSeconds = 10, TrimEndSeconds = 10,
            AudioTracks = [new AudioTrackInfo { StreamIndex = 0, TrackIndex = 0, Gain = 1 }] };
        string original = ExportReadinessIdentity.Capture(clip);
        clip.IsFavorite = true; clip.Title = "Renamed"; clip.GameName = "Other game";
        clip.Tags = ["clutch"]; clip.Markers = [new ClipMarker(1, "Marker")]; clip.ThumbnailPath = "new still";
        clip.AudioTracks[0].Name = "Renamed role label";
        Assert.Equal(original, ExportReadinessIdentity.Capture(clip));
        clip.TrimStartSeconds = 1;
        Assert.NotEqual(original, ExportReadinessIdentity.Capture(clip));
        clip.TrimStartSeconds = 0;
        clip.AudioTracks[0].Gain = .5;
        Assert.NotEqual(original, ExportReadinessIdentity.Capture(clip));
        clip.AudioTracks[0].Gain = 1;
        clip.ExportAspectPreset = "Square1x1";
        Assert.NotEqual(original, ExportReadinessIdentity.Capture(clip));
        clip.ExportAspectPreset = "Original";
        clip.OriginalFilePath = "replacement\0path";
        Assert.NotEqual(original, ExportReadinessIdentity.Capture(clip));
    }
}
