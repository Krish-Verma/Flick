using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LocalClip.Core.Models;
using LocalClip.Core.Storage;
using Xunit;

namespace LocalClip.Core.Tests;

public sealed class ConnectedFolderTests
{
    private sealed class Sandbox : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "FlickLibraryTests", Guid.NewGuid().ToString("N"));
        public string Media => Path.Combine(Root, "Existing Videos");
        public string Metadata => Path.Combine(Root, "Metadata");

        public Sandbox()
        {
            Directory.CreateDirectory(Media);
            Directory.CreateDirectory(Metadata);
        }

        public string Video(string name = "moment.MKV")
        {
            string path = Path.Combine(Media, name);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });
            return path;
        }

        public ClipStorage Storage() => new(AppSettings.CreateDefault(),
            metadataDirectory: Metadata);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch { /* Never harm files outside the dedicated test directory. */ }
        }
    }

    [Fact]
    public void ConnectedFolderPersistsAfterRestartWithNoVideoCopy()
    {
        using var env = new Sandbox();
        var original = env.Video();
        var folder = env.Storage().ConnectFolder(env.Media);
        var restarted = env.Storage();
        var entries = restarted.GetConnectedFolders();
        Assert.Single(entries);
        Assert.Equal(folder.Id, entries[0].Id);
        Assert.Equal(env.Media, entries[0].Path);
        Assert.True(File.Exists(original));
        Assert.Equal(5, new FileInfo(original).Length);
    }

    [Fact]
    public void OfflineSourceAndMetadataSurviveRestart()
    {
        using var env = new Sandbox();
        var file = env.Video();
        var db = env.Storage();
        var folder = db.ConnectFolder(env.Media);
        db.AddOrUpdateClip(new ClipMetadata
        {
            OriginalFilePath = file,
            SourceKind = "Imported",
            SourceLabel = "Existing Videos",
            ConnectedFolderId = folder.Id,
            Title = "Saved Moment",
            DurationSeconds = 29.9
        });

        var moved = Path.Combine(env.Root, "Disconnected");
        Directory.Move(env.Media, moved);
        var restarted = env.Storage();
        Assert.Single(restarted.GetAllClips());
        Assert.False(restarted.GetAllClips()[0].IsAvailable);
        Assert.Equal("Offline", restarted.GetAllClips()[0].LibraryStatus);
        Assert.Single(restarted.GetConnectedFolders());
        Assert.False(restarted.GetConnectedFolders()[0].IsAvailable);
    }

    [Fact]
    public async Task RemovingImportedClipNeverDeletesOriginalOrExportAndIgnoresRescan()
    {
        using var env = new Sandbox();
        var file = env.Video();
        var export = Path.Combine(env.Root, "share.mp4");
        File.WriteAllText(export, "user-created-export");
        var db = env.Storage();
        var folder = db.ConnectFolder(env.Media);
        var clip = new ClipMetadata
        {
            OriginalFilePath = file,
            SourceKind = "Imported",
            ConnectedFolderId = folder.Id,
            Title = "Imported Moment",
            ExportedFilePath = export
        };
        db.AddOrUpdateClip(clip);

        Assert.True(db.DeleteClip(clip.Id));
        Assert.True(File.Exists(file));
        Assert.True(File.Exists(export));
        Assert.Empty(db.GetAllClips());

        var restarted = env.Storage();
        var result = await restarted.ScanConnectedFolderAsync(folder.Id);
        Assert.Equal(1, result.TotalDiscovered);
        Assert.Equal(1, result.Ignored);
        Assert.Equal(0, result.Imported);
        Assert.True(File.Exists(file));
        Assert.True(File.Exists(export));
    }

    [Fact]
    public void UnlinkingFolderRemovesItsEntriesOnlyNeverDeletesVideos()
    {
        using var env = new Sandbox();
        var imported = env.Video();
        var flick = Path.Combine(env.Root, "own.mkv");
        File.WriteAllText(flick, "original");
        var db = env.Storage();
        var folder = db.ConnectFolder(env.Media);
        db.AddOrUpdateClip(new ClipMetadata
        {
            OriginalFilePath = imported, ConnectedFolderId = folder.Id,
            SourceKind = "Imported"
        });
        db.AddOrUpdateClip(new ClipMetadata { OriginalFilePath = flick });
        Assert.True(db.DisconnectFolder(folder.Id));
        Assert.Empty(db.GetConnectedFolders());
        Assert.Single(db.GetAllClips());
        Assert.Equal(flick, db.GetAllClips()[0].OriginalFilePath);
        Assert.True(File.Exists(imported));
        Assert.True(File.Exists(flick));
    }

    [Fact]
    public void BoundedRecursiveDiscoveryFindsSubfoldersButNoOtherFiles()
    {
        using var env = new Sandbox();
        env.Video("first.mp4");
        var inner = Path.Combine(env.Media, "Game A");
        Directory.CreateDirectory(inner);
        File.WriteAllBytes(Path.Combine(inner, "last.mkv"), new byte[] { 1 });
        File.WriteAllText(Path.Combine(inner, "notes.txt"), "not a video");
        var top = ClipLibraryPaths.DiscoverVideos(env.Media, includeSubfolders: false);
        var all = ClipLibraryPaths.DiscoverVideos(env.Media, includeSubfolders: true);
        Assert.Single(top);
        Assert.Equal(2, all.Count);
        Assert.All(all, x => Assert.True(ClipLibraryPaths.IsSupportedVideo(x)));
    }

    [Fact]
    public void ReconnectIsIdempotentAndFolderRootIsNotAccepted()
    {
        using var env = new Sandbox();
        var db = env.Storage();
        var a = db.ConnectFolder(env.Media);
        var b = db.ConnectFolder(env.Media + Path.DirectorySeparatorChar);
        Assert.Equal(a.Id, b.Id);
        Assert.Single(db.GetConnectedFolders());
        Assert.Throws<ArgumentException>(() =>
            ClipLibraryPaths.ValidateImportRoot(Path.GetPathRoot(env.Media)!));
    }

    [Fact]
    public void InvalidAndRelativePathsAreRejectedEarly()
    {
        Assert.False(ClipLibraryPaths.IsSupportedVideo("readme.txt"));
        Assert.True(ClipLibraryPaths.IsSupportedVideo("capture.MP4"));
        Assert.True(ClipLibraryPaths.IsSupportedVideo("capture.mkv"));
        Assert.Throws<ArgumentException>(() => ClipLibraryPaths.Normalize("relative/folder"));
    }

    [Fact]
    public void CancellingFolderDiscoveryDoesNotRemoveAnyFile()
    {
        using var env = new Sandbox();
        var video = env.Video();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            ClipLibraryPaths.DiscoverVideos(env.Media, true, cts.Token));
        Assert.True(File.Exists(video));
    }
}
