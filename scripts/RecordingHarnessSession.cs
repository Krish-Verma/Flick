using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Qualification-only bridge from SSH Session 0 to the same owner's existing
// desktop session. It never logs on, changes a token, adjusts privileges or ACLs,
// registers a task/service, or falls back to alternate credentials.
public sealed class RecordingHarnessSession : IDisposable
{
    private IntPtr process;
    public uint ProcessId { get; private set; }
    public uint SessionId { get; private set; }
    private RecordingHarnessSession() { }

    public static RecordingHarnessSession Start(uint explorerPid, uint expectedSession, string expectedSid,
        string application, string commandLine, string workingDirectory)
    {
        IntPtr explorer = IntPtr.Zero, token = IntPtr.Zero, primary = IntPtr.Zero, environment = IntPtr.Zero;
        PROCESS_INFORMATION child = new PROCESS_INFORMATION();
        try
        {
            explorer = OpenProcess(0x1000, false, explorerPid); // QUERY_LIMITED_INFORMATION
            Check(explorer != IntPtr.Zero, "Open existing desktop process");
            StringBuilder image = new StringBuilder(32768);
            uint imageLength = (uint)image.Capacity;
            Check(QueryFullProcessImageName(explorer, 0, image, ref imageLength), "Read retained desktop process image");
            string expectedImage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            Check(String.Equals(Path.GetFullPath(image.ToString()), Path.GetFullPath(expectedImage), StringComparison.OrdinalIgnoreCase),
                "Retained process is not the observed Windows Explorer");
            Check(OpenProcessToken(explorer, 0x000B, out token), "Open existing owner token");
            Check(TokenSid(token) == expectedSid, "Desktop token belongs to a different owner");
            uint session = TokenSession(token);
            Check(session != 0 && session == expectedSession, "Desktop token session changed or is not interactive");
            Check(DuplicateTokenEx(token, 0x000B, IntPtr.Zero, 2, 1, out primary), "Duplicate owner primary token");
            Check(TokenSid(primary) == expectedSid && TokenSession(primary) == session, "Duplicated token identity changed");
            Check(CreateEnvironmentBlock(out environment, primary, false), "Create owner environment");
            STARTUPINFO startup = new STARTUPINFO();
            startup.cb = Marshal.SizeOf(typeof(STARTUPINFO));
            startup.lpDesktop = "winsta0\\default";
            // Explicit application path; no inherited handles. The harness owns
            // its helper Job inside the interactive session, not Session 0.
            Check(CreateProcessAsUser(primary, application, new StringBuilder(commandLine),
                IntPtr.Zero, IntPtr.Zero, false, 0x08000404, environment,
                workingDirectory, ref startup, out child), "Create owned interactive harness");
            uint childSession;
            Check(ProcessIdToSessionId(child.dwProcessId, out childSession) && childSession == session,
                "Owned process started in the wrong session");
            Check(ResumeThread(child.hThread) != UInt32.MaxValue, "Resume owned harness");
            RecordingHarnessSession result = new RecordingHarnessSession();
            result.process = child.hProcess;
            result.ProcessId = child.dwProcessId;
            result.SessionId = session;
            child.hProcess = IntPtr.Zero;
            return result;
        }
        finally
        {
            // Any unreturned child is still suspended or has failed identity
            // admission. This retained handle can never refer to another PID.
            Exception cleanupError = null;
            if (child.hProcess != IntPtr.Zero)
            {
                try { TerminateOwnedAndWait(child.hProcess, 125); }
                catch (Exception error) { cleanupError = new InvalidOperationException("Unreturned owned harness PID " + child.dwProcessId + " exit is unconfirmed.", error); }
                finally { CloseHandle(child.hProcess); }
            }
            if (child.hThread != IntPtr.Zero) CloseHandle(child.hThread);
            if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
            if (primary != IntPtr.Zero) CloseHandle(primary);
            if (token != IntPtr.Zero) CloseHandle(token);
            if (explorer != IntPtr.Zero) CloseHandle(explorer);
            if (cleanupError != null) throw cleanupError;
        }
    }

    public bool HasExited { get { uint value = WaitForSingleObject(process, 0); Check(value != UInt32.MaxValue, "Query owned process"); return value == 0; } }
    public uint ExitCode { get { Check(HasExited, "Owned process is still running"); uint code; Check(GetExitCodeProcess(process, out code), "Read owned exit code"); return code; } }
    public void StopOwned() { TerminateOwnedAndWait(process, 124); }
    public void Dispose() { if (process != IntPtr.Zero) { CloseHandle(process); process = IntPtr.Zero; } }

    private static void TerminateOwnedAndWait(IntPtr handle, uint code)
    {
        if (WaitForSingleObject(handle, 0) == 0) return;
        Check(TerminateProcess(handle, code) || WaitForSingleObject(handle, 0) == 0, "Terminate exact owned harness");
        if (WaitForSingleObject(handle, 5000) != 0)
            throw new InvalidOperationException("Exact owned harness exit was not confirmed within five seconds.");
    }

    private static uint TokenSession(IntPtr token)
    {
        int length;
        IntPtr value = Marshal.AllocHGlobal(4);
        try { Check(GetTokenInformation(token, 12, value, 4, out length), "Read token session"); return unchecked((uint)Marshal.ReadInt32(value)); }
        finally { Marshal.FreeHGlobal(value); }
    }
    private static string TokenSid(IntPtr token)
    {
        int length;
        GetTokenInformation(token, 1, IntPtr.Zero, 0, out length);
        Check(length > 0 && length < 65536, "Read token user size");
        IntPtr value = Marshal.AllocHGlobal(length), sidString = IntPtr.Zero;
        try
        {
            Check(GetTokenInformation(token, 1, value, length, out length), "Read token user");
            Check(ConvertSidToStringSid(Marshal.ReadIntPtr(value), out sidString), "Read owner SID");
            return Marshal.PtrToStringUni(sidString);
        }
        finally { if (sidString != IntPtr.Zero) LocalFree(sidString); Marshal.FreeHGlobal(value); }
    }
    private static void Check(bool ok, string operation)
    { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), operation); }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public uint dwProcessId, dwThreadId; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder image, ref uint length);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attributes, int level, int type, out IntPtr primary);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int kind, IntPtr value, int size, out int length);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ConvertSidToStringSid(IntPtr sid, out IntPtr value);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcessAsUser(IntPtr token, string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref STARTUPINFO startup, out PROCESS_INFORMATION process);
    [DllImport("userenv.dll", SetLastError = true)] private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);
    [DllImport("userenv.dll")] private static extern bool DestroyEnvironmentBlock(IntPtr environment);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ProcessIdToSessionId(uint pid, out uint session);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr value);
}
