using System.Text.Json;
using System.Text.Json.Nodes;
using LocalClip.Core.Models;
using LocalClip.Core.Storage;

namespace LocalClip.Core.Tests;

public class LibraryIndexRecoveryTests
{
    [Theory]
    [InlineData("Title")]
    [InlineData("TargetName")]
    [InlineData("AudioTracks")]
    [InlineData("SourceKind")]
    [InlineData("null-track")]
    [InlineData("null-row")]
    public void NullMetadataCannotReachSearchOrAudioEditingOrOverwriteOriginalIndex(string fault)
    {
        using var fixture = new IndexFixture();
        var good = JsonSerializer.SerializeToNode(fixture.Clip)!.AsObject();
        var bad = good.DeepClone().AsObject();
        bad["Id"] = "damaged";
        bad["OriginalFilePath"] = Path.Combine(fixture.Directory, "second.mkv");
        if (fault == "null-track") bad["AudioTracks"] = new JsonArray((JsonNode?)null);
        else if (fault != "null-row") bad[fault] = null;
        var rows = new JsonArray(good, fault == "null-row" ? null : bad);
        string raw = rows.ToJsonString();
        File.WriteAllText(fixture.Index, raw);
        var storage = new ClipStorage(AppSettings.CreateDefault(), metadataDirectory: fixture.Directory);
        Assert.Empty(storage.GetAllClips()); // Do not present a partial index as a complete library.
        Assert.Contains("preserved", storage.LastLoadWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidDataException>(() => storage.AddOrUpdateClip(fixture.Clip));
        Assert.Equal(raw, File.ReadAllText(fixture.Index));
        Assert.True(File.Exists(fixture.Clip.OriginalFilePath));
    }

    [Fact]
    public void JsonNullIndexIsPreservedAndCannotBeReplacedByIncidentalWrites()
    {
        using var fixture = new IndexFixture();
        File.WriteAllText(fixture.Index, "null");
        var storage = new ClipStorage(AppSettings.CreateDefault(), metadataDirectory: fixture.Directory);
        Assert.Empty(storage.GetAllClips());
        Assert.Contains("preserved", storage.LastLoadWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidDataException>(() => storage.AddOrUpdateClip(fixture.Clip));
        Assert.Equal("null", File.ReadAllText(fixture.Index));
    }

    [Fact]
    public void ValidOfflineEntriesAndCaseInsensitiveImportedOwnershipRemainIntact()
    {
        using var fixture = new IndexFixture();
        fixture.Clip.SourceKind = "imported";
        File.Delete(fixture.Clip.OriginalFilePath);
        File.WriteAllText(fixture.Index, JsonSerializer.Serialize(new[] { fixture.Clip }));
        var storage = new ClipStorage(AppSettings.CreateDefault(), metadataDirectory: fixture.Directory);
        var restored = Assert.Single(storage.GetAllClips());
        Assert.True(restored.IsImported);
        Assert.False(restored.IsAvailable);
        Assert.Equal("", storage.LastLoadWarning);
    }

    private sealed class IndexFixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "flick-index-recovery-" + Guid.NewGuid().ToString("N"));
        public string Index => Path.Combine(Directory, "clips_index.json");
        public ClipMetadata Clip { get; }
        public IndexFixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            string path = Path.Combine(Directory, "original.mkv");
            File.WriteAllText(path, "original bytes are never modified");
            Clip = new ClipMetadata { OriginalFilePath = path, Title = "Original", TargetName = "Monitor" };
        }
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
}
