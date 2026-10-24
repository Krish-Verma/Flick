using LocalClip.Core.Models;
namespace LocalClip.Core.Audio;

public enum AudioTrackRole { Unknown, Combined, System, Game, Voice, Microphone, Silence }
public static class AudioTrackRoles
{
    // Only the correlated native save contract assigns roles. Imported tags
    // and stream counts never prove capture isolation.
    public static AudioTrackRole FromNativeName(string name) => name switch
    {
        "Combined Audio" => AudioTrackRole.Combined,
        "System Audio (whole device)" or "System Audio" => AudioTrackRole.System,
        "Game Audio (process tree)" => AudioTrackRole.Game,
        "Voice Audio (process tree)" => AudioTrackRole.Voice,
        "Microphone" => AudioTrackRole.Microphone,
        "Silent Diagnostic Audio" => AudioTrackRole.Silence,
        _ => AudioTrackRole.Unknown
    };
    public static IReadOnlyList<AudioTrackInfo> EffectiveMix(IEnumerable<AudioTrackInfo>? tracks)
    {
        var selected = tracks?.Where(track => !track.IsMuted && track.Gain > .001).ToArray() ?? [];
        if (selected.Select(track => track.TrackIndex).Distinct().Count() != selected.Length || selected.Any(track => track.TrackIndex < 0))
            throw new InvalidDataException("Audio stream selection is invalid.");
        // Combined is an alternate complete mix. If selected, it owns the mix;
        // its constituents must never be added a second time.
        var combined = selected.FirstOrDefault(track => track.Role == AudioTrackRole.Combined);
        return combined != null ? [combined] : selected;
    }
    public static void Reset(ClipMetadata clip)
    {
        bool knownStems = clip.AudioTracks.Any(track => track.Role is AudioTrackRole.System or AudioTrackRole.Game or AudioTrackRole.Voice or AudioTrackRole.Microphone);
        foreach (var track in clip.AudioTracks)
        {
            track.Gain = 1;
            track.IsMuted = track.Role == AudioTrackRole.Silence ||
                (track.Role == AudioTrackRole.Combined && knownStems) ||
                (track.Role == AudioTrackRole.Unknown && (clip.IsImported ? track.TrackIndex != 0 : clip.AudioTracks.Count > 1 && track.TrackIndex == 0));
        }
    }
}
