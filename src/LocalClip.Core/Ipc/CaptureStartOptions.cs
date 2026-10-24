using System.Text.Json;
using LocalClip.Core.Models;

namespace LocalClip.Core.Ipc;

/// <summary>Transient source consent, never persisted to application settings or diagnostic journals.</summary>
public sealed record CaptureWindowIdentity(ulong Hwnd, uint ProcessId, string Title, string Executable);

public sealed record CaptureStartOptions(bool EnableSystemAudio = true,
    CaptureWindowIdentity? ExpectedWindow = null, string? ExpectedMonitorId = null)
{
    internal bool RequiresCapabilities => !EnableSystemAudio || ExpectedWindow != null || ExpectedMonitorId != null;

    internal void Validate(AppSettings settings)
    {
        if (ExpectedWindow != null && ExpectedMonitorId != null)
            throw new ArgumentException("A recording request must identify one source.");
        if (ExpectedWindow is { } window && (!string.Equals(settings.TargetType, "Window", StringComparison.OrdinalIgnoreCase) ||
            window.Hwnd == 0 || window.Hwnd != settings.WindowHwnd || window.ProcessId == 0 ||
            string.IsNullOrWhiteSpace(window.Title) || window.Title.Length > 32768 ||
            string.IsNullOrWhiteSpace(window.Executable) || window.Executable.Length > 4096))
            throw new ArgumentException("The consented window identity is incomplete or changed.");
        if (ExpectedMonitorId != null && (!string.Equals(settings.TargetType, "Monitor", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(ExpectedMonitorId) || ExpectedMonitorId.Length > 1024))
            throw new ArgumentException("The consented display identity is incomplete or changed.");
    }
}

public sealed record CaptureHostCapabilities(int ProtocolVersion, bool DiagnosticNoSystemAudio, bool SourceIdentityBinding)
{
    public bool SupportsSafeDiagnostics => ProtocolVersion >= 2 && DiagnosticNoSystemAudio && SourceIdentityBinding;

    public static CaptureHostCapabilities FromResponse(IpcResponse response)
    {
        if (!response.Ok || response.Result is not JsonElement value || value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("protocol_version", out var version) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out int number) || number < 2 ||
            !value.TryGetProperty("diagnostic_no_system_audio", out var audio) || audio.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !value.TryGetProperty("source_identity_binding", out var identity) || identity.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidOperationException("This capture helper does not support the safe recording test. Update Flick and try again.");
        return new(number, audio.GetBoolean(), identity.GetBoolean());
    }
}
