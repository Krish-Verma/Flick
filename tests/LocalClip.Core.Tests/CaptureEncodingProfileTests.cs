using System.Text.Json;
using LocalClip.Core.Ipc;
using LocalClip.Core.Models;

namespace LocalClip.Core.Tests;

public class CaptureEncodingProfileTests
{
    [Theory]
    [InlineData("jim_nvenc", "NVIDIA NVENC", false, 1920, 1080, 60)]
    [InlineData("h264_texture_amf", "AMD AMF", false, 1920, 1080, 30)]
    [InlineData("obs_qsv11_v2", "Intel Quick Sync", false, 1280, 720, 30)]
    [InlineData("obs_x264", "CPU (x264)", true, 1280, 720, 30)]
    public void ConfiguredEventAndStartReplyShareTheActualProfile(
        string encoder, string name, bool software, int width, int height, int fps)
    {
        var data = JsonSerializer.SerializeToElement(new
        {
            encoder, encoder_name = name, software_fallback = software, width, height, fps
        });
        Assert.True(CaptureEncodingProfile.TryParse(data, out var configured));
        var reply = CaptureStartResult.FromResponse(new IpcResponse { Ok = true, Result = data });
        Assert.True(reply.Success);
        Assert.Equal(configured, reply.Profile);
        Assert.Equal(encoder, configured!.EncoderId);
        Assert.Equal(name, configured.EncoderName);
        Assert.Equal(software, configured.IsSoftwareFallback);
        Assert.Contains($"{width}×{height}", configured.DisplayText);
        Assert.Contains($"{fps} FPS", configured.DisplayText);
        Assert.Equal(software, configured.SoftwareWarning.Length > 0);
    }

    [Fact]
    public void CpuDisplayNeverReportsRequestedSmoothHardwareProfile()
    {
        var settings = AppSettings.CreateDefault();
        RecordingProfiles.TryApply(RecordingProfiles.Smooth, settings);
        var actual = new CaptureEncodingProfile("obs_x264", "CPU (x264)", true, 1280, 720, 30);
        Assert.Equal(1080, settings.VideoHeight);
        Assert.Contains("1280×720", actual.DisplayText);
        Assert.Contains("30 FPS", actual.DisplayText);
        Assert.DoesNotContain("1080", actual.DisplayText);
        Assert.Contains("May reduce game performance", actual.SoftwareWarning);
        Assert.Contains("720p / 30 FPS maximum", actual.SoftwareWarning);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"encoder\":\"obs_x264\",\"encoder_name\":\"CPU\",\"software_fallback\":true,\"width\":0,\"height\":720,\"fps\":30}")]
    [InlineData("{\"encoder\":\"obs_x264\",\"encoder_name\":\"CPU\",\"software_fallback\":\"true\",\"width\":1280,\"height\":720,\"fps\":30}")]
    [InlineData("{\"encoder\":\"obs_x264\",\"encoder_name\":\"CPU\",\"software_fallback\":true,\"width\":1280,\"height\":720,\"fps\":-1}")]
    public void MissingOrInvalidNegotiatedFieldsDoNotInventAnEncoder(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.False(CaptureEncodingProfile.TryParse(document.RootElement, out var profile));
        Assert.Null(profile);
    }

    [Fact]
    public void NativeStartupRefusalKeepsItsActionableErrorAndDoesNotClaimAProfile()
    {
        const string reason = "No compatible hardware encoder initialized; CPU fallback is disabled.";
        var reply = CaptureStartResult.FromResponse(new IpcResponse { Ok = false, Error = reason });
        Assert.False(reply.Success);
        Assert.Equal(reason, reply.Error);
        Assert.Null(reply.Profile);
        var legacy = CaptureStartResult.FromResponse(new IpcResponse { Ok = true });
        Assert.True(legacy.Success);
        Assert.Null(legacy.Profile);
    }
}
