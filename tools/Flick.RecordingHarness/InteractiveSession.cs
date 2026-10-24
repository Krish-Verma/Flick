using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Flick.RecordingHarness;

internal static class InteractiveSession
{
    [DllImport("user32.dll")] private static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformationW(IntPtr handle, int index, out UserObjectFlags flags,
        uint length, out uint needed);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformationW(IntPtr handle, int index, StringBuilder name,
        uint length, out uint needed);
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);
    [StructLayout(LayoutKind.Sequential)] private struct UserObjectFlags
    {
        public int Inherit;
        public int Reserved;
        public uint Flags;
    }

    public static int Require()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows is required.");
        using var current = Process.GetCurrentProcess();
        int session = current.SessionId;
        if (session == 0 || !Environment.UserInteractive)
            throw new InvalidOperationException("Capture refused: use an unlocked interactive user session; Session 0 is not capture qualification.");
        IntPtr station = GetProcessWindowStation();
        if (station == IntPtr.Zero || !GetUserObjectInformationW(station, 1, out var flags,
                (uint)Marshal.SizeOf<UserObjectFlags>(), out _) || (flags.Flags & 1) == 0)
            throw new InvalidOperationException("Capture refused: current window station is not interactive.");
        // Opening the current input desktop requests read access only. Never switch,
        // unlock, change access controls, or attach to another user's desktop.
        IntPtr desktop = OpenInputDesktop(0, false, 0x0001);
        if (desktop == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Capture refused: interactive input desktop is unavailable.");
        try
        {
            if (!IsDefaultDesktop(desktop) || !IsDefaultDesktop(GetThreadDesktop(GetCurrentThreadId())))
                throw new InvalidOperationException("Capture refused: the current and input desktops must both be the normal Default desktop.");
        }
        finally { CloseDesktop(desktop); }
        return session;
    }

    private static bool IsDefaultDesktop(IntPtr desktop)
    {
        var name = new StringBuilder(256);
        return desktop != IntPtr.Zero && GetUserObjectInformationW(desktop, 2, name,
            checked((uint)name.Capacity * 2), out _) && name.ToString().Equals("Default", StringComparison.OrdinalIgnoreCase);
    }
}
