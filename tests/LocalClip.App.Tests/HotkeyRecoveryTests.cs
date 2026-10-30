using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using LocalClip.App.Hotkeys;
using Xunit;

namespace LocalClip.App.Tests;

public sealed class HotkeyRecoveryTests
{
    [Fact]
    public void NativeShortcutEditingReleasesAndRestoresKeysWithHonestConflictAndDisposalState()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Message-only windows: no Application, real user profile,
                // visible UI, capture source, keyboard injection or normal
                // Flick shortcut is involved.
                using var blocker = new HwndSource(new HwndSourceParameters("Flick hotkey qualification blocker") {
                    ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 1, Height = 1 });
                using var target = new HwndSource(new HwndSourceParameters("Flick hotkey qualification target") {
                    ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 1, Height = 1 });
                var reserved = new List<(int Id, uint Key, string Chord)>();
                try
                {
                    for (int function = 17; function <= 24 && reserved.Count < 3; function++)
                    {
                        int id = 4000 + function;
                        uint key = (uint)(0x70 + function - 1);
                        if (RegisterHotKey(blocker.Handle, id, Modifiers, key))
                            reserved.Add((id, key, $"Ctrl+Alt+Shift+F{function}"));
                    }
                    Assert.Equal(3, reserved.Count); // unavailable is not a passing skipped test
                    var toggle = reserved[0];
                    var save = reserved[1];
                    var conflict = reserved[2];
                    Assert.True(UnregisterHotKey(blocker.Handle, toggle.Id));
                    Assert.True(UnregisterHotKey(blocker.Handle, save.Id));
                    using (var manager = new HotkeyManager())
                    {
                        manager.Initialize(target.Handle);
                        Assert.True(manager.TryConfigure(toggle.Chord, save.Chord, out var initialError), initialError);
                        Assert.False(manager.TryConfigure(toggle.Chord, conflict.Chord, out var error));
                        Assert.Contains("1409", error); // ERROR_HOTKEY_ALREADY_REGISTERED
                        Assert.Equal(toggle.Chord, manager.RegisteredToggleHotkey);
                        Assert.Equal(save.Chord, manager.RegisteredSaveHotkey);
                        Assert.False(RegisterHotKey(blocker.Handle, toggle.Id, Modifiers, toggle.Key));
                        Assert.Equal(1409, Marshal.GetLastWin32Error());
                        Assert.False(RegisterHotKey(blocker.Handle, save.Id, Modifiers, save.Key));
                        Assert.Equal(1409, Marshal.GetLastWin32Error());

                        var restoreErrors = new List<string>();
                        manager.ShortcutRestoreFailed += restoreErrors.Add;
                        manager.IsShortcutEditing = true;
                        manager.IsShortcutEditing = true; // repeated focus/edit notifications are harmless
                        Assert.Null(manager.RegisteredToggleHotkey);
                        Assert.Null(manager.RegisteredSaveHotkey);
                        Assert.True(RegisterHotKey(blocker.Handle, toggle.Id, Modifiers, toggle.Key));
                        Assert.True(RegisterHotKey(blocker.Handle, save.Id, Modifiers, save.Key));
                        Assert.False(manager.TryConfigure(conflict.Chord, save.Chord, out var pausedError));
                        Assert.Contains("Finish editing", pausedError);
                        Assert.False(manager.RegisterToggleHotkey(conflict.Chord, out _));
                        Assert.False(manager.RegisterSaveHotkey(conflict.Chord, out _));
                        Assert.True(UnregisterHotKey(blocker.Handle, toggle.Id));
                        Assert.True(UnregisterHotKey(blocker.Handle, save.Id));
                        manager.IsShortcutEditing = false;
                        manager.IsShortcutEditing = false;
                        Assert.Equal(toggle.Chord, manager.RegisteredToggleHotkey);
                        Assert.Equal(save.Chord, manager.RegisteredSaveHotkey);
                        Assert.Empty(restoreErrors);
                        Assert.False(RegisterHotKey(blocker.Handle, toggle.Id, Modifiers, toggle.Key));
                        Assert.Equal(1409, Marshal.GetLastWin32Error());
                        Assert.False(RegisterHotKey(blocker.Handle, save.Id, Modifiers, save.Key));
                        Assert.Equal(1409, Marshal.GetLastWin32Error());

                        // Another owned window can claim a key while editing.
                        // A failed restore must expose the missing native key,
                        // retain its configured chord, and allow a real retry.
                        manager.IsShortcutEditing = true;
                        Assert.True(RegisterHotKey(blocker.Handle, save.Id, Modifiers, save.Key));
                        manager.IsShortcutEditing = false;
                        Assert.Equal(toggle.Chord, manager.RegisteredToggleHotkey);
                        Assert.Null(manager.RegisteredSaveHotkey);
                        Assert.Contains("Settings", Assert.Single(restoreErrors));
                        manager.IsShortcutEditing = false;
                        Assert.Single(restoreErrors);
                        Assert.True(UnregisterHotKey(blocker.Handle, save.Id));
                        Assert.True(manager.TryConfigure(toggle.Chord, save.Chord, out var retryError), retryError);
                        Assert.Equal(toggle.Chord, manager.RegisteredToggleHotkey);
                        Assert.Equal(save.Chord, manager.RegisteredSaveHotkey);

                        // The individual registration API must also reacquire
                        // an equivalent configured chord when its native ID is absent.
                        manager.IsShortcutEditing = true;
                        Assert.True(RegisterHotKey(blocker.Handle, toggle.Id, Modifiers, toggle.Key));
                        manager.IsShortcutEditing = false;
                        Assert.Null(manager.RegisteredToggleHotkey);
                        Assert.Equal(save.Chord, manager.RegisteredSaveHotkey);
                        Assert.Equal(2, restoreErrors.Count);
                        Assert.True(UnregisterHotKey(blocker.Handle, toggle.Id));
                        Assert.True(manager.RegisterToggleHotkey(toggle.Chord, out var directRetryError), directRetryError);
                        Assert.Equal(toggle.Chord, manager.RegisteredToggleHotkey);
                    }
                    Assert.True(RegisterHotKey(blocker.Handle, toggle.Id, Modifiers, toggle.Key));
                    Assert.True(RegisterHotKey(blocker.Handle, save.Id, Modifiers, save.Key));
                    Assert.True(UnregisterHotKey(blocker.Handle, toggle.Id));
                    Assert.True(UnregisterHotKey(blocker.Handle, save.Id));

                    using var pausedManager = new HotkeyManager();
                    pausedManager.Initialize(target.Handle);
                    Assert.True(pausedManager.TryConfigure(toggle.Chord, save.Chord, out var pausedInitialError), pausedInitialError);
                    int disposedRestoreErrors = 0;
                    pausedManager.ShortcutRestoreFailed += _ => disposedRestoreErrors++;
                    pausedManager.IsShortcutEditing = true;
                    Assert.True(RegisterHotKey(blocker.Handle, toggle.Id, Modifiers, toggle.Key));
                    Assert.True(RegisterHotKey(blocker.Handle, save.Id, Modifiers, save.Key));
                    pausedManager.Dispose();
                    pausedManager.IsShortcutEditing = false;
                    pausedManager.IsShortcutEditing = true;
                    Assert.False(pausedManager.IsShortcutEditing);
                    Assert.Null(pausedManager.RegisteredToggleHotkey);
                    Assert.Null(pausedManager.RegisteredSaveHotkey);
                    Assert.Equal(0, disposedRestoreErrors);
                    Assert.False(pausedManager.TryConfigure(toggle.Chord, save.Chord, out _));
                    Assert.False(pausedManager.RegisterSaveHotkey(save.Chord, out _));
                    Assert.True(UnregisterHotKey(blocker.Handle, toggle.Id));
                    Assert.True(UnregisterHotKey(blocker.Handle, save.Id));
                    Assert.True(RegisterHotKey(blocker.Handle, toggle.Id, Modifiers, toggle.Key));
                    Assert.True(RegisterHotKey(blocker.Handle, save.Id, Modifiers, save.Key));
                }
                finally
                {
                    foreach (var item in reserved) UnregisterHotKey(blocker.Handle, item.Id);
                }
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Owned hotkey qualification thread timed out.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private const uint Modifiers = 0x4007; // NOREPEAT + CTRL + ALT + SHIFT
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
