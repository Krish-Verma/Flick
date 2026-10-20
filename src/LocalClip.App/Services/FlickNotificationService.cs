using System;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LocalClip.App.Views;

namespace LocalClip.App.Services;

/// <summary>
/// Lazily creates one short-lived overlay per burst of notifications, then
/// closes it completely. Hidden/inactive Flick never runs an overlay timer.
/// Sound uses a tiny generated PCM tone in-process, not a media player engine.
/// </summary>
public sealed class FlickNotificationService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private ToastOverlayWindow? _window;
    private DispatcherTimer? _dismissTimer;
    private SoundPlayer? _chime;
    private MemoryStream? _chimeData;
    private bool _disposed;
    private int _generation;

    public FlickNotificationService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public void NotifySaved(string clipTitle, double durationSeconds, bool showOverlay, bool playSound)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _ = _dispatcher.BeginInvoke(new Action(() =>
                NotifySaved(clipTitle, durationSeconds, showOverlay, playSound)));
            return;
        }

        if (playSound) PlayChime();
        if (!showOverlay) return;
        int seconds = Math.Max(0, (int)Math.Round(durationSeconds));
        string detail = string.IsNullOrWhiteSpace(clipTitle)
            ? FormatTime(seconds) : clipTitle.Trim() + " · " + FormatTime(seconds);
        Show("Clip Saved", detail, error: false);
    }

    public void NotifySaveFailed(string message, bool showOverlay)
    {
        if (_disposed || !showOverlay) return;
        if (!_dispatcher.CheckAccess())
        {
            _ = _dispatcher.BeginInvoke(new Action(() => NotifySaveFailed(message, true)));
            return;
        }
        Show("Couldn't Save Clip", "Check Flick for details", error: true);
    }

    public void Preview(bool playSound)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _ = _dispatcher.BeginInvoke(new Action(() => Preview(playSound)));
            return;
        }
        if (playSound) PlayChime();
        Show("Clip Saved", "Last Round Clutch · 0:29", error: false);
    }

    private void Show(string heading, string detail, bool error)
    {
        if (_disposed) return;

        // Never stack notifications or leave a topmost surface running.
        _generation++;
        _dismissTimer?.Stop();
        if (_window is null)
        {
            _window = new ToastOverlayWindow();
            _window.Closed += (_, _) =>
            {
                if (!_disposed) _window = null;
            };
        }
        _window.SetMessage(heading, detail, error);
        if (!_window.IsVisible)
        {
            _window.Opacity = 0;
            _window.Show();
        }

        _window.PlaceOnForegroundDisplay();
        _window.BeginAnimation(Window.OpacityProperty, null);
        _window.Opacity = 0;
        _window.BeginAnimation(Window.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130))
            { FillBehavior = FillBehavior.Stop });
        _window.Opacity = 1;

        _dismissTimer ??= new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _dismissTimer.Tick -= OnDismiss;
        _dismissTimer.Tick += OnDismiss;
        _dismissTimer.Start();
    }

    private void OnDismiss(object? sender, EventArgs e)
    {
        _dismissTimer?.Stop();
        if (_window is null) return;
        var window = _window;
        int generation = _generation;
        var animation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140))
        {
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            if (!_disposed && generation == _generation && ReferenceEquals(_window, window))
            {
                _window = null;
                window.Close();
            }
        };
        window.BeginAnimation(Window.OpacityProperty, animation);
    }

    private static string FormatTime(int seconds) =>
        $"{seconds / 60}:{seconds % 60:00}";

    private void PlayChime()
    {
        try
        {
            if (_chime is null)
            {
                _chimeData = CreateChimeWave();
                _chime = new SoundPlayer(_chimeData);
                _chime.Load();
            }
            _chime.Play();
        }
        catch (Exception)
        {
            // Audio notifications must never interrupt or invalidate a real save.
        }
    }

    private static MemoryStream CreateChimeWave()
    {
        const int sampleRate = 22050;
        const int sampleCount = 3969; // 180ms
        var memory = new MemoryStream(44 + sampleCount * 2);
        using (var writer = new BinaryWriter(memory, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + sampleCount * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(sampleCount * 2);
            for (int i = 0; i < sampleCount; i++)
            {
                double t = (double)i / sampleRate;
                double ramp = Math.Min(1d, t / 0.012) *
                              Math.Min(1d, (sampleCount - i) / (sampleRate * 0.065));
                double frequency = i < sampleCount * .48 ? 784 : 1046.5;
                double tone = Math.Sin(2d * Math.PI * frequency * t);
                writer.Write((short)(tone * ramp * 5400)); // deliberately quiet
            }
        }
        memory.Position = 0;
        return memory;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dismissTimer?.Stop();
        _dismissTimer = null;
        if (_window != null)
        {
            _window.Close();
            _window = null;
        }
        _chime?.Dispose();
        _chimeData?.Dispose();
        _chime = null;
        _chimeData = null;
    }
}
