using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace LocalClip.App.Hotkeys;

public class HotkeyManager : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private bool _isShortcutEditing;
    private bool _disposed;

    public bool IsShortcutEditing
    {
        get => _isShortcutEditing;
        set
        {
            if (_disposed || value == _isShortcutEditing) return;
            _isShortcutEditing = value;
            if (value)
            {
                // Dropping WM_HOTKEY is insufficient: Windows still consumes
                // the registered chord before the shortcut field can see it.
                ClearRegistrations(clearConfiguration: false);
                return;
            }

            if (!RestoreConfiguredHotkeys())
                ShortcutRestoreFailed?.Invoke("Some shortcuts are unavailable. Choose another shortcut in Settings.");
        }
    }

    private const int WM_HOTKEY = 0x0312;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private IntPtr _hwnd;
    private HwndSource? _source;
    private int _currentId = 1000;

    public event Action? OnCaptureToggleHotkeyPressed;
    public event Action? OnSaveReplayHotkeyPressed;
    public event Action<string>? ShortcutRestoreFailed;

    private int _toggleId = 0;
    private int _saveId = 0;
    private string? _toggleHotkey;
    private string? _saveHotkey;

    public void Initialize(IntPtr hwnd)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(HwndHook);
    }

    public bool RegisterToggleHotkey(string hotkeyStr, out string errorMessage)
        => Register(ref _toggleId, ref _toggleHotkey, hotkeyStr, out errorMessage);

    public bool RegisterSaveHotkey(string hotkeyStr, out string errorMessage)
        => Register(ref _saveId, ref _saveHotkey, hotkeyStr, out errorMessage);

    private bool Register(ref int currentId, ref string? previous, string requested, out string errorMessage)
    {
        errorMessage = "";
        if (!CanChangeRegistrations(out errorMessage)) return false;
        if (!ParseHotkey(requested, out uint modifiers, out uint vk))
        {
            errorMessage = $"Invalid hotkey: {requested}";
            return false;
        }
        if (currentId != 0 && previous != null && AreEquivalentHotkeys(previous, requested))
            return true;

        // Register replacement before removing the prior working shortcut.
        int candidate = ++_currentId;
        if (!RegisterHotKey(_hwnd, candidate, modifiers | MOD_NOREPEAT, vk))
        {
            errorMessage = $"Unable to register '{requested}' (Windows error {Marshal.GetLastWin32Error()}).";
            if (currentId != 0) errorMessage += " The previous shortcut remains active.";
            return false;
        }
        if (currentId != 0) UnregisterHotKey(_hwnd, currentId);
        currentId = candidate;
        previous = requested;
        return true;
    }

    // Configuration survives editing and a failed restore. These properties
    // describe actual native registrations, so callers can report lost keys.
    public string? RegisteredToggleHotkey => _toggleId != 0 ? _toggleHotkey : null;
    public string? RegisteredSaveHotkey => _saveId != 0 ? _saveHotkey : null;

    public bool TryConfigure(string toggle, string save, out string error)
    {
        error = "";
        if (!CanChangeRegistrations(out error)) return false;
        if (!IsValidHotkey(toggle) || !IsValidHotkey(save) ||
            AreEquivalentHotkeys(toggle, save))
        {
            error = "Use two distinct, valid hotkeys.";
            return false;
        }
        if (_toggleId != 0 && _saveId != 0 && _toggleHotkey != null && _saveHotkey != null &&
            AreEquivalentHotkeys(toggle, _toggleHotkey) &&
            AreEquivalentHotkeys(save, _saveHotkey))
            return true;

        string? oldToggle = _toggleHotkey, oldSave = _saveHotkey;
        ClearRegistrations();
        bool toggleOk = RegisterToggleHotkey(toggle, out var toggleError);
        string saveError = "";
        bool saveOk = toggleOk && RegisterSaveHotkey(save, out saveError);
        if (toggleOk && saveOk) return true;

        // Report the actual Win32 registration failure rather than hiding it
        // behind a generic message when the save shortcut is unavailable.
        error = toggleOk ? saveError : toggleError;
        ClearRegistrations();
        _toggleHotkey = oldToggle;
        _saveHotkey = oldSave;
        if (!RestoreConfiguredHotkeys()) error += " Previous shortcuts could not all be restored.";
        return false;
    }

    private bool CanChangeRegistrations(out string error)
    {
        error = _disposed ? "Flick is closing." :
            _isShortcutEditing ? "Finish editing the shortcut first." : "";
        return error.Length == 0;
    }

    private bool RestoreConfiguredHotkeys()
    {
        string? toggle = _toggleHotkey, save = _saveHotkey;
        bool toggleRestored = toggle == null || RegisterToggleHotkey(toggle, out _);
        // Restore the other key even when one was claimed by another app.
        bool saveRestored = save == null || RegisterSaveHotkey(save, out _);
        return toggleRestored && saveRestored;
    }

    private void ClearRegistrations(bool clearConfiguration = true)
    {
        if (_toggleId != 0) UnregisterHotKey(_hwnd, _toggleId);
        if (_saveId != 0) UnregisterHotKey(_hwnd, _saveId);
        _toggleId = _saveId = 0;
        if (clearConfiguration) _toggleHotkey = _saveHotkey = null;
    }

    public static bool AreEquivalentHotkeys(string left, string right) =>
        ParseHotkey(left, out var leftMods, out var leftKey) &&
        ParseHotkey(right, out var rightMods, out var rightKey) &&
        leftMods == rightMods && leftKey == rightKey;

    public static bool IsValidHotkey(string hotkey) => ParseHotkey(hotkey, out _, out _);

    private static bool ParseHotkey(string str, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(str)) return false;

        string[] tokens = str.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Key key = Key.None;

        foreach (string token in tokens)
        {
            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || token.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_CONTROL;
            }
            else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_SHIFT;
            }
            else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_ALT;
            }
            else if (token.Equals("Win", StringComparison.OrdinalIgnoreCase) || token.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_WIN;
            }
            else
            {
                if (Enum.TryParse<Key>(token, true, out var parsedKey) &&
                    Enum.IsDefined(typeof(Key), parsedKey) && key == Key.None)
                    key = parsedKey;
                else return false;
            }
        }

        if (key == Key.None) return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return LocalClip.Core.Rules.ShortcutChordRules.IsAllowed(modifiers, vk);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            if (IsShortcutEditing || LocalClip.App.Services.FlickDialog.IsOpen) { handled = true; return IntPtr.Zero; }
            int id = wParam.ToInt32();
            if (id == _toggleId)
            {
                OnCaptureToggleHotkeyPressed?.Invoke();
                handled = true;
            }
            else if (id == _saveId)
            {
                OnSaveReplayHotkeyPressed?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isShortcutEditing = false;
        ClearRegistrations();
        _source?.RemoveHook(HwndHook);
        _source = null;
        _hwnd = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }
}
