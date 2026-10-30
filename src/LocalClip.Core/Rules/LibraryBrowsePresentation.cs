namespace LocalClip.Core.Rules;

/// <summary>Explains browsing results without touching availability, source media or the library index.</summary>
public sealed record LibraryBrowsePresentation(string EmptyTitle, string EmptyHint, int FilterCount)
{
    public string FilterLabel => FilterCount == 0 ? "Filters" : $"Filters ({FilterCount})";

    public static LibraryBrowsePresentation Create(int totalClips, int matchingClips, string? search,
        string source, bool favoritesOnly, string game, string period, string sort = "Newest")
    {
        int filters = (source is "Flick" or "Imported" ? 1 : 0) + (favoritesOnly ? 1 : 0) +
                      (game != "All Games" ? 1 : 0) + (period != "Any time" ? 1 : 0) +
                      (sort == "Recently Exported" ? 1 : 0);
        if (totalClips == 0)
            return new("No clips yet", "Choose a capture source and start replay, or import a folder of existing clips.", filters);
        if (matchingClips == 0)
            return new("No matching clips", !string.IsNullOrWhiteSpace(search)
                ? "Try another search, clear the search, or reset your filters."
                : "Reset your filters to see your other clips.", filters);
        return new("", "", filters);
    }
}
