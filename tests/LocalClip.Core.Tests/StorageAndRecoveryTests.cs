using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LocalClip.Core.Models;
using LocalClip.Core.Storage;
using Xunit;

namespace LocalClip.Core.Tests;

public class StorageAndRecoveryTests : IDisposable
{
    private readonly string _testDir;

    public StorageAndRecoveryTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "LocalClip_StorageTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void TestAppSettingsDefaultsAndStartArmedStrictlyFalse()
    {
        var settings = AppSettings.CreateDefault();
        Assert.False(settings.StartArmed);
        Assert.Equal(30, settings.ReplayDurationSeconds);
        Assert.Equal(20, settings.DiscordTargetSizeMb);
        Assert.Equal("Ctrl+Shift+F9", settings.CaptureToggleHotkey);
        Assert.Equal("Ctrl+Shift+F8", settings.SaveReplayHotkey);
    }

    [Fact]
    public void TestAppSettingsCorruptedFileRecovery()
    {
        string settingsPath = AppSettings.GetSettingsFilePath();
        string backupPath = settingsPath + ".bak";

        try
        {
            if (File.Exists(settingsPath))
            {
                File.Copy(settingsPath, backupPath, true);
            }

            // Write completely corrupted garbage to settings file
            File.WriteAllText(settingsPath, "{ this is invalid json !!! }}}");

            // AppSettings.Load() should recover without throwing and return clean default
            var loaded = AppSettings.Load();
            Assert.NotNull(loaded);
            Assert.False(loaded.StartArmed);
            Assert.Equal(30, loaded.ReplayDurationSeconds);
        }
        finally
        {
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, settingsPath, true);
                File.Delete(backupPath);
            }
        }
    }

    [Fact]
    public async Task TestClipStorageUnicodeAndSpecialNames()
    {
        var settings = new AppSettings
        {
            StorageRoot = _testDir,
            OriginalsFolder = Path.Combine(_testDir, "Originals"),
            ExportsFolder = Path.Combine(_testDir, "Exports"),
            TempFolder = Path.Combine(_testDir, "Temp")
        };
        Directory.CreateDirectory(settings.OriginalsFolder);

        // Create dummy MKV file with Unicode characters, spaces, and brackets
        string unicodeFileName = "Clip 🎮 日本語 [Test - #1] (2026).mkv";
        string filePath = Path.Combine(settings.OriginalsFolder, unicodeFileName);
        File.WriteAllText(filePath, "dummy mkv content for testing storage indexing");

        var storage = new ClipStorage(settings);

        var meta = new ClipMetadata
        {
            OriginalFilePath = filePath,
            Title = Path.GetFileNameWithoutExtension(filePath),
            CreatedAt = DateTime.UtcNow,
            DurationSeconds = 30.0,
            FileSizeBytes = new FileInfo(filePath).Length,
            Width = 1920,
            Height = 1080,
            Fps = 30,
            TargetName = "Unicode Monitor (1)",
            TrimStartSeconds = 0.0,
            TrimEndSeconds = 30.0
        };

        storage.AddOrUpdateClip(meta);

        // Verify retrieval
        var retrieved = storage.GetClipById(meta.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("Clip 🎮 日本語 [Test - #1] (2026)", retrieved.Title);
        Assert.Equal(filePath, retrieved.OriginalFilePath);

        // Test rename
        bool renameOk = storage.RenameClip(meta.Id, "Renamed Clip ⚡");
        Assert.True(renameOk);
        var renamed = storage.GetClipById(meta.Id);
        Assert.NotNull(renamed);
        Assert.Equal("Renamed Clip ⚡", renamed.Title);

        // Test delete without recycle bin
        bool deleteOk = storage.DeleteClip(meta.Id, moveToRecycleBin: false);
        Assert.True(deleteOk);
        Assert.Null(storage.GetClipById(meta.Id));
        Assert.False(File.Exists(filePath));
    }
}
