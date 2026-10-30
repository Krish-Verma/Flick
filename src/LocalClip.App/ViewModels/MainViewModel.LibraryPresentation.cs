using LocalClip.Core.Rules;

namespace LocalClip.App.ViewModels;

public partial class MainViewModel
{
    private LibraryBrowsePresentation LibraryPresentation => LibraryBrowsePresentation.Create(_allClips.Count,
        _filteredClips.Count, _searchQuery, _clipSourceFilter, FavoritesOnly, GameFilter, LibraryPeriod, LibrarySort);
    public bool LibraryHasClips => _allClips.Count > 0;
    public bool LibraryIsEmpty => _filteredClips.Count == 0;
    public bool LibraryHasSearch => !string.IsNullOrWhiteSpace(_searchQuery);
    public bool LibraryHasFilters => ActiveLibraryFilterCount > 0;
    public int ActiveLibraryFilterCount => LibraryPresentation.FilterCount;
    public string LibraryFilterLabel => LibraryPresentation.FilterLabel;
    public string LibraryEmptyTitle => LibraryPresentation.EmptyTitle;
    public string LibraryEmptyHint => LibraryPresentation.EmptyHint;

    public void ClearLibrarySearch() => SearchQuery = "";

    /// <summary>One in-memory result refresh. Does not change media/index/preferences or discard a search.</summary>
    public void ClearLibraryFilters()
    {
        _clipSourceFilter = "All Clips";
        _favoritesOnly = false;
        _gameFilter = "All Games";
        _libraryPeriod = "Any time";
        // This sort also restricts results to exports; ordinary sort preference is kept.
        if (_librarySort == "Recently Exported") _librarySort = "Newest";
        foreach (string name in new[] { nameof(ClipSourceFilter), nameof(FavoritesOnly), nameof(GameFilter), nameof(LibraryPeriod), nameof(LibrarySort) })
            OnPropertyChanged(name);
        ApplyFilter();
    }

    private void PublishLibraryPresentation()
    {
        foreach (string name in new[] { nameof(LibraryHasClips), nameof(LibraryIsEmpty), nameof(LibraryHasSearch),
                     nameof(LibraryHasFilters), nameof(ActiveLibraryFilterCount), nameof(LibraryFilterLabel),
                     nameof(LibraryEmptyTitle), nameof(LibraryEmptyHint) })
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}
