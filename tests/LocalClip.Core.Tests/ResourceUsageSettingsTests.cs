using System.Text.Json;
using LocalClip.Core.Models;

namespace LocalClip.Core.Tests;

public sealed class ResourceUsageSettingsTests
{
    [Fact]
    public void NewAndDefaultSettingsShowResourceUsage()
    {
        Assert.True(new AppSettings().ShowResourceUsage);
        Assert.True(AppSettings.CreateDefault().ShowResourceUsage);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void ExistingJsonWithoutDisplayPreferenceDefaultsOnWithoutRewriting(int schema)
    {
        using var fixture = new SettingsFixture();
        var settings = AppSettings.CreateDefault();
        settings.SchemaVersion = schema;
        var document = JsonSerializer.SerializeToNode(settings)!.AsObject();
        Assert.True(document.Remove(nameof(AppSettings.ShowResourceUsage)));
        string original = document.ToJsonString();
        File.WriteAllText(fixture.Path, original);

        var loaded = AppSettings.LoadExistingFile(fixture.Path);

        Assert.True(loaded.ShowResourceUsage);
        Assert.Equal(original, File.ReadAllText(fixture.Path));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SavedDisplayPreferenceSurvivesSerializationAndIsolatedReload(bool enabled)
    {
        using var fixture = new SettingsFixture();
        var settings = AppSettings.CreateDefault();
        settings.ShowResourceUsage = enabled;
        settings.SaveToFile(fixture.Path);

        using var document = JsonDocument.Parse(File.ReadAllText(fixture.Path));
        Assert.Equal(enabled, document.RootElement.GetProperty(nameof(AppSettings.ShowResourceUsage)).GetBoolean());
        var loaded = AppSettings.LoadExistingFile(fixture.Path);
        Assert.Equal(enabled, loaded.ShowResourceUsage);
        Assert.False(loaded.RequiresExplicitSettingsSave);
    }

    private sealed class SettingsFixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "flick-resource-settings-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(_directory, "settings.json");
        public SettingsFixture() => Directory.CreateDirectory(_directory);
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
