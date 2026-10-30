using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using System.Threading;
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
        string originalPreferences = JsonSerializer.Serialize(settings);
        var page = new SettingsWindow(settings, () => false);
        Assert.False(page.HasUnsavedChanges);
        Assert.False(page.CanSaveDraft);
        var list = Assert.IsType<ComboBox>(page.FindName("LstApprovedGames"));
        var staged = Assert.IsType<ObservableCollection<ApprovedGameProfile>>(list.ItemsSource);
        Assert.False(Assert.IsType<CheckBox>(page.FindName("ChkGameDetectionEnabled")).IsChecked);
        Assert.NotSame(approved, Assert.Single(staged));
        list.SelectedIndex = 0;
        Assert.False(page.HasUnsavedChanges); // Merely reviewing a profile is not an edit.
        Assert.IsType<TextBox>(page.FindName("TxtGameName")).Text = "Staged rename";
        Assert.IsType<CheckBox>(page.FindName("ChkGameMicrophone")).IsChecked = true;
        typeof(SettingsWindow).GetMethod("ApplyGameProfile_Click", hidden)!.Invoke(page, [page, new RoutedEventArgs()]);
        Assert.Equal("Staged rename", Assert.Single(staged).Name);
        Assert.True(staged[0].AllowMicrophone);
        Assert.True(page.HasUnsavedChanges);
        Assert.True(page.CanSaveDraft);
        Assert.True(Assert.IsType<Button>(page.FindName("BtnSaveSettings")).IsEnabled);
        Assert.Equal(approved.Id, Assert.IsType<ApprovedGameProfile>(list.SelectedItem).Id);
        Assert.Equal("Synthetic approved game", settings.ApprovedGameProfiles[0].Name);
        Assert.False(settings.ApprovedGameProfiles[0].AllowMicrophone);
        bool cancelled = false;
        page.CloseRequested += saved => { Assert.False(saved); cancelled = true; };
        typeof(SettingsWindow).GetMethod("Cancel_Click", hidden)!.Invoke(page, [page, new RoutedEventArgs()]);
        Assert.True(cancelled);
        Assert.Equal(originalPreferences, JsonSerializer.Serialize(settings));
        Assert.False(settings.GameDetectionEnabled);
        Assert.False(settings.ApprovedGameProfiles[0].AllowMicrophone);

        var busy = typeof(MainViewModel).GetField("_gameAutomationBusy", hidden)!;
        var operation = typeof(MainViewModel).GetField("_gameOperationTask", hidden)!;
        var cancellationField = typeof(MainViewModel).GetField("_gameOperationCancellation", hidden)!;
        var stopField = typeof(MainViewModel).GetField("_stopCaptureTask", hidden)!;
        var epoch = typeof(MainViewModel).GetField("_captureEpoch", hidden)!;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var manualStop = typeof(MainViewModel).GetField("_manualStopOperation", hidden)!;
        var manualIntent = typeof(MainViewModel).GetField("_manualReplayIntent", hidden)!;
        var manualReserved = typeof(MainViewModel).GetField("_manualReplayReserved", hidden)!;
        object? originalBusy = busy.GetValue(vm), originalOperation = operation.GetValue(vm),
            originalCancellation = cancellationField.GetValue(vm), originalStop = stopField.GetValue(vm),
            originalIntent = manualIntent.GetValue(vm), originalReservation = manualReserved.GetValue(vm);
        Assert.Null(manualStop.GetValue(vm)); // This fixture must own its stop reservation.
        var originalContext = SynchronizationContext.Current;
        string originalState = vm.CaptureStatus;
        object? originalEpoch = epoch.GetValue(vm);
        busy.SetValue(vm, true); operation.SetValue(vm, completion.Task); cancellationField.SetValue(vm, cancellation);
        stopField.SetValue(vm, Task.CompletedTask); // Even a failed guard cannot invoke native IPC.
        // A raw STA thread that never runs Application.Run has no ambient WPF
        // context. Model a real dispatcher click locally: the awaited stop's UI
        // notifications must resume on this dispatcher, including duplicate-
        // stop completion. Earlier blocking Settings helpers stay out
        // of this scope.
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
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
            Task stopped = Task.WhenAll(stop, duplicateStop);
            _ = stopped.ContinueWith(_ => window.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            guard.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { guard.Stop(); }
            Assert.True(stop.IsCompletedSuccessfully,
                "First stop failed or timed out: " + Describe(stop) + "; duplicate: " + Describe(duplicateStop));
            Assert.True(duplicateStop.IsCompletedSuccessfully,
                "Duplicate stop failed or timed out: " + Describe(duplicateStop));
            Assert.Null(manualStop.GetValue(vm));
            Assert.Equal(originalState, vm.CaptureStatus);
            Assert.Equal(originalEpoch, epoch.GetValue(vm));
            Assert.False(vm.CaptureHost.IsRunning);
            Assert.False(vm.DiagnosticsInitialized);
            Assert.Null(typeof(MainViewModel).GetField("_gameSuppressionStore", hidden)!.GetValue(vm));
        }
        finally
        {
            completion.TrySetResult();
            busy.SetValue(vm, originalBusy); operation.SetValue(vm, originalOperation); cancellationField.SetValue(vm, originalCancellation);
            stopField.SetValue(vm, originalStop);
            manualIntent.SetValue(vm, originalIntent); manualReserved.SetValue(vm, originalReservation);
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }

        static string Describe(Task task) => task.Status + (task.Exception == null ? "" : ": " + task.Exception);
    }
}
