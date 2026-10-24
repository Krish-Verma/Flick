using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace LocalClip.Core.GameDetection;

/// <summary>Ordinary read-only Win32 metadata. Never opens capture/audio, changes desktops or elevates.</summary>
public sealed class WindowsGameWindowEnumerator : IGameWindowEnumerator
{
    private const uint QueryAndSynchronize = 0x1000 | 0x100000;
    private const int MaximumWindows = 1024;
    public GameWindowSnapshot Scan(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return new(GameScanStatus.Unavailable, []);
        var windows = new List<GameWindowObservation>();
        int examined = 0;
        bool truncated = false;
        bool success = EnumWindows((hwnd, _) =>
        {
            if (cancellationToken.IsCancellationRequested) return false;
            if (++examined > MaximumWindows) { truncated = true; return false; }
            try { if (ReadWindow(hwnd) is { } observation) windows.Add(observation); }
            catch (Exception error) when (error is Win32Exception or ArgumentException or OverflowException) { }
            return true;
        }, IntPtr.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        return new(truncated ? GameScanStatus.Truncated : success ? GameScanStatus.Complete : GameScanStatus.Unavailable,
            windows.AsReadOnly());
    }

    public GameWindowObservation? Revalidate(GameWindowObservation window)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var current = ReadWindow(new IntPtr(unchecked((long)window.Hwnd)));
        return current != null && current.Process.Matches(window.Process) && current.Hwnd == window.Hwnd &&
            current.WindowClass == window.WindowClass ? current : null;
    }

    public GameProcessPresence Probe(GameProcessIdentity process)
    {
        if (!OperatingSystem.IsWindows() || !process.IsComplete) return GameProcessPresence.Unknown;
        IntPtr handle = OpenProcess(QueryAndSynchronize, false, process.Pid);
        if (handle == IntPtr.Zero) return Marshal.GetLastWin32Error() == 87 ? GameProcessPresence.Exited : GameProcessPresence.Unknown;
        try
        {
            uint waited = WaitForSingleObject(handle, 0);
            if (waited == 0) return GameProcessPresence.Exited;
            if (waited != 258 || !TryReadProcess(handle, process.Pid, out var actual)) return GameProcessPresence.Unknown;
            return actual.Matches(process) ? GameProcessPresence.Alive : GameProcessPresence.Exited;
        }
        finally { CloseHandle(handle); }
    }

    private static GameWindowObservation? ReadWindow(IntPtr hwnd)
    {
        if (GetAncestor(hwnd, 2) != hwnd || GetWindow(hwnd, 4) != IntPtr.Zero || !IsWindow(hwnd)) return null;
        if (GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0) return null;
        IntPtr process = OpenProcess(QueryAndSynchronize, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            if (WaitForSingleObject(process, 0) != 258 || !TryReadProcess(process, pid, out var identity)) return null;
            bool elevated = true;
            if (OpenProcessToken(process, 8, out IntPtr token))
            {
                try
                {
                    if (GetTokenInformation(token, 20, out int elevation, sizeof(int), out _)) elevated = elevation != 0;
                }
                finally { CloseHandle(token); }
            }
            int titleLength = GetWindowTextLengthW(hwnd);
            if (titleLength < 0 || titleLength > 32768) return null;
            var title = new StringBuilder(titleLength + 1);
            GetWindowTextW(hwnd, title, title.Capacity);
            var windowClass = new StringBuilder(256);
            if (GetClassNameW(hwnd, windowClass, windowClass.Capacity) == 0 || !GetWindowRect(hwnd, out var bounds)) return null;
            bool cloakKnown = DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0;
            if (GetWindowThreadProcessId(hwnd, out uint afterPid) == 0 || afterPid != pid || !IsWindow(hwnd) ||
                WaitForSingleObject(process, 0) != 258) return null;
            return new(identity, unchecked((ulong)hwnd.ToInt64()), title.ToString(), windowClass.ToString(),
                IsWindowVisible(hwnd), IsIconic(hwnd), cloaked != 0, (GetWindowLongPtrW(hwnd, -20).ToInt64() & 0x80) != 0,
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, cloakKnown, elevated);
        }
        finally { CloseHandle(process); }
    }

    private static bool TryReadProcess(IntPtr handle, uint pid, out GameProcessIdentity identity)
    {
        identity = new("", 0, 0);
        string? fullPath = null;
        for (int capacity = 512; capacity <= 32768; capacity *= 2)
        {
            var path = new StringBuilder(capacity);
            uint size = (uint)path.Capacity;
            if (QueryFullProcessImageNameW(handle, 0, path, ref size))
            {
                if (!GameDetectionPolicy.TryNormalizePath(path.ToString(), out fullPath)) return false;
                break;
            }
            if (Marshal.GetLastWin32Error() != 122) return false;
        }
        if (fullPath == null || !GetProcessTimes(handle, out var birth, out _, out _, out _)) return false;
        ulong created = ((ulong)birth.High << 32) | birth.Low;
        identity = new(fullPath, pid, created);
        return created != 0;
    }

    private delegate bool EnumCallback(IntPtr hwnd, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low, High; }
    [StructLayout(LayoutKind.Sequential)] private struct Rectangle { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLengthW(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder title, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hwnd, StringBuilder name, int size);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out Rectangle bounds);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out int value, int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(IntPtr process, out FileTime creation, out FileTime exit, out FileTime kernel, out FileTime user);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetTokenInformation(IntPtr token, int kind, out int value, int size, out int returned);
}
