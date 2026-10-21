using System;
using System.IO;
using Microsoft.Win32;

namespace LocalClip.App.Services;

/// <summary>Per-user opt-in startup only; never requests elevation or arms capture.</summary>
public static class WindowsStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Flick";

    public static string BuildCommand(string executablePath, bool minimized)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || executablePath.Contains('"'))
            throw new ArgumentException("Invalid application path", nameof(executablePath));
        return "\"" + executablePath + "\"" + (minimized ? " --minimized" : "");
    }

    public static void Apply(bool enabled, bool minimized)
    {
        using var registry = Registry.CurrentUser.CreateSubKey(RunKey, true)
            ?? throw new InvalidOperationException("Cannot access per-user Windows startup preferences.");
        if (!enabled)
        {
            registry.DeleteValue(ValueName, false);
            return;
        }

        // The executable is now branded Flick.exe; preserve compatibility
        // with users explicitly launching an older LocalClip.App.exe apphost.
        // Never accidentally register a system dotnet.exe host at login.
        string? running = Environment.ProcessPath;
        string path = running != null &&
            (Path.GetFileName(running).Equals("Flick.exe", StringComparison.OrdinalIgnoreCase) ||
             Path.GetFileName(running).Equals("LocalClip.App.exe", StringComparison.OrdinalIgnoreCase))
            ? running : Path.Combine(AppContext.BaseDirectory, "Flick.exe");
        if (!File.Exists(path))
            throw new FileNotFoundException("Install Flick before enabling Windows startup.", path);
        registry.SetValue(ValueName, BuildCommand(path, minimized), RegistryValueKind.String);
    }
}
