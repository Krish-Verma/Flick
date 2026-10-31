using LocalClip.Core.Storage;
using Xunit;

namespace LocalClip.Core.Tests;

public sealed class LibraryDiscoveryLimitsTests
{
    [Fact]
    public void RecursiveDepthLimitReportsOmittedChildAndStillIncludesDepthEight()
    {
        using var owned = new OwnedFolder();
        string directory = owned.Path;
        string included = "";
        for (int depth = 1; depth <= ClipLibraryPaths.MaximumDirectoryDepth + 1; depth++)
        {
            directory = System.IO.Path.Combine(directory, "level");
            Directory.CreateDirectory(directory);
            if (depth == ClipLibraryPaths.MaximumDirectoryDepth)
                included = AddVideo(directory);
        }
        AddVideo(directory);

        var result = ClipLibraryPaths.DiscoverVideosWithLimits(owned.Path, true);

        Assert.Equal(included, Assert.Single(result.Videos));
        Assert.True(result.ScanLimitReached);
        Assert.Equal(VideoDiscoveryLimits.DirectoryDepth, result.ReachedLimits);
        Assert.Contains(ClipLibraryPaths.MaximumDirectoryDepth.ToString(), result.LimitDescription);
        Assert.Equal(result.Videos.ToArray(), ClipLibraryPaths.DiscoverVideos(owned.Path, true).ToArray());
    }

    [Fact]
    public void DirectoryBudgetReportsPartialScanBeforeTheFileBudget()
    {
        using var owned = new OwnedFolder();
        // The root consumes one slot, so one of these child directories must
        // remain unvisited. Every child has media; ordering cannot hide the gap.
        for (int index = 0; index < ClipLibraryPaths.MaximumDirectoriesPerScan; index++)
        {
            string child = System.IO.Path.Combine(owned.Path, index.ToString("D4"));
            Directory.CreateDirectory(child);
            AddVideo(child);
        }

        var result = ClipLibraryPaths.DiscoverVideosWithLimits(owned.Path, true);

        Assert.Equal(ClipLibraryPaths.MaximumDirectoriesPerScan - 1, result.Videos.Count);
        Assert.True(result.Videos.Count < ClipLibraryPaths.MaximumMediaFilesPerScan);
        Assert.True(result.ScanLimitReached);
        Assert.Equal(VideoDiscoveryLimits.DirectoryCount, result.ReachedLimits);
        Assert.Contains(ClipLibraryPaths.MaximumDirectoriesPerScan.ToString(), result.LimitDescription);
    }

    [Fact]
    public void NonrecursiveChoiceDoesNotReportAnUnrequestedTraversalAsTruncated()
    {
        using var owned = new OwnedFolder();
        string included = AddVideo(owned.Path);
        string child = System.IO.Path.Combine(owned.Path, "child");
        Directory.CreateDirectory(child);
        AddVideo(child);

        var result = ClipLibraryPaths.DiscoverVideosWithLimits(owned.Path, false);

        Assert.Equal(included, Assert.Single(result.Videos));
        Assert.False(result.ScanLimitReached);
        Assert.Equal(VideoDiscoveryLimits.None, result.ReachedLimits);
        Assert.Empty(result.LimitDescription);
    }

    private static string AddVideo(string directory)
    {
        string path = System.IO.Path.Combine(directory, "fixture.mp4");
        using (File.Create(path)) { }
        return ClipLibraryPaths.Normalize(path);
    }

    private sealed class OwnedFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "Flick-Discovery-Limits-" + Guid.NewGuid().ToString("N"));
        public OwnedFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
