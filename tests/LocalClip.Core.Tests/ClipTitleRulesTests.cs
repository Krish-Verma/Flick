using System.Globalization;
using System.Text.Json;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;

namespace LocalClip.Core.Tests;

public class ClipTitleRulesTests
{
    private static readonly DateTime SavedAt = new(2026, 10, 30, 9, 41, 0);
    private const string Legacy = "Flick_clip_20261030_164100_0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("Counter-Strike 2 (automatic)", "Counter-Strike 2 · Oct 30, 9:41 AM")]
    [InlineData("Counter-Strike 2 (cs2.exe)", "Counter-Strike 2 · Oct 30, 9:41 AM")]
    [InlineData("Counter-Strike 2 [cs2.exe]", "Counter-Strike 2 · Oct 30, 9:41 AM")]
    [InlineData("Display 1 (2560x1440)", "Oct 30, 9:41 AM")]
    [InlineData("Screen", "Oct 30, 9:41 AM")]
    [InlineData("", "Oct 30, 9:41 AM")]
    public void NewTitlesHaveSourceAndLocalTimeWithoutInternalIdentifiers(string source, string expected) =>
        Assert.Equal(expected, ClipTitleRules.Create(source, SavedAt, CultureInfo.GetCultureInfo("en-US"))
            .Replace('\u202f', ' ').Replace('\u00a0', ' '));

    [Fact]
    public void TimeUsesUsersTwentyFourHourPreference() =>
        Assert.EndsWith("09:41", ClipTitleRules.Create("Game", SavedAt, CultureInfo.GetCultureInfo("fr-FR")));

    [Fact]
    public void LegacyPresentationDoesNotRewritePersistedNameOrPath()
    {
        var clip = new ClipMetadata { Title = Legacy, OriginalFilePath = Path.Combine("Clips", Legacy + ".mkv"),
            TargetName = "My game", CreatedAt = SavedAt };
        Assert.StartsWith("My game · ", clip.DisplayTitle);
        Assert.DoesNotContain(Legacy, clip.DisplayTitle);
        var roundTrip = JsonSerializer.Deserialize<ClipMetadata>(JsonSerializer.Serialize(clip))!;
        Assert.Equal(Legacy, roundTrip.Title);
        Assert.Equal(clip.OriginalFilePath, roundTrip.OriginalFilePath);
        Assert.DoesNotContain("DisplayTitle", JsonSerializer.Serialize(clip));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ImportsAndCustomTitlesArePreserved(bool imported, bool sameOriginalName)
    {
        string path = Path.Combine("Clips", (sameOriginalName ? Legacy : "My recording") + ".mkv");
        Assert.Equal(Legacy, ClipTitleRules.ForDisplay(Legacy, path, "Game", SavedAt, imported));
        Assert.Equal("Last round clutch", ClipTitleRules.ForDisplay("Last round clutch", path, "Game", SavedAt, false));
    }

    [Fact]
    public void RenamingUpdatesDisplayWithoutChangingOriginal()
    {
        var clip = new ClipMetadata { Title = Legacy, OriginalFilePath = Legacy + ".mkv", CreatedAt = SavedAt };
        var notifications = new List<string?>();
        clip.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        clip.Title = "Overtime win";
        Assert.Equal("Overtime win", clip.DisplayTitle);
        Assert.Equal(Legacy + ".mkv", clip.OriginalFilePath);
        Assert.Contains(nameof(ClipMetadata.DisplayTitle), notifications);
    }
}
