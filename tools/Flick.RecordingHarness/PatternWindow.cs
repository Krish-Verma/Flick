using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LocalClip.Core.Ipc;

namespace Flick.RecordingHarness;

public sealed record PatternSample(double XFraction, double YFraction, byte R, byte G, byte B);

/// <summary>
/// A window containing generated pixels only. Construct and run it on the harness STA;
/// the asynchronous lifecycle methods also marshal calls from its worker tasks.
/// Accessing the static sample contract does not construct any WPF object.
/// </summary>
public sealed class PatternWindow : Window, IDisposable
{
    public const string FixtureTitle = "Flick Recording Harness - Synthetic Fixture";
    public const int FrameWidth = 640;
    public const int FrameHeight = 360;
    public const int TargetFramesPerSecond = 20;
    public const double MotionBandTop = .46;
    public const double MotionBandBottom = .66;

    // Sample patches are deliberately far from the header, counter and motion band.
    public static IReadOnlyList<PatternSample> Samples { get; } = Array.AsReadOnly<PatternSample>(
    [
        new(.125, .25, 224, 32, 32),
        new(.875, .25, 32, 208, 64),
        new(.125, .75, 32, 64, 224),
        new(.875, .75, 224, 208, 32)
    ]);

    private readonly PatternCanvas _pattern = new();
    private readonly DispatcherTimer _animation;
    private CaptureWindowIdentity? _identity;
    private bool _closed;

    public IntPtr Hwnd { get; private set; }
    public uint ProcessId { get; } = checked((uint)Environment.ProcessId);
    public string ExecutablePath { get; } = Environment.ProcessPath ??
        throw new InvalidOperationException("The harness executable identity is unavailable.");
    public string ExecutableName => Path.GetFileName(ExecutablePath);
    public int PhysicalClientWidth { get; private set; }
    public int PhysicalClientHeight { get; private set; }
    public CaptureWindowIdentity Identity => _identity ??
        throw new InvalidOperationException("ShowReadyAsync must finish before reading the fixture identity.");

    public PatternWindow()
    {
        Title = FixtureTitle;
        Width = FrameWidth;
        Height = FrameHeight;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 48;
        Top = 48;
        ShowInTaskbar = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Content = _pattern;
        _animation = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1.0 / TargetFramesPerSecond)
        };
        _animation.Tick += (_, _) => _pattern.Advance();
        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            NormalizeClientSize();
        };
        IsVisibleChanged += (_, _) => UpdateAnimation();
        StateChanged += (_, _) => UpdateAnimation();
        Closed += (_, _) =>
        {
            _closed = true;
            _animation.Stop();
        };
    }

    public async Task<CaptureWindowIdentity> ShowReadyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.InvokeAsync(() =>
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            WindowState = WindowState.Normal;
            if (!IsVisible) Show();
            NormalizeClientSize();
            UpdateLayout();
            UpdateAnimation();
        }, DispatcherPriority.Normal, cancellationToken).Task.ConfigureAwait(false);

        // ContextIdle follows WPF's Loaded and Render work. The identity is not
        // released merely because Show created an HWND before its content rendered.
        return await Dispatcher.InvokeAsync(() =>
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            NormalizeClientSize();
            UpdateLayout();
            _identity = new CaptureWindowIdentity(unchecked((ulong)Hwnd.ToInt64()),
                ProcessId, FixtureTitle, ExecutableName);
            return _identity;
        }, DispatcherPriority.ContextIdle, cancellationToken).Task.ConfigureAwait(false);
    }

    public Task MinimizeAsync() => DispatchAsync(() =>
    {
        if (!_closed && IsVisible) WindowState = WindowState.Minimized;
    });

    public Task RestoreAsync() => DispatchAsync(() =>
    {
        if (_closed) return;
        WindowState = WindowState.Normal;
        if (IsVisible) NormalizeClientSize();
        UpdateAnimation();
    });

    public Task CloseAsync() => DispatchAsync(() =>
    {
        if (!_closed) Close();
    });

    public void Dispose()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        if (Dispatcher.CheckAccess())
        {
            if (!_closed) Close();
        }
        else Dispatcher.Invoke(() => { if (!_closed) Close(); });
        GC.SuppressFinalize(this);
    }

    private Task DispatchAsync(Action action)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return Task.CompletedTask;
        return Dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
    }

    private void UpdateAnimation()
    {
        if (!_closed && IsVisible && WindowState != WindowState.Minimized) _animation.Start();
        else _animation.Stop();
    }

    private void NormalizeClientSize()
    {
        if (Hwnd == IntPtr.Zero || WindowState == WindowState.Minimized) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        Width = FrameWidth / dpi.DpiScaleX;
        Height = FrameHeight / dpi.DpiScaleY;
        // Borderless client == outer bounds. Set the owned HWND's physical size,
        // rather than assuming that 640 WPF DIPs are 640 pixels at every DPI.
        if (!GetClientRect(Hwnd, out var rectangle)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (rectangle.Right - rectangle.Left != FrameWidth || rectangle.Bottom - rectangle.Top != FrameHeight)
        {
            const uint noMoveNoZOrderNoActivate = 0x0002 | 0x0004 | 0x0010;
            if (!SetWindowPos(Hwnd, IntPtr.Zero, 0, 0, FrameWidth, FrameHeight, noMoveNoZOrderNoActivate))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!GetClientRect(Hwnd, out rectangle)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        PhysicalClientWidth = rectangle.Right - rectangle.Left;
        PhysicalClientHeight = rectangle.Bottom - rectangle.Top;
        if (PhysicalClientWidth != FrameWidth || PhysicalClientHeight != FrameHeight)
            throw new InvalidOperationException("The synthetic fixture could not establish its exact physical client dimensions.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out NativeRectangle rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);

    private sealed class PatternCanvas : FrameworkElement
    {
        private readonly SolidColorBrush[] _colors = Samples.Select(sample =>
            FrozenBrush(sample.R, sample.G, sample.B)).ToArray();
        private readonly SolidColorBrush _dark = FrozenBrush(16, 20, 28);
        private readonly SolidColorBrush _white = FrozenBrush(248, 248, 248);
        private readonly Typeface _typeface = new("Consolas");
        private long _frame;

        public void Advance()
        {
            _frame++;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawing)
        {
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            drawing.PushTransform(new ScaleTransform(ActualWidth / FrameWidth, ActualHeight / FrameHeight));
            drawing.DrawRectangle(_colors[0], null, new Rect(0, 0, 320, 180));
            drawing.DrawRectangle(_colors[1], null, new Rect(320, 0, 320, 180));
            drawing.DrawRectangle(_colors[2], null, new Rect(0, 180, 320, 180));
            drawing.DrawRectangle(_colors[3], null, new Rect(320, 180, 320, 180));
            drawing.DrawRectangle(_dark, null, new Rect(0, 0, 640, 40));
            DrawText(drawing, "FLICK SYNTHETIC RECORDING FIXTURE", 12, 5, 15);
            DrawText(drawing, "Generated pixels only / no audio / no personal content", 12, 23, 11);

            // A 5.2-second triangle sweep. Nothing else in the verification band
            // is white, so decoded frames can measure this marker's centroid.
            int phase = (int)(_frame % 104);
            int step = phase <= 52 ? phase : 104 - phase;
            drawing.DrawRectangle(_white, null, new Rect(96 + step * 8, 176, 32, 32));
            drawing.DrawRectangle(_dark, null, new Rect(0, 300, 640, 60));
            DrawText(drawing, "SYNTHETIC FRAME " + _frame.ToString("D8", CultureInfo.InvariantCulture), 12, 311, 20);
            DrawText(drawing, "Close this window to cancel the qualification run.", 12, 338, 12);
            drawing.Pop();
        }

        private void DrawText(DrawingContext drawing, string text, double x, double y, double size)
        {
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                _typeface, size, _white, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawing.DrawText(formatted, new Point(x, y));
        }

        private static SolidColorBrush FrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
