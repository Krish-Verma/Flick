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

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int size);

    public static bool TryApplyMica(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (SystemParameters.HighContrast ||
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
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}
