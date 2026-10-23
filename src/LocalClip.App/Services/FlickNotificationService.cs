using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LocalClip.Core.Rules;
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
    private readonly object _soundGate = new();
    private CancellationTokenSource? _soundStop;
    private int _soundBusy;
    private int _soundEpoch;
    private bool _disposed;
    private int _generation;

    public FlickNotificationService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public void NotifySaved(double durationSeconds, bool showOverlay, bool playSound, string tone = "Chime", string customPath = "")
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _ = _dispatcher.BeginInvoke(new Action(() =>
                NotifySaved(durationSeconds, showOverlay, playSound, tone, customPath)));
            return;
        }

        if (playSound) PlayChime(tone, customPath);
        if (!showOverlay) return;
        Show("Clip Saved", NotificationWaveRules.DurationText(durationSeconds), error: false);
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

    public void Preview(bool playSound, string tone = "Chime", string customPath = "")
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess())
        {
            _ = _dispatcher.BeginInvoke(new Action(() => Preview(playSound, tone, customPath)));
            return;
        }
        if (playSound) PlayChime(tone, customPath);
        Show("Clip Saved", "30 sec", error: false);
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
        if (FlickVisualPreferences.ShouldAnimate)
        {
            _window.Opacity = 0;
            _window.BeginAnimation(Window.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130))
                { FillBehavior = FillBehavior.Stop });
        }
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
        if (!FlickVisualPreferences.ShouldAnimate)
        {
            _window = null;
            window.Close();
            return;
        }
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

    public void StopSound()
    {
        lock (_soundGate)
        {
            _soundEpoch++;
            try { _soundStop?.Cancel(); _chime?.Stop(); } catch { }
        }
    }

    private void PlayChime(string tone, string customPath)
    {
        // Coalesce bursts; IO, validation and decoding never run on the save/UI path.
        if (Interlocked.CompareExchange(ref _soundBusy, 1, 0) != 0) return;
        int epoch;
        lock (_soundGate) epoch = _soundEpoch;
        _ = Task.Run(async () =>
        {
            try
            {
                MemoryStream wave;
                try
                {
                    wave = tone == "Custom" ? new MemoryStream(NotificationSoundFile.Read(customPath))
                        : CreateChimeWave(tone == "Quiet");
                }
                catch { wave = CreateChimeWave(false); }
                using var stop = new CancellationTokenSource();
                _ = NotificationWaveRules.TryGetDuration(wave.ToArray(), out double seconds);
                using (wave)
                using (var player = new SoundPlayer(wave))
                {
                    player.Load();
                    lock (_soundGate)
                    {
                        if (_disposed || epoch != _soundEpoch) return;
                        _chime = player;
                        _soundStop = stop;
                        player.Play(); // Start atomically with the disabled/disposed check.
                    }
                    await Task.Delay(TimeSpan.FromSeconds(seconds + .05), stop.Token);
                    lock (_soundGate) if (ReferenceEquals(_chime, player)) _chime = null;
                }
            }
            catch { /* Confirmation audio never invalidates a saved clip. */ }
            finally
            {
                lock (_soundGate) { _chime = null; _soundStop = null; }
                Interlocked.Exchange(ref _soundBusy, 0);
            }
        });
    }

    private static MemoryStream CreateChimeWave(bool quiet)
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
                double frequency = i < sampleCount * .48 ? (quiet ? 660 : 784) : (quiet ? 784 : 1046.5);
                double tone = Math.Sin(2d * Math.PI * frequency * t);
                writer.Write((short)(tone * ramp * (quiet ? 2600 : 5400))); // deliberately quiet
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
        StopSound(); // Worker owns bounded stream/player disposal; no idle cache.
    }
}
