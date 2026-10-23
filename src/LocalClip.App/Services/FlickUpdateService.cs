using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using LocalClip.Core.Updates;

namespace LocalClip.App.Services;

/// <summary>
/// User-initiated updates only. GitHub checksums catch corruption; Windows trust
/// is checked separately because a checksum does not authenticate a publisher.
/// </summary>
public sealed class FlickUpdateService
{
    public const string ReleasesUrl = "https://github.com/Krish-Verma/Flick/releases";
    public static string CurrentVersion =>
        typeof(FlickUpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "0.0.0";

    public static string DownloadDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flick", "Updates");

    public static async Task<PreparedUpdate> PrepareInstallationAsync(
        DownloadedUpdate update, CancellationToken cancellationToken) =>
        await Task.Run(() => PrepareInstallation(update, cancellationToken), cancellationToken);

    private static PreparedUpdate PrepareInstallation(DownloadedUpdate update, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReleaseVersion.TryParse(update.Release.Tag, out var releaseVersion) ||
            releaseVersion.Text != update.Release.Version)
            throw new InvalidDataException("The update release version is invalid. Check for updates again.");
        string path = Path.GetFullPath(update.FilePath);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(DownloadDirectory)) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(path), "Flick-Setup.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update is outside Flick's update download folder. Check for updates again.");

        // Hold a deny-write/delete handle through Process.Start: verification
        // and launch must refer to the same bytes, even after a consent dialog.
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (stream.Length != update.Release.InstallerSize)
                throw new InvalidDataException("The downloaded installer size changed. Download the update again.");
            byte[] actual = SHA256.HashData(stream);
            byte[] expected;
            try { expected = Convert.FromHexString(update.Sha256); }
            catch (FormatException) { throw new InvalidDataException("The update checksum is invalid. Download the update again."); }
            if (expected.Length != 32 || !CryptographicOperations.FixedTimeEquals(actual, expected))
                throw new InvalidDataException("The downloaded installer changed. Download the update again.");
            cancellationToken.ThrowIfCancellationRequested();
            int trust = VerifyAuthenticode(path);
            bool unsigned = trust == unchecked((int)0x800B0100); // TRUST_E_NOSIGNATURE only.
            bool preview = releaseVersion.IsPrerelease;
            if (trust != 0 && !(unsigned && preview))
                throw new InvalidDataException(unsigned
                    ? "This stable update is unsigned. Flick will only install a stable release with a trusted Windows signature."
                    : "Windows could not verify this installer's signature. Flick will not run it; download a trusted release later.");
            cancellationToken.ThrowIfCancellationRequested();
            return new PreparedUpdate(path, stream, unsigned, expected);
        }
        catch { stream.Dispose(); throw; }
    }

    public static void DiscardDownload(DownloadedUpdate update)
    {
        try
        {
            string path = Path.GetFullPath(update.FilePath);
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(DownloadDirectory)) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(path), "Flick-Setup.exe", StringComparison.OrdinalIgnoreCase)) return;
            File.Delete(path);
            // Never recursively remove a folder: only this page's installer is ours.
            string? folder = Path.GetDirectoryName(path);
            if (folder != null && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    public static void LaunchInstaller(PreparedUpdate update)
    {
        if (!FlickUninstallLauncher.IsInstalled)
            throw new InvalidOperationException("This copy is portable. Open the release page and update its extracted folder manually after quitting Flick.");
        update.ReverifyChecksum();
        var start = new ProcessStartInfo
        {
            FileName = update.FilePath,
            WorkingDirectory = Path.GetDirectoryName(update.FilePath)!,
            UseShellExecute = false
        };
        start.ArgumentList.Add("/SILENT");
        start.ArgumentList.Add("/NORESTART");
        start.ArgumentList.Add("/FLICKUPDATE=1");
        start.ArgumentList.Add("/FLICKWAITPID=" + Environment.ProcessId);
        start.ArgumentList.Add("/DIR=" + Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        using var process = Process.Start(start);
        if (process is null) throw new InvalidOperationException("Windows did not start the update installer.");
    }

    public static void OpenReleasePage(string? releaseUrl = null)
    {
        // Use only release URLs from the fixed repository, never arbitrary URI
        // protocols or a value from user-editable settings.
        if (!Uri.TryCreate(releaseUrl ?? ReleasesUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com" ||
            (uri.AbsolutePath != "/Krish-Verma/Flick/releases" &&
             !uri.AbsolutePath.StartsWith("/Krish-Verma/Flick/releases/tag/", StringComparison.Ordinal)))
            throw new InvalidDataException("The update release link is invalid.");
        using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    public sealed class PreparedUpdate : IDisposable
    {
        private readonly FileStream _lockedFile;
        private readonly byte[] _expectedChecksum;
        internal PreparedUpdate(string path, FileStream lockedFile, bool unsigned, byte[] expectedChecksum)
        { FilePath = path; _lockedFile = lockedFile; IsUnsignedPreview = unsigned; _expectedChecksum = expectedChecksum; }
        internal void ReverifyChecksum()
        {
            _lockedFile.Position = 0;
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(_lockedFile), _expectedChecksum))
                throw new InvalidDataException("The update changed after verification. Download it again.");
        }
        public string FilePath { get; }
        public bool IsUnsignedPreview { get; }
        public void Dispose() => _lockedFile.Dispose();
    }

    private static int VerifyAuthenticode(string path)
    {
        var file = new WinTrustFileInfo { Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = path };
        IntPtr filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(file, filePointer, false);
            var data = new WinTrustData
            {
                Size = (uint)Marshal.SizeOf<WinTrustData>(), UiChoice = 2, // WTD_UI_NONE
                RevocationChecks = 1, // WTD_REVOKE_WHOLECHAIN
                UnionChoice = 1, FileInfo = filePointer,
                StateAction = 1, // WTD_STATEACTION_VERIFY
                ProviderFlags = 0x80 // WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT
            };
            var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
            try { return WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
            finally { data.StateAction = 2; _ = WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(filePointer);
            Marshal.FreeHGlobal(filePointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref WinTrustData data);
}
