using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using LocalClip.App;
using LocalClip.App.Services;
using LocalClip.App.Views;
using LocalClip.Core.Models;
using Xunit;

namespace LocalClip.App.Tests;

public class SettingsStagingTests
{
    [Fact]
    public void ActualWpfSettingsCancelDoesNotCommitShortcutOrSoundChanges()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                // Testhost owns Application.ResourceAssembly. Load the actual
                // production dictionaries by assembly-qualified pack URI.
                foreach (string dictionary in new[] { "Colors", "Metrics", "Typography", "Icons", "Controls", "Chrome" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary {
                        Source = new Uri($"/Flick;component/Resources/Theme/Flick.{dictionary}.xaml", UriKind.Relative)
                    });
                var settings = AppSettings.CreateDefault();
                var page = new SettingsWindow(settings, () => false);
                T Find<T>(string name) => (T)page.FindName(name);
                void Invoke(string method, object sender) => typeof(SettingsWindow)
                    .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(page, new[] { sender, new RoutedEventArgs() });
                Assert.Equal(Visibility.Collapsed, Find<Button>("BtnInstallUpdate").Visibility);
                Assert.True(Find<Button>("BtnCheckUpdates").IsEnabled);
                Assert.True(Find<CheckBox>("ChkPreviewUpdates").IsChecked);
                Assert.True(Find<CheckBox>("ChkSoftwareFallback").IsChecked);
                Assert.True(settings.AllowSoftwareFallback);
                // Exercise the actual disclosure template: its keyboard-focusable
                // toggle must update the Expander and reveal/hide its content.
                var disclosure = Find<Expander>("WebhookExpander");
                disclosure.ApplyTemplate();
                var header = Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(
                    disclosure.Template.FindName("HeaderSite", disclosure));
                var disclosureContent = Assert.IsType<ContentPresenter>(
                    disclosure.Template.FindName("ExpandSite", disclosure));
                header.IsChecked = true;
                page.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Assert.True(disclosure.IsExpanded);
                Assert.Equal(Visibility.Visible, disclosureContent.Visibility);
                header.IsChecked = false;
                page.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Assert.False(disclosure.IsExpanded);
                Assert.Equal(Visibility.Collapsed, disclosureContent.Visibility);
                var toggle = Find<TextBox>("TxtToggleHotkey");
                var save = Find<TextBox>("TxtSaveHotkey");
                Assert.True(toggle.IsReadOnly);
                Assert.True(save.IsReadOnly);
                bool editing = false;
                page.ShortcutEditingChanged += value => editing = value;
                Invoke("ChangeShortcut_Click", new Button { Tag = "Toggle" });
                Assert.True(editing);
                Assert.Equal(Visibility.Visible, Find<StackPanel>("ShortcutEditor").Visibility);
                Invoke("Save_Click", page); // Incomplete shortcut must not write settings/startup.
                Assert.True(editing);
                Assert.Equal(Visibility.Visible, Find<StackPanel>("ShortcutEditor").Visibility);
                Invoke("CancelShortcut_Click", page);
                Assert.False(editing);
                Assert.Equal("Ctrl+Shift+F9", toggle.Text);
                Assert.Equal("Ctrl+Shift+F9", settings.CaptureToggleHotkey);
                Find<ComboBox>("CmbNotificationTone").SelectedIndex = 1;
                Find<CheckBox>("ChkSoftwareFallback").IsChecked = false;
                bool closed = false;
                page.CloseRequested += saved => { Assert.False(saved); closed = true; };
                Invoke("Cancel_Click", page);
                Assert.True(closed);
                Assert.Equal("Chime", settings.ClipSavedSoundTone);
                Assert.True(settings.AllowSoftwareFallback);
                // No overlay/sound requested, even with a missing configured file.
                using var notification = new FlickNotificationService(page.Dispatcher);
                notification.NotifySaved(30, false, false, "Custom", @"Z:\missing.wav");
                Assert.False(FlickDialog.IsOpen);
                app.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF staging test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
