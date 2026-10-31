using System.Text.Json;
using System.Reflection;
using LocalClip.Core.Ipc;
using LocalClip.Core.Models;
using LocalClip.Testing;

namespace LocalClip.Core.Tests;

public sealed class CaptureStartupIpcTests
{
    private static readonly CaptureStartOptions Monitor = new(ExpectedMonitorId: "fixture-monitor-id");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NormalMonitorAfterDiscoveryShutdownVerifiesFreshHelperThenStartsWithUnchangedConsent(bool diagnosticNoAudio)
    {
        using var fixture = new CaptureHostPipeFixture(diagnosticNoAudio: diagnosticNoAudio);
        using var client = fixture.CreateClient();
        var settings = Settings(fixture.Root);
        string before = JsonSerializer.Serialize(settings);
        Assert.False(client.HasResponded);
        var targets = await client.GetTargetsAsync().WaitAsync(TimeSpan.FromSeconds(12));
        Assert.Equal(Monitor.ExpectedMonitorId, Assert.Single(targets.Monitors).Device);
        await client.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(8));
        Assert.False(client.IsRunning);
        Assert.False(client.HasResponded);

        var capabilities = await client.GetCapabilitiesAsync().WaitAsync(TimeSpan.FromSeconds(12));
        Assert.True(capabilities.SourceIdentityBinding);
        Assert.Equal(diagnosticNoAudio, capabilities.DiagnosticNoSystemAudio);
        Assert.True(client.HasResponded);
        Assert.True(await client.StartCaptureAsync(settings, Monitor).WaitAsync(TimeSpan.FromSeconds(12)));
        Assert.NotNull(client.CurrentProfile);
        var status = await client.GetStatusAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Buffering", status.State);
        Assert.False(status.IsArmed); // An accepted command is not retained-packet readiness.
        var requests = fixture.Requests();
        Assert.Equal(new[] { "enum_targets", "shutdown", "get_capabilities", "start_capture", "get_status" },
            requests.Select(request => request.GetProperty("cmd").GetString()).ToArray());
        var parameters = requests.Single(request => request.GetProperty("cmd").GetString() == "start_capture").GetProperty("params");
        Assert.Equal(JsonSerializer.Serialize(CaptureHostClient.CreateStartParameters(settings, Monitor)), parameters.GetRawText());
        Assert.True(parameters.GetProperty("enable_system_audio").GetBoolean());
        Assert.False(parameters.GetProperty("enable_mic").GetBoolean());
        Assert.False(parameters.TryGetProperty("game_audio", out _));
        Assert.False(parameters.TryGetProperty("voice_audio", out _));
        Assert.Equal("auto", parameters.GetProperty("encoder").GetString());
        Assert.Equal(before, JsonSerializer.Serialize(settings));
        await client.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task IdentityCapabilityFailureCannotSendCaptureOrFallBackToMonitorIndex()
    {
        using var fixture = new CaptureHostPipeFixture(sourceIdentity: false);
        using var client = fixture.CreateClient();
        await client.GetCapabilitiesAsync().WaitAsync(TimeSpan.FromSeconds(12));
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => client.StartCaptureAsync(Settings(fixture.Root), Monitor));
        Assert.Contains("exact source", failure.Message);
        Assert.Equal("get_capabilities", Assert.Single(fixture.Requests()).GetProperty("cmd").GetString());
        Assert.False(client.IsRunning);
        Assert.Null(client.CurrentProfile);
    }

    [Fact]
    public async Task VerifiedSourceSupportDoesNotAuthorizeANoSystemAudioDiagnostic()
    {
        using var fixture = new CaptureHostPipeFixture(diagnosticNoAudio: false);
        using var client = fixture.CreateClient();
        await client.GetCapabilitiesAsync().WaitAsync(TimeSpan.FromSeconds(12));
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.StartCaptureAsync(Settings(fixture.Root), new(false, ExpectedMonitorId: Monitor.ExpectedMonitorId)));
        Assert.Contains("consented recording test", failure.Message);
        Assert.Equal("get_capabilities", Assert.Single(fixture.Requests()).GetProperty("cmd").GetString());
        Assert.False(client.IsRunning);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnverifiedAdvancedStartCannotLaunchAFreshHelper(bool diagnostic)
    {
        using var fixture = new CaptureHostPipeFixture();
        using var client = fixture.CreateClient();
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.StartCaptureAsync(Settings(fixture.Root),
            diagnostic ? new(false) : Monitor));
        Assert.Empty(fixture.Requests());
        Assert.False(client.IsRunning);
        Assert.False(client.HasResponded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HelperExitOrReplacementBetweenVerificationAndStartCannotReuseOldCapabilities(bool replace)
    {
        using var fixture = new CaptureHostPipeFixture();
        using var client = fixture.CreateClient();
        await client.GetCapabilitiesAsync().WaitAsync(TimeSpan.FromSeconds(12));
        int? verified = client.HostProcessId;
        await client.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(8));
        if (replace)
        {
            client.Start();
            Assert.True(client.IsRunning);
            Assert.NotEqual(verified, client.HostProcessId);
            Assert.False(client.HasResponded);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.StartCaptureAsync(Settings(fixture.Root), Monitor));
        Assert.Equal(new[] { "get_capabilities", "shutdown" },
            fixture.Requests().Select(request => request.GetProperty("cmd").GetString()).ToArray());
        Assert.False(client.IsRunning);
        Assert.False(client.HasResponded);
    }

    [Fact]
    public async Task ExitAfterOuterVerificationButBeforeGuardedSendCannotAutoStartAReplacement()
    {
        using var fixture = new CaptureHostPipeFixture();
        using var client = fixture.CreateClient();
        await client.GetCapabilitiesAsync().WaitAsync(TimeSpan.FromSeconds(12));
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var verified = Assert.IsType<System.Diagnostics.Process>(typeof(CaptureHostClient).GetField("_process", hidden)!.GetValue(client));
        verified.Kill(); // This fixture owns only this metadata helper.
        await verified.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        // Enter the actual guarded-send operation with the process already
        // selected by the outer check: deterministic OS-exit race coverage.
        var send = (Task<IpcResponse>)typeof(CaptureHostClient).GetMethod("SendCommandCoreAsync", hidden)!.Invoke(client,
            ["start_capture", CaptureHostClient.CreateStartParameters(Settings(fixture.Root), Monitor), 60000, verified])!;
        var failure = await Assert.ThrowsAsync<IOException>(() => send);
        Assert.Contains("verified capture helper", failure.Message);
        Assert.Equal("get_capabilities", Assert.Single(fixture.Requests()).GetProperty("cmd").GetString());
        Assert.False(client.IsRunning);
        Assert.False(client.HasResponded);
    }

    [Fact]
    public async Task NormalExactWindowAlsoUsesSourceBindingWithoutRequiringTheDiagnosticAudioFeature()
    {
        using var fixture = new CaptureHostPipeFixture(diagnosticNoAudio: false);
        using var client = fixture.CreateClient();
        var settings = Settings(fixture.Root);
        settings.TargetType = "Window"; settings.WindowHwnd = 42;
        var options = new CaptureStartOptions(ExpectedWindow: new(42, 23, "Fixture window", "fixture.exe",
            @"C:\Synthetic\fixture.exe", 123456));
        var capabilities = await client.GetCapabilitiesAsync().WaitAsync(TimeSpan.FromSeconds(12));
        Assert.True(capabilities.ExactWindowBinding);
        Assert.True(await client.StartCaptureAsync(settings, options).WaitAsync(TimeSpan.FromSeconds(12)));
        var request = fixture.Requests()[1].GetProperty("params");
        Assert.Equal(42ul, request.GetProperty("window_hwnd").GetUInt64());
        Assert.Equal(23u, request.GetProperty("expected_window_pid").GetUInt32());
        Assert.Equal(123456ul, request.GetProperty("expected_window_creation_time").GetUInt64());
        Assert.Equal(@"C:\Synthetic\fixture.exe", request.GetProperty("expected_window_path").GetString());
        Assert.True(request.GetProperty("enable_system_audio").GetBoolean());
        await client.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task PreverifiedDiagnosticRetainsItsExplicitNoSystemAudioRequest()
    {
        using var fixture = new CaptureHostPipeFixture();
        using var client = fixture.CreateClient();
        var options = new CaptureStartOptions(false, ExpectedMonitorId: Monitor.ExpectedMonitorId);
        await client.GetCapabilitiesAsync().WaitAsync(TimeSpan.FromSeconds(12));
        Assert.True(await client.StartCaptureAsync(Settings(fixture.Root), options).WaitAsync(TimeSpan.FromSeconds(12)));
        var request = fixture.Requests()[1].GetProperty("params");
        Assert.False(request.GetProperty("enable_system_audio").GetBoolean());
        Assert.False(request.GetProperty("enable_mic").GetBoolean());
        await client.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task NativeRefusalRetainsExactExplanationAndDoesNotClaimAProfile()
    {
        const string reason = "Fixture encoder refused: no compatible encoder initialized.";
        using var fixture = new CaptureHostPipeFixture(refusal: reason);
        using var client = fixture.CreateClient();
        await client.GetCapabilitiesAsync().WaitAsync(TimeSpan.FromSeconds(12));
        Assert.False(await client.StartCaptureAsync(Settings(fixture.Root), Monitor).WaitAsync(TimeSpan.FromSeconds(12)));
        Assert.Equal(reason, client.LastStartError);
        Assert.Null(client.CurrentProfile);
        Assert.True(client.HasResponded); // Refusal is response evidence, not recording success.
        await client.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(8));
    }

    internal static AppSettings Settings(string root) => new()
    {
        TargetType = "Monitor", MonitorIndex = 7, MonitorDevice = "fixture-monitor-id",
        OriginalsFolder = Path.Combine(root, "Originals"), ExportsFolder = Path.Combine(root, "Exports"),
        TempFolder = Path.Combine(root, "Temp")
    };
}
