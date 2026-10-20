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

    private int _toggleId = 0;
    private int _saveId = 0;

    public void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(HwndHook);
    }

    public bool RegisterToggleHotkey(string hotkeyStr, out string errorMessage)
    {
        errorMessage = "";
        if (_toggleId != 0)
        {
            UnregisterHotKey(_hwnd, _toggleId);
            _toggleId = 0;
        }

        if (!ParseHotkey(hotkeyStr, out uint modifiers, out uint vk))
        {
            errorMessage = $"Invalid hotkey format: {hotkeyStr}";
            return false;
        }

        _toggleId = ++_currentId;
        if (!RegisterHotKey(_hwnd, _toggleId, modifiers | MOD_NOREPEAT, vk))
        {
            int err = Marshal.GetLastWin32Error();
            errorMessage = $"Failed to register '{hotkeyStr}' (Error code {err}). It may be in use by another application.";
            _toggleId = 0;
            return false;
        }

        return true;
    }

    public bool RegisterSaveHotkey(string hotkeyStr, out string errorMessage)
    {
        errorMessage = "";
        if (_saveId != 0)
        {
            UnregisterHotKey(_hwnd, _saveId);
            _saveId = 0;
        }

        if (!ParseHotkey(hotkeyStr, out uint modifiers, out uint vk))
        {
            errorMessage = $"Invalid hotkey format: {hotkeyStr}";
            return false;
        }

        _saveId = ++_currentId;
        if (!RegisterHotKey(_hwnd, _saveId, modifiers | MOD_NOREPEAT, vk))
        {
            int err = Marshal.GetLastWin32Error();
            errorMessage = $"Failed to register '{hotkeyStr}' (Error code {err}). It may be in use by another application.";
            _saveId = 0;
            return false;
        }

        return true;
    }

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
        return vk > 0;
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
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
        if (_toggleId != 0 && _hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, _toggleId);
            _toggleId = 0;
        }
        if (_saveId != 0 && _hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, _saveId);
            _saveId = 0;
        }
        _source?.RemoveHook(HwndHook);
        _source = null;
        GC.SuppressFinalize(this);
    }
}
