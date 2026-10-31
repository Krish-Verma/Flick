using System.Text.Json;
using LocalClip.Core.Models;
using LocalClip.Core.Storage;

namespace LocalClip.Core.Tests;

public sealed class ClipStorageTransactionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ImportedRemovalOrUnlinkRollsBackBothFilesAndMemoryOnWriteFailure(bool unlink, bool failIndex)
    {
        string root = Path.Combine(Path.GetTempPath(), "Flick-Metadata-Transaction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "source.mkv"), export = Path.Combine(root, "user-export.mp4");
            byte[] media = [1, 2, 3, 4], exported = [5, 6, 7];
            File.WriteAllBytes(source, media); File.WriteAllBytes(export, exported);
            string metadata = Path.Combine(root, "Metadata");
            var settings = new AppSettings();
            var storage = new ClipStorage(settings, metadataDirectory: metadata);
            var folder = storage.ConnectFolder(root);
            var clip = new ClipMetadata { OriginalFilePath = source, SourceKind = "Imported",
                ConnectedFolderId = folder.Id, ExportedFilePath = export };
            storage.AddOrUpdateClip(clip);
            string index = Path.Combine(metadata, "clips_index.json"), sources = Path.Combine(metadata, "connected_sources.json");
            byte[] beforeIndex = File.ReadAllBytes(index), beforeSources = File.ReadAllBytes(sources);
            string blocked = (failIndex ? index : sources) + ".tmp";
            Directory.CreateDirectory(blocked); // Only the selected atomic-write destination is blocked.

            Exception? failure = Record.Exception(() =>
            {
                if (unlink) storage.DisconnectFolder(folder.Id);
                else storage.DeleteClip(clip.Id);
            });
            Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString() ?? "Expected owned disk failure");
            Assert.Same(clip, Assert.Single(storage.GetAllClips()));
            Assert.Equal(folder.Id, Assert.Single(storage.GetConnectedFolders()).Id);
            Assert.Equal(beforeIndex, File.ReadAllBytes(index));
            Assert.Equal(beforeSources, File.ReadAllBytes(sources));
            var restarted = new ClipStorage(settings, metadataDirectory: metadata);
            Assert.Equal(clip.Id, Assert.Single(restarted.GetAllClips()).Id);
            Assert.Equal(folder.Id, Assert.Single(restarted.GetConnectedFolders()).Id);
            using (var state = JsonDocument.Parse(File.ReadAllText(sources)))
                Assert.Empty(state.RootElement.GetProperty("IgnoredImportedPaths").EnumerateArray());
            Assert.Equal(media, File.ReadAllBytes(source));
            Assert.Equal(exported, File.ReadAllBytes(export));

            Directory.Delete(blocked);
            Assert.True(unlink ? storage.DisconnectFolder(folder.Id) : storage.DeleteClip(clip.Id));
            Assert.Empty(storage.GetAllClips());
            restarted = new ClipStorage(settings, metadataDirectory: metadata);
            Assert.Empty(restarted.GetAllClips());
            if (unlink) Assert.Empty(restarted.GetConnectedFolders());
            else Assert.Equal(folder.Id, Assert.Single(restarted.GetConnectedFolders()).Id);
            Assert.Equal(media, File.ReadAllBytes(source));
            Assert.Equal(exported, File.ReadAllBytes(export));
        }
        finally { Directory.Delete(root, true); }
    }
}
