using System.Text.Json;
using LocalClip.Core.Models;

namespace LocalClip.Core.Audio;

/// <summary>A frozen process instance. A PID alone is never a durable identity.</summary>
public sealed record AudioProcessIdentity(uint ProcessId, ulong CreationTime, string ExecutablePath, string DisplayName)
{
    public bool IsComplete => ProcessId > 0 && CreationTime > 0 &&
        !string.IsNullOrWhiteSpace(ExecutablePath) && ExecutablePath.Length <= 32768;
    public static AudioProcessIdentity FromWindow(TargetWindow window) =>
        new(window.Pid, window.ProcessCreationFileTime, window.ExecutablePath, window.DisplayName);
    public string DisplayLabel => $"{DisplayName} · app process tree";
}

public sealed record AudioSourceStatus(string Kind, string State, uint ProcessId, bool SignalObserved);
public sealed record CaptureAudioStatus(string RequestedMode, string ActualMode, IReadOnlyList<AudioSourceStatus> Sources)
{
    public string Summary => Sources.Count == 0 ? "Audio off" : string.Join(" · ", Sources.Select(source =>
        source.Kind is "Game" or "Voice"
            ? $"{source.Kind}: {source.State.ToLowerInvariant()} · {(source.SignalObserved ? "sound observed" : "no sound observed yet")}"
            : $"{source.Kind}: {source.State.ToLowerInvariant()}"));
    public static CaptureAudioStatus? Parse(JsonElement data)
    {
        if (!data.TryGetProperty("audio_mode_actual", out var actual) || actual.ValueKind != JsonValueKind.String) return null;
        if (actual.GetString() is not ("Separated" or "SystemFallback" or "NoSystemAudio")) throw new InvalidDataException("Unknown audio routing status.");
        var sources = new List<AudioSourceStatus>();
        if (data.TryGetProperty("audio_sources", out var values))
        {
            if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 4) throw new InvalidDataException("Invalid audio sources.");
            foreach (var item in values.EnumerateArray())
            {
                string kind = item.GetProperty("kind").GetString() ?? "";
                if (kind is not ("Game" or "Voice" or "System" or "Microphone") || sources.Any(source => source.Kind == kind)) throw new InvalidDataException("Invalid audio source kind.");
                string state = item.GetProperty("state").GetString() ?? "";
                if (state is not ("Attached" or "Lost" or "Device loopback" or "Input device")) throw new InvalidDataException("Invalid audio source state.");
                sources.Add(new(kind, state, item.TryGetProperty("process_id", out var pid) ? pid.GetUInt32() : 0,
                    item.TryGetProperty("signal_observed", out var signal) && signal.GetBoolean()));
            }
        }
        return new(data.TryGetProperty("audio_mode_requested", out var requested) ? requested.GetString() ?? "" : "", actual.GetString()!, sources.AsReadOnly());
    }
}

public static class AudioCapturePolicy
{
    public static bool IsSeparated(AppSettings settings) => string.Equals(settings.AudioMode, "Separated", StringComparison.OrdinalIgnoreCase);
    public static void Validate(AppSettings settings)
    {
        if (!IsSeparated(settings) && !string.Equals(settings.AudioMode, "SystemFallback", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose System Audio or Separate App Audio in Settings.");
        if (!IsSeparated(settings)) return;
        if (settings.GameAudioSource == null && settings.VoiceAudioSource == null)
            throw new ArgumentException("Select at least one Game or Voice app audio source in Settings.");
        foreach (var source in new[] { settings.GameAudioSource, settings.VoiceAudioSource })
            if (source != null && !source.IsComplete) throw new ArgumentException("Refresh and select an available app audio source.");
        if (settings.GameAudioSource != null && settings.VoiceAudioSource?.ProcessId == settings.GameAudioSource.ProcessId)
            throw new ArgumentException("Game and Voice must use different app process trees.");
    }
}
