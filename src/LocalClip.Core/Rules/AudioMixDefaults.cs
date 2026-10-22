namespace LocalClip.Core.Rules;

/// <summary>
/// Conservative defaults depend on whether Flick knows its track layout.
/// Imported media has no proven "combined" track; use only track 0 by default.
/// Flick originals mute their combined preview when dedicated tracks exist.
/// </summary>
public static class AudioMixDefaults
{
    public static bool ShouldMute(bool imported, int trackCount, int trackIndex) =>
        imported ? trackIndex != 0 : trackCount > 1 && trackIndex == 0;
}
