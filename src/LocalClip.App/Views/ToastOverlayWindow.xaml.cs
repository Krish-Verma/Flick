using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Forms;

namespace LocalClip.App.Views;

/// <summary>
/// Very small, temporary, click-through WPF surface. No game injection,
/// polling, GPU shaders or persistent overlay process.
/// </summary>
public partial class ToastOverlayWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolwindow = 0x80;
    private const int WsExNoactivate = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public ToastOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int exstyle = GetWindowLong(hwnd, GwlExstyle);
            _ = SetWindowLong(hwnd, GwlExstyle,
                exstyle | WsExNoactivate | WsExTransparent | WsExToolwindow);
        };
    }

    public void SetMessage(string heading, string detail, bool error)
    {
        NotificationBody.Heading = heading;
        NotificationBody.Detail = detail;
        NotificationBody.IsError = error;
    }

    public void PlaceOnForegroundDisplay()
    {
        // Capture the monitor focused by the gamer, not necessarily the Flick window.
        IntPtr foreground = GetForegroundWindow();
        var display = foreground != IntPtr.Zero
            ? Screen.FromHandle(foreground)
            : Screen.PrimaryScreen;
        if (display is null) return;

        // Windows Screen returns physical pixels; WPF positions use DIPs.
        // Windows mixed-DPI screens require on-device testing (see handoff).
        var dpi = VisualTreeHelper.GetDpi(this);
        double dx = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1d;
        double dy = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1d;
        Left = display.WorkingArea.Right / dx - Width - 18;
        Top = display.WorkingArea.Top / dy + 18;
    }
}
