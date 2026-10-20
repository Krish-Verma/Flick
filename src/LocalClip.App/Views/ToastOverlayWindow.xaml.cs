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
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpNozorder = 0x0004;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
        int x, int y, int cx, int cy, uint flags);

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

        // Position in the monitor's physical coordinate system through Win32.
        // Avoid dividing absolute virtual-screen coordinates by WPF DPI, which
        // can place a toast on the wrong display under mixed scaling.
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        var dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Ceiling(Width * Math.Max(1d, dpi.DpiScaleX));
        int height = (int)Math.Ceiling(Height * Math.Max(1d, dpi.DpiScaleY));
        int x = display.WorkingArea.Right - width - 18;
        int y = display.WorkingArea.Top + 18;
        if (!SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height,
                SwpNoactivate | SwpNozorder))
        {
            // A failed DWM move must not leave a toast at an arbitrary location.
            Left = display.WorkingArea.Right / Math.Max(1d, dpi.DpiScaleX)
                - Width - 18;
            Top = display.WorkingArea.Top / Math.Max(1d, dpi.DpiScaleY) + 18;
        }
    }
}
