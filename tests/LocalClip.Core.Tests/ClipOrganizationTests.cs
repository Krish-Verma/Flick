using System.Diagnostics;
using System.Text.Json;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using LocalClip.Core.Storage;
using Xunit;

namespace LocalClip.Core.Tests;

public sealed class ClipOrganizationTests
{
    [Fact]
    public void TagsNormalizeAndBoundWithoutLosingMeaning()
    {
        Assert.Equal(new[] { "Clutch", "team play" }, ClipOrganization.ParseTags(" Clutch,clutch; team play "));
        Assert.Throws<ArgumentException>(() => ClipOrganization.ParseTags(new string('a', 33)));
        Assert.Throws<ArgumentException>(() => ClipOrganization.NormalizeTags(Enumerable.Range(0, 13).Select(i => "tag" + i)));
        Assert.Throws<ArgumentException>(() => ClipOrganization.ParseTags("private\nnewline"));
    }

    [Fact]
    public void OfflineImportedMetadataAndMarkersRoundTripWithoutChangingOriginal()
    {
        using var files = new Fixture();
        byte[] original = [1, 2, 3, 4]; File.WriteAllBytes(files.Original, original);
        var storage = new ClipStorage(new AppSettings(), metadataDirectory: files.Metadata);
        var clip = new ClipMetadata { OriginalFilePath = files.Original, Title = "Round trip", DurationSeconds = 10,
            SourceKind = "Imported", ConnectedFolderId = "linked", IsFavorite = true,
            Tags = ["Clutch", "clutch"], GameName = "Test Game", Markers = [new(7, "Finish"), new(1, "Start")] };
        storage.AddOrUpdateClip(clip);
        Assert.Equal(original, File.ReadAllBytes(files.Original));
        File.Delete(files.Original); // Simulate the owner disconnecting external storage.
        var restored = Assert.Single(new ClipStorage(new AppSettings(), metadataDirectory: files.Metadata).GetAllClips());
        Assert.True(restored.IsFavorite); Assert.True(restored.IsImported); Assert.False(restored.IsAvailable);
        Assert.Equal("Test Game", restored.GameGroup); Assert.Equal("Clutch", Assert.Single(restored.Tags));
        Assert.Equal(new[] { 1d, 7d }, restored.Markers.Select(m => m.PositionSeconds));
    }

    [Fact]
    public void LegacyIndexDefaultsAndMalformedOrganizationPreserveRawBytes()
    {
        using var files = new Fixture();
        Directory.CreateDirectory(files.Metadata);
        string path = Path.Combine(files.Metadata, "clips_index.json");
        var legacy = new { Id = "legacy", OriginalFilePath = files.Original, Title = "Old", TargetName = "Old Game", DurationSeconds = 5 };
        string raw = JsonSerializer.Serialize(new[] { legacy }); File.WriteAllText(path, raw);
        var storage = new ClipStorage(new AppSettings(), metadataDirectory: files.Metadata);
        var clip = Assert.Single(storage.GetAllClips());
        Assert.False(clip.IsFavorite); Assert.Empty(clip.Tags); Assert.Empty(clip.Markers);
        Assert.Equal(ExportAspectPresets.Original, clip.ExportAspectPreset);
        Assert.Equal("Old Game", clip.GameGroup); Assert.Equal(raw, File.ReadAllText(path));
        string damaged = raw.Replace("\"Title\":\"Old\"", "\"Title\":\"Old\",\"Tags\":null");
        File.WriteAllText(path, damaged);
        var invalid = new ClipStorage(new AppSettings(), metadataDirectory: files.Metadata);
        Assert.Empty(invalid.GetAllClips()); Assert.NotEmpty(invalid.LastLoadWarning);
        Assert.Throws<InvalidDataException>(() => invalid.AddOrUpdateClip(clip));
        Assert.Equal(damaged, File.ReadAllText(path));
    }

    [Fact]
    public void SearchFilterAndStableSortUseNoFilesystemOrAvailabilityReads()
    {
        var clips = Enumerable.Range(0, 20_000).Select(i => new ClipMetadata {
            Id = i.ToString("D5"), OriginalFilePath = "invalid\0path", Title = "Clip " + i,
            GameName = i % 2 == 0 ? "Arena" : "Racing", Tags = [i % 3 == 0 ? "clutch" : "warmup"],
            IsFavorite = i % 5 == 0, CreatedAt = new DateTime(2026, 1, 1).AddSeconds(i)
        }).ToArray();
        var timer = Stopwatch.StartNew();
        var result = ClipLibraryQuery.Apply(clips, "ARENA #CLUTCH", favoritesOnly: true, sort: "Oldest");
        Assert.NotEmpty(result); Assert.All(result, c => Assert.True(c.IsFavorite && c.GameName == "Arena" && c.Tags.Contains("clutch")));
        Assert.Equal(result.OrderBy(c => c.CreatedAt).Select(c => c.Id), result.Select(c => c.Id));
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), "20k in-memory rows exceeded the loose non-performance-regression ceiling.");
    }

    [Fact]
    public void MarkersRejectOutOfRangeDuplicateAndUnboundedEntries()
    {
        Assert.Throws<ArgumentException>(() => ClipOrganization.ValidateMarkers([new(-1, "")], 10));
        Assert.Throws<ArgumentException>(() => ClipOrganization.ValidateMarkers([new(11, "")], 10));
        Assert.Throws<ArgumentException>(() => ClipOrganization.ValidateMarkers([new(1, ""), new(1, "duplicate")], 10));
        Assert.Throws<ArgumentException>(() => ClipOrganization.ValidateMarkers(Enumerable.Range(0, 33).Select(i => new ClipMarker(i, "")), 60));
    }

    [Fact]
    public void DateSourceAndGameFiltersComposeWithoutDroppingOfflineRows()
    {
        var recent = new ClipMetadata { Title = "Offline", OriginalFilePath = "missing", CreatedAt = new DateTime(2026, 10, 24),
            SourceKind = "Imported", GameName = "Arena", IsFavorite = true, Tags = ["Highlight"] };
        var old = new ClipMetadata { Title = "Old", CreatedAt = new DateTime(2020, 1, 1) };
        Assert.Same(recent, Assert.Single(ClipLibraryQuery.Apply([old, recent], "2026-10-24 #highlight", "Imported", true, "Arena", createdAfter: new DateTime(2026, 10, 20))));
        var marker = new ClipMarker(1, "saved");
        Assert.Same(marker, Assert.Single(ClipOrganization.ValidateMarkers([marker], 10))); // Other metadata saves retain live marker identity.
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "flick-organization-" + Guid.NewGuid().ToString("N"));
        public string Original => Path.Combine(_root, "source.mkv");
        public string Metadata => Path.Combine(_root, "Metadata");
        public Fixture() => Directory.CreateDirectory(_root);
        public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
    }
}
