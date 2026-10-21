using System;
using System.IO;
using LocalClip.Core.Models;
using Xunit;

namespace LocalClip.Core.Tests;

public sealed class SettingsRecoveryTests
{
    [Fact]
    public void DamagedSettingsArePreservedInsteadOfSilentlyOverwritten()
    {
        string path = Path.Combine(Path.GetTempPath(), "flick-settings-" + Guid.NewGuid().ToString("N") + ".json");
        const string damaged = "{NOT_VALID_JSON";
        try
        {
            File.WriteAllText(path, damaged);
            var loaded = AppSettings.LoadExistingFile(path);
            Assert.False(loaded.StartArmed);
            Assert.False(loaded.EnableMicrophone);
            Assert.Equal(30, loaded.ReplayDurationSeconds);
            Assert.Equal(20, loaded.DiscordTargetSizeMb);
            Assert.Contains("NOT overwritten", loaded.LoadWarning);
            Assert.Equal(damaged, File.ReadAllText(path));
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void OutOfRangePersistedValuesCannotCrashCaptureHeader()
    {
        string path = Path.Combine(Path.GetTempPath(), "flick-settings-" + Guid.NewGuid().ToString("N") + ".json");
        const string raw = "{\"SchemaVersion\":1,\"ReplayDurationSeconds\":0,\"DiscordTargetSizeMb\":999,\"EnableMicrophone\":true}";
        try
        {
            File.WriteAllText(path, raw);
            var loaded = AppSettings.LoadExistingFile(path);
            Assert.Equal(30, loaded.ReplayDurationSeconds);
            Assert.Equal(20, loaded.DiscordTargetSizeMb);
            Assert.False(loaded.EnableMicrophone);
            Assert.False(loaded.StartArmed);
            Assert.True(Path.IsPathFullyQualified(loaded.OriginalsFolder));
            Assert.True(Path.IsPathFullyQualified(loaded.ExportsFolder));
            Assert.NotEmpty(loaded.LoadWarning);
            Assert.Equal(raw, File.ReadAllText(path)); // Recovery is in-memory only.
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void ValidSettingsSurviveIsolatedReloadWithNoUnrequestedWrite()
    {
        string path = Path.Combine(Path.GetTempPath(), "flick-settings-" + Guid.NewGuid().ToString("N") + ".json");
        const string raw = "{\"SchemaVersion\":2,\"ReplayDurationSeconds\":60,\"DiscordTargetSizeMb\":10,\"EnableMicrophone\":true,\"OriginalsFolder\":\"C:\\\\Videos\\\\Originals\",\"ExportsFolder\":\"C:\\\\Videos\\\\Exports\"}";
        try
        {
            File.WriteAllText(path, raw);
            var loaded = AppSettings.LoadExistingFile(path);
            Assert.Equal(60, loaded.ReplayDurationSeconds);
            Assert.Equal(10, loaded.DiscordTargetSizeMb);
            Assert.True(loaded.EnableMicrophone);
            Assert.Equal("", loaded.LoadWarning);
            Assert.Equal(raw, File.ReadAllText(path));
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
