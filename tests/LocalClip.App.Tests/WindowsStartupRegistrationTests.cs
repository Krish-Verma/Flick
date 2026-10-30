using System.IO;
using LocalClip.App.Services;
using Xunit;

namespace LocalClip.App.Tests;

// Pure registry-store fixtures: no test reads/writes HKCU or launches a process.
public sealed class WindowsStartupRegistrationTests
{
    private const string Executable = @"C:\Program Files\Flick\Flick.exe";

    [Theory]
    [InlineData(false, "\"C:\\Program Files\\Flick\\Flick.exe\" --startup")]
    [InlineData(true, "\"C:\\Program Files\\Flick\\Flick.exe\" --startup --minimized")]
    public void StartupCommandQuotesTheActualAppAndPreservesTrayIntent(bool tray, string expected) =>
        Assert.Equal(expected, WindowsStartupRegistration.BuildCommand(Executable, tray));

    [Theory]
    [InlineData("Flick.exe")]
    [InlineData("C:\\Program Files\\Flick\\dotnet.exe")]
    [InlineData("C:\\Program Files\\Flick\\Flick.exe\" --other")]
    [InlineData("C:\\Program Files\\Flick\\\nFlick.exe")]
    public void StartupRejectsRelativeOrUnownedCommandTargets(string path) =>
        Assert.Throws<ArgumentException>(() => WindowsStartupRegistration.BuildCommand(path, false));

    [Fact]
    public void RepeatEnabledSaveRepairsAMissingOrMovedAppRegistration()
    {
        var store = new Store { Command = "\"C:\\Old Flick\\Flick.exe\"" };
        WindowsStartupRegistration.Apply(true, true, store, Executable);
        Assert.Equal(WindowsStartupRegistration.BuildCommand(Executable, true), store.Command);
        Assert.Equal(1, store.Writes);
        store.Command = null; // Simulate cleanup/missing Run value between explicit Saves.
        WindowsStartupRegistration.Apply(true, true, store, Executable);
        Assert.Equal(WindowsStartupRegistration.BuildCommand(Executable, true), store.Command);
        Assert.Equal(2, store.Writes);
        WindowsStartupRegistration.Apply(true, true, store, Executable);
        Assert.Equal(2, store.Writes); // Readback checks avoid unnecessary registry writes.
    }

    [Fact]
    public void DisableRemovesTheOwnedRunValueWithoutApprovalChanges()
    {
        var store = new Store { Command = WindowsStartupRegistration.BuildCommand(Executable, true), Approval = Disabled(3) };
        byte[] before = store.Approval.ToArray();
        WindowsStartupRegistration.Apply(false, true, store, null);
        Assert.Null(store.Command);
        Assert.Equal(1, store.Removals);
        Assert.Equal(before, store.Approval);
    }

    [Fact]
    public void ReadbackFailureRestoresThePreviousRegistrationAndReportsFailure()
    {
        const string previous = "\"C:\\Earlier Install\\Flick.exe\"";
        var store = new Store { Command = previous, CorruptNextWrite = true };
        Assert.Throws<IOException>(() => WindowsStartupRegistration.Apply(true, true, store, Executable));
        Assert.Equal(previous, store.Command);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    public void WindowsDisabledStateIsReportedWithoutOverridingIt(byte marker)
    {
        var store = new Store { Approval = Disabled(marker) };
        byte[] before = store.Approval.ToArray();
        WindowsStartupRegistration.Apply(true, true, store, Executable);
        var status = WindowsStartupRegistration.ReadStatus(true, store, Executable);
        Assert.Equal(WindowsStartupRegistrationState.DisabledByWindows, status.State);
        Assert.Contains("Windows", status.Message);
        Assert.Equal(before, store.Approval);
        Assert.NotNull(store.Command); // Remains listed so the owner can enable it in Windows Settings.
    }

    [Fact]
    public void RegisteredMeansVerifiedRunCommandRatherThanAClaimThatLogonExecuted()
    {
        var store = new Store { Command = WindowsStartupRegistration.BuildCommand(Executable, false) };
        var status = WindowsStartupRegistration.ReadStatus(false, store, Executable);
        Assert.Equal(WindowsStartupRegistrationState.Registered, status.State);
        Assert.Contains("Registered", status.Message);
        Assert.Equal(0, store.Writes);
        Assert.Equal(0, store.Removals);
        Assert.Equal(WindowsStartupRegistrationState.MissingOrStale,
            WindowsStartupRegistration.ReadStatus(true, store, Executable).State);
    }

    private static byte[] Disabled(byte marker) { var bytes = new byte[12]; bytes[0] = marker; return bytes; }

    private sealed class Store : IWindowsStartupStore
    {
        public string? Command;
        public byte[]? Approval;
        public int Writes, Removals;
        public bool CorruptNextWrite;
        public string? ReadCommand() => Command;
        public void WriteCommand(string command)
        {
            Writes++;
            Command = CorruptNextWrite ? "corrupt command" : command;
            CorruptNextWrite = false;
        }
        public void RemoveCommand() { Removals++; Command = null; }
        public byte[]? ReadApproval() => Approval;
    }
}
