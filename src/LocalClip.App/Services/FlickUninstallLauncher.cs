using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace LocalClip.App.Services;

/// <summary>
/// Launch only the per-user Windows uninstaller associated with this
/// installed Flick.exe. Portable copies cannot self-delete and must never
/// run a shell command from an arbitrary registry path.
/// </summary>
public static class FlickUninstallLauncher
{
    private const string InstalledAppKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{F3C41F96-EF62-4CB1-A292-91EF3F6C18E8}_is1";

    public static bool IsInstalled => TryFindInstalledUninstaller(out _);

    private static bool TryFindInstalledUninstaller(out string path)
    {
        path = "";
        string appDir = Path.GetFullPath(AppContext.BaseDirectory);
        string exe = Path.Combine(appDir, "Flick.exe");
        string uninstallExe = Path.Combine(appDir, "unins000.exe");
        if (!File.Exists(exe) || !File.Exists(uninstallExe))
            return false;

        string? running = Environment.ProcessPath;
        if (running == null ||
            !string.Equals(Path.GetFullPath(running), Path.GetFullPath(exe),
                StringComparison.OrdinalIgnoreCase))
            return false;

        using var key = Registry.CurrentUser.OpenSubKey(InstalledAppKey);
        string? registered = key?.GetValue("UninstallString") as string;
        string? displayName = key?.GetValue("DisplayName") as string;
        if (!string.Equals(displayName, "Flick", StringComparison.OrdinalIgnoreCase) ||
            registered == null ||
            !registered.Contains(uninstallExe, StringComparison.OrdinalIgnoreCase))
            return false;

        path = uninstallExe;
        return true;
    }

    public static bool TryLaunch(out string error)
    {
        if (!TryFindInstalledUninstaller(out string uninstallExe))
        {
            error = "Flick is in portable mode or its normal Windows uninstall registration is unavailable. Use Settings > Apps > Installed Apps or quit Flick and remove its extracted folder.";
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = uninstallExe,
                WorkingDirectory = Path.GetDirectoryName(uninstallExe)!,
                UseShellExecute = true
            });
            if (process is null)
                throw new InvalidOperationException("Windows did not launch the registered uninstaller.");
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = "Couldn't start the Windows uninstaller: " + ex.Message;
            return false;
        }
    }
}
