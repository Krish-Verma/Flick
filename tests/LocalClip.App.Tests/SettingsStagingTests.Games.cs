using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LocalClip.App;
using LocalClip.App.ViewModels;
using LocalClip.App.Views;
using LocalClip.Core.GameDetection;
using LocalClip.Core.Models;
using Xunit;

namespace LocalClip.App.Tests;

public partial class SettingsStagingTests
{
    // Runs inside the existing single-Application STA fixture. No window is
    // shown, no picker is invoked, no profile is saved and no helper can start.
    private static void VerifyGameSettingsAndReservations(MainViewModel vm, MainWindow window)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var approved = new ApprovedGameProfile { Id = Guid.NewGuid(), Enabled = true, Name = "Synthetic approved game", ExecutablePath = @"C:\Synthetic\Game.exe" };
        var settings = AppSettings.CreateDefault(); settings.ApprovedGameProfiles = [approved];
        var page = new SettingsWindow(settings, () => false);
        var list = Assert.IsType<ComboBox>(page.FindName("LstApprovedGames"));
        var staged = Assert.IsType<ObservableCollection<ApprovedGameProfile>>(list.ItemsSource);
        Assert.False(Assert.IsType<CheckBox>(page.FindName("ChkGameDetectionEnabled")).IsChecked);
        Assert.NotSame(approved, Assert.Single(staged));
        list.SelectedIndex = 0;
        Assert.IsType<TextBox>(page.FindName("TxtGameName")).Text = "Staged rename";
        Assert.IsType<CheckBox>(page.FindName("ChkGameMicrophone")).IsChecked = true;
        typeof(SettingsWindow).GetMethod("ApplyGameProfile_Click", hidden)!.Invoke(page, [page, new RoutedEventArgs()]);
        Assert.Equal("Staged rename", Assert.Single(staged).Name);
        Assert.True(staged[0].AllowMicrophone);
        Assert.Equal(approved.Id, Assert.IsType<ApprovedGameProfile>(list.SelectedItem).Id);
        Assert.Equal("Synthetic approved game", settings.ApprovedGameProfiles[0].Name);
        Assert.False(settings.ApprovedGameProfiles[0].AllowMicrophone);
        typeof(SettingsWindow).GetMethod("Cancel_Click", hidden)!.Invoke(page, [page, new RoutedEventArgs()]);
        Assert.False(settings.GameDetectionEnabled);
        Assert.False(settings.ApprovedGameProfiles[0].AllowMicrophone);

        var busy = typeof(MainViewModel).GetField("_gameAutomationBusy", hidden)!;
        var operation = typeof(MainViewModel).GetField("_gameOperationTask", hidden)!;
        var cancellationField = typeof(MainViewModel).GetField("_gameOperationCancellation", hidden)!;
        var stopField = typeof(MainViewModel).GetField("_stopCaptureTask", hidden)!;
        var epoch = typeof(MainViewModel).GetField("_captureEpoch", hidden)!;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        object? originalStop = stopField.GetValue(vm);
        string originalState = vm.CaptureStatus;
        object? originalEpoch = epoch.GetValue(vm);
        busy.SetValue(vm, true); operation.SetValue(vm, completion.Task); cancellationField.SetValue(vm, cancellation);
        stopField.SetValue(vm, Task.CompletedTask); // Even a failed guard cannot invoke native IPC.
        try
        {
            Assert.False(vm.CanRunRecordingDiagnostic);
            Assert.False(vm.CanChangeCaptureSource);
            Assert.False(vm.CanStartExport);
            Assert.True(vm.IsCaptureActive);
            Assert.True(vm.IsBackgroundWorkActive);
            Task stop = vm.StopCaptureAsync();
            Task duplicateStop = vm.StopCaptureAsync();
            Assert.False(duplicateStop.IsCompleted);
            Assert.True(cancellation.IsCancellationRequested);
            Assert.False(stop.IsCompleted);
            Assert.Equal(originalEpoch, epoch.GetValue(vm));
            completion.TrySetResult();
            var frame = new DispatcherFrame();
            var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            guard.Tick += (_, _) => frame.Continue = false;
            _ = Task.WhenAll(stop, duplicateStop).ContinueWith(_ => window.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            guard.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { guard.Stop(); }
            Assert.True(stop.IsCompletedSuccessfully);
            Assert.True(duplicateStop.IsCompletedSuccessfully);
            Assert.Equal(originalState, vm.CaptureStatus);
            Assert.Equal(originalEpoch, epoch.GetValue(vm));
            Assert.False(vm.CaptureHost.IsRunning);
            Assert.False(vm.DiagnosticsInitialized);
            Assert.Null(typeof(MainViewModel).GetField("_gameSuppressionStore", hidden)!.GetValue(vm));
        }
        finally
        {
            completion.TrySetResult();
            busy.SetValue(vm, false); operation.SetValue(vm, null); cancellationField.SetValue(vm, null);
            stopField.SetValue(vm, originalStop);
        }
    }
}
