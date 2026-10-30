using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace LocalClip.App.Services;

public enum WindowsStartupRegistrationState { Registered, MissingOrStale, DisabledByWindows, Unavailable }

public sealed record WindowsStartupStatus(WindowsStartupRegistrationState State, string Message);

/// <summary>A narrow test seam; production writes only Flick's per-user Run value.</summary>
public interface IWindowsStartupStore
{
    string? ReadCommand();
    void WriteCommand(string command);
    void RemoveCommand();
    byte[]? ReadApproval();
}

/// <summary>Per-user opt-in startup only; never requests elevation or overrides Windows startup approval.</summary>
public static class WindowsStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "Flick";

    public static string BuildCommand(string executablePath, bool minimized)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || executablePath.Contains('"') ||
            executablePath.Any(char.IsControl) || !Path.IsPathFullyQualified(executablePath) ||
            !IsOwnedAppHost(executablePath))
            throw new ArgumentException("Invalid application path", nameof(executablePath));
        string command = "\"" + executablePath + "\" --startup" + (minimized ? " --minimized" : "");
        if (command.Length > 260)
            throw new ArgumentException("Install Flick in a shorter folder before enabling Windows startup.", nameof(executablePath));
        return command;
    }

    public static void Apply(bool enabled, bool minimized) =>
        Apply(enabled, minimized, new RegistryStartupStore(), enabled ? ResolveExecutablePath() : null);

    /// <summary>Write and independently read back the desired command. Used with an isolated store in tests.</summary>
    public static void Apply(bool enabled, bool minimized, IWindowsStartupStore store, string? executablePath)
    {
        ArgumentNullException.ThrowIfNull(store);
        string? desired = enabled ? BuildCommand(executablePath ?? "", minimized) : null;
        string? previous = store.ReadCommand();
        if (string.Equals(previous, desired, StringComparison.Ordinal)) return;
        try
        {
            if (enabled) store.WriteCommand(desired!);
            else store.RemoveCommand();
            if (!string.Equals(store.ReadCommand(), desired, StringComparison.Ordinal))
                throw new IOException("Windows startup registration could not be verified.");
        }
        catch (Exception writeError)
        {
            try
            {
                if (previous == null) store.RemoveCommand();
                else store.WriteCommand(previous);
                if (!string.Equals(store.ReadCommand(), previous, StringComparison.Ordinal))
                    throw new IOException("The previous startup registration could not be restored.");
            }
            catch (Exception restoreError)
            {
                throw new IOException("Windows startup registration could not be updated or restored.",
                    new AggregateException(writeError, restoreError));
            }
            throw;
        }
    }

    public static WindowsStartupStatus ReadStatus(bool minimized)
    {
        try { return ReadStatus(minimized, new RegistryStartupStore(), ResolveExecutablePath()); }
        catch (Exception) { return new(WindowsStartupRegistrationState.Unavailable, "Windows startup couldn't be checked."); }
    }

    public static WindowsStartupStatus ReadStatus(bool minimized, IWindowsStartupStore store, string executablePath)
    {
        ArgumentNullException.ThrowIfNull(store);
        string expected = BuildCommand(executablePath, minimized);
        if (!string.Equals(store.ReadCommand(), expected, StringComparison.Ordinal))
            return new(WindowsStartupRegistrationState.MissingOrStale, "Save Settings to repair Windows startup.");
        // StartupApproved is Explorer-owned state. Only recognize known disabled
        // markers; never write this key or infer that an unknown format grants
        // approval. A matching Run entry is registration, not proof of a logon.
        byte[]? approval = store.ReadApproval();
        if (approval is { Length: >= 12 } && (approval[0] == 3 || approval[0] == 7) &&
            approval[1] == 0 && approval[2] == 0 && approval[3] == 0)
            return new(WindowsStartupRegistrationState.DisabledByWindows,
                "Windows disabled Flick at startup. Enable it in Windows Startup apps.");
        return new(WindowsStartupRegistrationState.Registered, "Registered for Windows startup.");
    }

    private static string ResolveExecutablePath()
    {
        // The executable is now branded Flick.exe; preserve compatibility
        // with users explicitly launching an older LocalClip.App.exe apphost.
        // Never accidentally register a system dotnet.exe host at login.
        string? running = Environment.ProcessPath;
        string path = running != null && IsOwnedAppHost(running)
            ? running : Path.Combine(AppContext.BaseDirectory, "Flick.exe");
        if (!File.Exists(path))
            throw new FileNotFoundException("Install Flick before enabling Windows startup.", path);
        return path;
    }

    private static bool IsOwnedAppHost(string path) =>
        Path.GetFileName(path).Equals("Flick.exe", StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileName(path).Equals("LocalClip.App.exe", StringComparison.OrdinalIgnoreCase);

    private sealed class RegistryStartupStore : IWindowsStartupStore
    {
        public string? ReadCommand()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }

        public void WriteCommand(string command)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
                ?? throw new IOException("Windows startup preferences are unavailable.");
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }

        public void RemoveCommand()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        public byte[]? ReadApproval()
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovalKey, writable: false);
            return key?.GetValue(ValueName) as byte[];
        }
    }
}
