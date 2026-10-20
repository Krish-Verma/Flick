using System;
using System.IO;
using Microsoft.Win32;

namespace LocalClip.App.Services;

/// <summary>Per-user opt-in startup only; never requests elevation or arms capture.</summary>
public static class WindowsStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Flick";

    public static string BuildCommand(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || executablePath.Contains('"'))
            throw new ArgumentException("Invalid application path", nameof(executablePath));
        return "\"" + executablePath + "\" --minimized";
    }

    public static void Apply(bool enabled)
    {
        using var registry = Registry.CurrentUser.CreateSubKey(RunKey, true)
            ?? throw new InvalidOperationException("Cannot access per-user Windows startup preferences.");
        if (!enabled)
        {
            registry.DeleteValue(ValueName, false);
            return;
        }

        string path = Path.Combine(AppContext.BaseDirectory, "LocalClip.App.exe");
        if (!File.Exists(path))
            throw new FileNotFoundException("Install Flick before enabling Windows startup.", path);
        registry.SetValue(ValueName, BuildCommand(path), RegistryValueKind.String);
    }
}
