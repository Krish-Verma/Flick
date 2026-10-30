using System.Globalization;
using System.Text.RegularExpressions;

namespace LocalClip.Core.Rules;

/// <summary>Readable library names; original paths and save identities stay immutable.</summary>
public static partial class ClipTitleRules
{
    [GeneratedRegex(@"^Flick_clip_\d{8}_\d{6}_[a-fA-F0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyGeneratedName();

    [GeneratedRegex(@"\s+[\[(][^\[\]()]+\.exe[\])]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExecutableSuffix();

    public static string Create(string? source, DateTime createdAt, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        string stamp = createdAt.ToString("MMM d", culture) + ", " + createdAt.ToString("t", culture);
        string label = (source ?? "").Trim();
        const string automatic = " (automatic)";
        if (label.EndsWith(automatic, StringComparison.OrdinalIgnoreCase)) label = label[..^automatic.Length].TrimEnd();
        label = ExecutableSuffix().Replace(label, "");
        bool generic = label.Length == 0 || label.Equals("Screen", StringComparison.OrdinalIgnoreCase) ||
            label.Equals("Monitor", StringComparison.OrdinalIgnoreCase) || label.Equals("Display", StringComparison.OrdinalIgnoreCase) ||
            label.StartsWith("Screen ", StringComparison.OrdinalIgnoreCase) ||
            label.StartsWith("Monitor ", StringComparison.OrdinalIgnoreCase) || label.StartsWith("Display ", StringComparison.OrdinalIgnoreCase);
        if (generic) return stamp;
        label = string.Concat(label.Where(c => !char.IsControl(c)));
        if (label.Length > 64) label = label[..61].TrimEnd() + "…";
        return label.Length == 0 ? stamp : label + " · " + stamp;
    }

    public static string ForDisplay(string title, string path, string? source, DateTime createdAt, bool imported)
    {
        if (imported || !LegacyGeneratedName().IsMatch(title)) return title;
        // Never rewrite arbitrary imports/custom titles or rename any file.
        return string.Equals(Path.GetFileNameWithoutExtension(path), title, StringComparison.Ordinal)
            ? Create(source, createdAt) : title;
    }
}
