using System.Text.Json;
using LocalClip.Core.Ipc;

namespace LocalClip.Core.Tests;

public class CaptureHostHealthIpcTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StatusQueryNeverLaunchesAnIdleHelper(bool directCommand)
    {
        using var client = new CaptureHostClient(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"));

        if (directCommand)
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendCommandAsync("get_status"));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetStatusAsync());

        Assert.False(client.IsRunning);
        Assert.Null(client.HostProcessId);
        Assert.False(client.HasPendingReplaySaves);
    }

    [Fact]
    public void RejectedStatusPreservesFailureInsteadOfInventingOff()
    {
        const string reason = "Capture engine is unavailable.";
        var failure = Assert.Throws<InvalidOperationException>(() => CaptureHostStatus.FromResponse(
            new IpcResponse { Ok = false, Error = reason }));
        Assert.Equal(reason, failure.Message);
        Assert.Throws<InvalidOperationException>(() => CaptureHostStatus.FromResponse(new IpcResponse { Ok = false }));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"state\":null}")]
    [InlineData("{\"state\":\"UnknownFutureState\"}")]
    [InlineData("{\"state\":\"Armed\",\"buffered_sec\":-1}")]
    [InlineData("{\"state\":\"Armed\",\"buffered_sec\":1e500}")]
    [InlineData("{\"state\":\"Armed\",\"buffer_tracking_limited\":\"false\"}")]
    [InlineData("{\"state\":\"Armed\",\"private_bytes\":-10}")]
    [InlineData("{\"state\":\"Armed\",\"tracks\":[5]}")]
    public void MalformedStatusCannotBecomeHealthyOrOff(string json)
    {
        Assert.Throws<InvalidDataException>(() => CaptureHostStatus.FromResponse(
            new IpcResponse { Ok = true, Result = JsonSerializer.Deserialize<JsonElement>(json) }));
    }

    [Fact]
    public void NativeHealthFieldsAndActualProfileSurviveStatusParsing()
    {
        var result = JsonSerializer.SerializeToElement(new
        {
            state = "Faulted", buffered_sec = 4.5, max_sec = 30, is_armed = false,
            target_type = "window", target_valid = false, private_bytes = 12345L, working_set = 23456L,
            fault_reason = "capture_target_lost", buffered_duration_estimated = true,
            buffer_tracking_limited = true, tracks = new[] { "Combined Mix", "System Audio" },
            encoder = "obs_x264", encoder_name = "CPU (x264)", software_fallback = true,
            width = 1280, height = 720, fps = 30
        });

        var status = CaptureHostStatus.FromResponse(new IpcResponse { Ok = true, Result = result });

        Assert.Equal("Faulted", status.State);
        Assert.Equal("capture_target_lost", status.FaultReason);
        Assert.True(status.BufferedDurationEstimated);
        Assert.True(status.BufferTrackingLimited);
        Assert.Equal(4.5, status.BufferedSec);
        Assert.Equal(30, status.MaxSec);
        Assert.False(status.IsArmed);
        Assert.False(status.TargetValid);
        Assert.Equal("window", status.TargetType);
        Assert.Equal(12345, status.PrivateBytes);
        Assert.Equal(23456, status.WorkingSet);
        Assert.Equal(new[] { "Combined Mix", "System Audio" }, status.Tracks);
        Assert.Equal("obs_x264", status.Profile!.EncoderId);
        Assert.True(status.Profile.UsesSoftwareEncoding);
        Assert.Equal(720, status.Profile.Height);
    }

    [Fact]
    public void MissingLegacyDiagnosticsRemainConservative()
    {
        var status = CaptureHostStatus.FromResponse(new IpcResponse
        {
            Ok = true, Result = JsonSerializer.SerializeToElement(new { state = "Off" })
        });

        Assert.Equal("Off", status.State);
        Assert.True(status.BufferedDurationEstimated);
        Assert.False(status.TargetValid);
        Assert.Empty(status.FaultReason);
        Assert.Empty(status.Tracks);
        Assert.Null(status.Profile);
    }
}
