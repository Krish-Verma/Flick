namespace LocalClip.Core.Rules;

/// <summary>Pure Windows virtual-key policy; never installs a keyboard hook.</summary>
public static class ShortcutChordRules
{
    public static bool IsAllowed(uint modifiers, uint key)
    {
        // Ctrl or Alt is required; Windows chords remain owned by the OS.
        if ((modifiers & 3) == 0 || (modifiers & ~7u) != 0) return false;
        if (key == 0 || key > 254 || key is 16 or 17 or 18 or 91 or 92 or 123) return false;
        if ((modifiers & 1) != 0 && key is 9 or 27 or 32 or 115) return false;
        if ((modifiers & 2) != 0 && key == 27) return false;
        if ((modifiers & 3) == 3 && key == 46) return false;
        return true;
    }
}
