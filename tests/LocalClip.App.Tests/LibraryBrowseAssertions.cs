using System.Reflection;
using System.Text.Json;
using LocalClip.App.ViewModels;
using LocalClip.Core.Models;
using Xunit;

namespace LocalClip.App.Tests;

/// <summary>Runs in the existing isolated WPF fixture; exercises actual VM transitions without storage/media calls.</summary>
internal static class LibraryBrowseAssertions
{
    public static void Run(MainViewModel vm)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo Field(string name) => typeof(MainViewModel).GetField(name, hidden)!;
        string[] fields = ["_allClips", "_searchQuery", "_clipSourceFilter", "_favoritesOnly", "_gameFilter", "_libraryPeriod", "_librarySort"];
        var before = fields.ToDictionary(name => name, name => Field(name).GetValue(vm));
        var selection = vm.SelectedClip;
        string preferences = JsonSerializer.Serialize(vm.Settings);
        try
        {
            var clips = new List<ClipMetadata>
            {
                new() { Id = "one", Title = "Arena finish", GameName = "Arena", IsFavorite = true,
                    OriginalFilePath = "unavailable-original-one.mkv", CreatedAt = DateTime.Now },
                new() { Id = "two", Title = "Other round", GameName = "Other", SourceKind = "Imported",
                    OriginalFilePath = "unavailable-original-two.mkv", CreatedAt = DateTime.Now }
            };
            Field("_allClips").SetValue(vm, clips);
            vm.ClearLibraryFilters();
            vm.SearchQuery = "missing title";
            Assert.True(vm.LibraryIsEmpty);
            Assert.True(vm.LibraryHasClips);
            Assert.Equal("No matching clips", vm.LibraryEmptyTitle);
            vm.FavoritesOnly = true;
            vm.GameFilter = "Arena";
            vm.LibraryPeriod = "7 days";
            vm.ClipSourceFilter = "Flick";
            vm.LibrarySort = "Recently Exported";
            Assert.Equal(5, vm.ActiveLibraryFilterCount);
            vm.ClearLibraryFilters();
            Assert.Equal("missing title", vm.SearchQuery);
            Assert.Equal("Newest", vm.LibrarySort);
            Assert.False(vm.LibraryHasFilters);
            Assert.True(vm.LibraryIsEmpty);
            vm.ClearLibrarySearch();
            Assert.False(vm.LibraryHasSearch);
            Assert.Equal(2, vm.FilteredClips.Count);
            Assert.False(vm.LibraryIsEmpty);
            Assert.Same(selection, vm.SelectedClip);
            Assert.Equal(preferences, JsonSerializer.Serialize(vm.Settings));
            Assert.Equal("unavailable-original-one.mkv", clips[0].OriginalFilePath);
            Field("_allClips").SetValue(vm, new List<ClipMetadata>());
            vm.ApplyFilter();
            Assert.Equal("No clips yet", vm.LibraryEmptyTitle);
            Assert.False(vm.LibraryHasClips);
        }
        finally
        {
            foreach (var (name, value) in before) Field(name).SetValue(vm, value);
            vm.ApplyFilter();
        }
    }
}
