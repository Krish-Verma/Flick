using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LocalClip.App.Views;
using LocalClip.Core.Models;
using Xunit;

namespace LocalClip.App.Tests;

// Called from the existing STA test after its one Application and theme resources
// exist. Never invokes Save, startup registration, or the real settings profile.
internal static class ResourceUsageSettingsAssertions
{
    public static void Run()
    {
        var settings = AppSettings.CreateDefault();
        settings.ShowResourceUsage = false;
        var page = new SettingsWindow(settings, () => false);
        var toggle = Assert.IsType<CheckBox>(page.FindName("ChkShowResourceUsage"));
        Assert.Equal("Show RAM and CPU", toggle.Content);
        Assert.Equal("Show RAM and CPU", AutomationProperties.GetName(toggle));
        Assert.False(toggle.IsChecked);
        toggle.IsChecked = true;
        Assert.False(settings.ShowResourceUsage);

        var staged = AppSettings.CreateDefault();
        staged.ShowResourceUsage = false;
        Invoke(page, "StageDisplay", staged);
        Assert.True(staged.ShowResourceUsage);
        Assert.False(settings.ShowResourceUsage);
        bool canceled = false;
        page.CloseRequested += saved => { Assert.False(saved); canceled = true; };
        Invoke(page, "Cancel_Click", page, new RoutedEventArgs());
        Assert.True(canceled);
        Assert.False(settings.ShowResourceUsage);

        // Restore Defaults uses Populate with a new default snapshot. Exercise
        // that same real path without opening the confirmation dialog.
        var restoredPage = new SettingsWindow(settings, () => false);
        var restoredToggle = Assert.IsType<CheckBox>(restoredPage.FindName("ChkShowResourceUsage"));
        Assert.False(restoredToggle.IsChecked);
        Invoke(restoredPage, "Populate", AppSettings.CreateDefault());
        Assert.True(restoredToggle.IsChecked);
        Assert.False(settings.ShowResourceUsage);
        staged.ShowResourceUsage = false;
        Invoke(restoredPage, "StageDisplay", staged);
        Assert.True(staged.ShowResourceUsage);
        Assert.False(settings.ShowResourceUsage);
        Invoke(restoredPage, "Cancel_Click", restoredPage, new RoutedEventArgs());
        Assert.False(settings.ShowResourceUsage);
    }

    private static void Invoke(SettingsWindow page, string name, params object[] parameters) =>
        typeof(SettingsWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, parameters);
}
