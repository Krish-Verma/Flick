using System.Text.Json;
using LocalClip.Core.Ipc;
using LocalClip.Core.Models;

namespace LocalClip.Core.Tests;

public class GpuCompatibilitySettingsTests
{
    [Fact]
    public void NewSettingsPreferHardwareAutomaticallyWithBoundedCpuCompatibility()
    {
        var settings = AppSettings.CreateDefault();
        Assert.Equal("auto", settings.VideoEncoder);
        Assert.True(settings.AllowSoftwareFallback);
        Assert.False(settings.EnableMicrophone);
        Assert.False(settings.StartArmed);
    }

    [Theory]
    [InlineData(2, "obs_nvenc_h264_tex", "auto", true)]
    [InlineData(2, "jim_nvenc", "jim_nvenc", false)]
    [InlineData(2, "h264_texture_amf", "h264_texture_amf", false)]
    [InlineData(3, "obs_nvenc_h264_tex", "obs_nvenc_h264_tex", false)]
    public void LegacyDefaultMigrationPreservesExplicitEncoderAndCpuOptOut(
        int schema, string originalEncoder, string expectedEncoder, bool needsReview)
    {
        var original = AppSettings.CreateDefault();
        original.SchemaVersion = schema;
        original.VideoEncoder = originalEncoder;
        original.AllowSoftwareFallback = false;
        var raw = JsonSerializer.Serialize(original);
        var path = Path.Combine(Path.GetTempPath(), "flick-gpu-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, raw);
            var loaded = AppSettings.LoadExistingFile(path);
            Assert.Equal(expectedEncoder, loaded.VideoEncoder);
            Assert.False(loaded.AllowSoftwareFallback);
            Assert.Equal(3, loaded.SchemaVersion);
            Assert.Equal(needsReview, loaded.RequiresExplicitSettingsSave);
            Assert.Equal(raw, File.ReadAllText(path));
            if (needsReview)
            {
                loaded.MonitorIndex = 4;
                loaded.SaveToFile(path);
                Assert.Equal(raw, File.ReadAllText(path));
                loaded.MarkExplicitSettingsSaveCompleted();
                loaded.SaveToFile(path);
                var saved = AppSettings.LoadExistingFile(path);
                Assert.Equal("auto", saved.VideoEncoder);
                Assert.False(saved.AllowSoftwareFallback);
                Assert.False(saved.RequiresExplicitSettingsSave);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LegacyDefaultWithoutSchemaIsStillMigratedOnlyInMemory()
    {
        var settings = AppSettings.CreateDefault();
        var node = JsonSerializer.SerializeToNode(settings)!.AsObject();
        node.Remove("SchemaVersion");
        node.Remove("AllowSoftwareFallback");
        node["VideoEncoder"] = "obs_nvenc_h264_tex";
        var raw = node.ToJsonString();
        var path = Path.Combine(Path.GetTempPath(), "flick-gpu-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, raw);
            var loaded = AppSettings.LoadExistingFile(path);
            Assert.Equal("auto", loaded.VideoEncoder);
            Assert.True(loaded.AllowSoftwareFallback);
            Assert.True(loaded.RequiresExplicitSettingsSave);
            Assert.Equal(raw, File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("jim_nvenc")]
    [InlineData("h264_texture_amf")]
    [InlineData("obs_qsv11_v2")]
    [InlineData("obs_x264")]
    public void EveryQualityPresetPreservesEncoderPolicyAndCpuOptOut(string encoder)
    {
        var settings = AppSettings.CreateDefault();
        settings.VideoEncoder = encoder;
        settings.AllowSoftwareFallback = false;
        foreach (var preset in new[] { RecordingProfiles.Light, RecordingProfiles.Balanced, RecordingProfiles.Smooth })
        {
            Assert.True(RecordingProfiles.TryApply(preset, settings));
            Assert.Equal(encoder, settings.VideoEncoder);
            Assert.False(settings.AllowSoftwareFallback);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StartRequestCarriesHardwareAutoModeAndCpuPermission(bool allowed)
    {
        var settings = AppSettings.CreateDefault();
        settings.AllowSoftwareFallback = allowed;
        var request = JsonSerializer.SerializeToElement(CaptureHostClient.CreateStartParameters(settings));
        Assert.Equal("auto", request.GetProperty("encoder").GetString());
        Assert.Equal(allowed, request.GetProperty("allow_software_fallback").GetBoolean());
        Assert.False(request.GetProperty("enable_mic").GetBoolean());
        Assert.Equal(settings.VideoFps, request.GetProperty("fps").GetInt32());
        Assert.Equal(settings.VideoWidth, request.GetProperty("width").GetInt32());
        Assert.Equal(settings.VideoHeight, request.GetProperty("height").GetInt32());
    }
}
