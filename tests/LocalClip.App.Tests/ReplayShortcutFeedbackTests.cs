using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using LocalClip.App;
using LocalClip.App.ViewModels;
using Xunit;

namespace LocalClip.App.Tests;

public class ReplayShortcutFeedbackTests
{
    [Fact]
    public void ReplaySaveIsDisabledUntilReadyAndOffHotkeyReportsWhy()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Application? app = null;
            MainViewModel? vm = null;
            try
            {
                app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (string dictionary in new[] { "Colors", "Metrics", "Typography", "Icons", "Controls", "Chrome" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/Flick;component/Resources/Theme/Flick.{dictionary}.xaml", UriKind.Relative)
                    });

                // The window is never shown or loaded; no capture host is started.
                var window = new MainWindow();
                vm = Assert.IsType<MainViewModel>(window.DataContext);
                vm.Settings.ShowClipSavedOverlay = false; // No topmost toast on the CI desktop.
                var saveButton = Assert.IsType<Button>(window.FindName("BtnSaveReplay"));
                var update = typeof(MainWindow).GetMethod("UpdateStatusBadgeColor",
                    BindingFlags.NonPublic | BindingFlags.Instance)!;

                vm.CaptureStatus = "Buffering";
                update.Invoke(window, null);
                Assert.False(saveButton.IsEnabled);

                vm.CaptureStatus = "Armed";
                update.Invoke(window, null);
                Assert.True(saveButton.IsEnabled);

                vm.CaptureStatus = "Off";
                update.Invoke(window, null);
                Assert.False(saveButton.IsEnabled);

                string? reported = null;
                vm.ReplaySaveFailed += reason => reported = reason;
                vm.SaveReplayAsync().GetAwaiter().GetResult();
                Assert.NotNull(reported);
                Assert.Contains("Replay is off", reported);
                Assert.Contains(vm.Settings.CaptureToggleHotkey, reported);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                vm?.Dispose();
                app?.Shutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Replay hotkey WPF test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
