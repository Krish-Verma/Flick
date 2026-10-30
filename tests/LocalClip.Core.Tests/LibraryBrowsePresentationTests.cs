using System.Diagnostics;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using Xunit.Abstractions;

namespace LocalClip.Core.Tests;

public class LibraryBrowsePresentationTests(ITestOutputHelper output)
{
    [Fact]
    public void EmptyLibraryAndFilteredResultsExplainDifferentRecoveryActions()
    {
        var firstRun = LibraryBrowsePresentation.Create(0, 0, "missing", "All Clips", false, "All Games", "Any time");
        Assert.Equal("No clips yet", firstRun.EmptyTitle);
        Assert.Contains("import a folder", firstRun.EmptyHint);
        var noMatch = LibraryBrowsePresentation.Create(50, 0, "missing", "Imported", true, "Arena", "7 days");
        Assert.Equal("No matching clips", noMatch.EmptyTitle);
        Assert.Contains("clear the search", noMatch.EmptyHint);
        Assert.Equal(4, noMatch.FilterCount);
        Assert.Equal("Filters (4)", noMatch.FilterLabel);
        var results = LibraryBrowsePresentation.Create(50, 12, "#clutch", "All Clips", false, "All Games", "Any time");
        Assert.Empty(results.EmptyTitle);
        Assert.Equal("Filters", results.FilterLabel);
        Assert.Equal(0, results.FilterCount); // Search has its own visible clear action.
        Assert.Equal(1, LibraryBrowsePresentation.Create(50, 0, "", "All Clips", false, "All Games", "Any time", "Recently Exported").FilterCount);
    }

    [Fact]
    public void ExistingQueryProducesRepeatableResultsAtEachPlannedLibrarySizeAndReportsRawTimings()
    {
        // Measurement fixture, not a gameplay, UI-scroll or universal latency claim.
        foreach (int count in new[] { 0, 50, 1000, 5000 })
        {
            var clips = Enumerable.Range(0, count).Select(i => new ClipMetadata
            {
                Id = $"clip-{i:D5}", Title = $"Round {i:D5}",
                GameName = i % 2 == 0 ? "Arena" : "Other game",
                Tags = i % 5 == 0 ? ["Clutch"] : ["Practice"],
                CreatedAt = new DateTime(2026, 10, 30).AddSeconds(-i),
                OriginalFilePath = $@"Z:\never-open-for-query\clip-{i:D5}.mkv"
            }).ToArray();
            ClipLibraryQuery.Apply(clips, "#clutch", game: "Arena", sort: "Title"); // JIT/warmup, excluded.
            var timings = new List<double>();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            IReadOnlyList<ClipMetadata>? result = null;
            for (int repeat = 0; repeat < 25; repeat++)
            {
                var timer = Stopwatch.StartNew();
                result = ClipLibraryQuery.Apply(clips, "#clutch", game: "Arena", sort: "Title");
                timer.Stop(); timings.Add(timer.Elapsed.TotalMilliseconds);
            }
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Assert.NotNull(result);
            Assert.Equal((count + 9) / 10, result.Count);
            Assert.All(result, clip => Assert.Equal("Arena", clip.GameName));
            Assert.Equal(result.OrderBy(clip => clip.Title, StringComparer.OrdinalIgnoreCase).Select(clip => clip.Id), result.Select(clip => clip.Id));
            timings.Sort();
            output.WriteLine($"Existing ClipLibraryQuery: rows={count}; repetitions=25; median_ms={timings[12]:F3}; p95_ms={timings[23]:F3}; total_thread_allocated_bytes={allocated}; raw_ms={string.Join(',', timings.Select(value => value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}");
        }
    }
}
