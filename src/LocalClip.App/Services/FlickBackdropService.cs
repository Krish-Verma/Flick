using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LocalClip.App.Services;

/// <summary>
/// Optional Windows 11 compositor backdrop; no background capture, custom
/// pixel shader, timer, or additional process. Solid fallback is mandatory.
/// Not wired into the existing capture window until the approved shell slice.
/// </summary>
public static class FlickBackdropService
{
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmSystemBackdropAuto = 0;
    private const int DwmSystemBackdropMica = 2;
    // Windows 11 DWM attributes allow native caption theming while retaining
    // genuine Windows min/max/close, keyboard access, Snap and resize.
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const int DwmwaBorderColor = 34;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Theme the real Windows caption, not a fake WPF title bar. No custom
    /// borderless window, per-frame effect, animation, timer or memory churn.
    /// Windows high-contrast remains fully owned by the OS.
    /// </summary>
    public static bool TryApplyDarkTitleBar(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ||
            SystemParameters.HighContrast) return false;
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            int darkMode = 1;
            int bg = ColorRef(21, 22, 34);       // Flick.Surface #151622
            int fg = ColorRef(242, 239, 249);    // Flick.TextPrimary #F2EFF9
            int edge = ColorRef(69, 64, 82);     // Flick.Border #454052
            bool dark = DwmSetWindowAttribute(
                hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int)) >= 0;
            bool caption = DwmSetWindowAttribute(
                hwnd, DwmwaCaptionColor, ref bg, sizeof(int)) >= 0;
            _ = DwmSetWindowAttribute(
                hwnd, DwmwaTextColor, ref fg, sizeof(int));
            _ = DwmSetWindowAttribute(
                hwnd, DwmwaBorderColor, ref edge, sizeof(int));
            return dark && caption;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    private static int ColorRef(byte r, byte g, byte b) =>
        r | (g << 8) | (b << 16);

    public static bool TryApplyMica(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!FlickVisualPreferences.CanUseTransparency ||
            !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            return false;

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;

        int desired = DwmSystemBackdropMica;
        try
        {
            // HRESULT >= 0 means accepted, not proof of visible translucency.
            return DwmSetWindowAttribute(
                hwnd, DwmwaSystemBackdropType, ref desired, sizeof(int)) >= 0;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    public static void Reset(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int defaultType = DwmSystemBackdropAuto;
        try
        {
            _ = DwmSetWindowAttribute(
                hwnd, DwmwaSystemBackdropType, ref defaultType, sizeof(int));
            // Reset custom caption controls when disposing the native window.
            int defaultColor = unchecked((int)0xFFFFFFFF);
            int noDarkPreference = 0;
            _ = DwmSetWindowAttribute(
                hwnd, DwmwaCaptionColor, ref defaultColor, sizeof(int));
            _ = DwmSetWindowAttribute(
                hwnd, DwmwaTextColor, ref defaultColor, sizeof(int));
            _ = DwmSetWindowAttribute(
                hwnd, DwmwaBorderColor, ref defaultColor, sizeof(int));
            _ = DwmSetWindowAttribute(
                hwnd, DwmwaUseImmersiveDarkMode, ref noDarkPreference, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}
