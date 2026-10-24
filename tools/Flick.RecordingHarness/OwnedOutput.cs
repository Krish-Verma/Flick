using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flick.RecordingHarness;

internal sealed class OwnedOutput
{
    private static readonly JsonSerializerOptions Json = new()
    { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string Root { get; }
    public Guid RunId { get; } = Guid.NewGuid();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryW(string directory, IntPtr security);

    private OwnedOutput(string root) => Root = root;

    public static OwnedOutput Create(string root)
    {
        RefuseReparseComponents(Path.GetDirectoryName(root)!);
        if (!CreateDirectoryW(root, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot reserve a new qualification output directory.");
        var output = new OwnedOutput(root);
        output.WriteNew(".flick-recording-qualification.json", new
        {
            SchemaVersion = 1, output.RunId, Purpose = "SyntheticWindowNoAudio", CreatedUtc = DateTimeOffset.UtcNow
        });
        return output;
    }

    public static void RefuseReparseComponents(string path)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full)!;
        string current = root;
        foreach (string segment in full[root.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0) continue;
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Qualification paths cannot contain a reparse point.");
        }
    }

    public string NewDirectory(string relative)
    {
        string path = Resolve(relative);
        RefuseReparseComponents(Path.GetDirectoryName(path)!);
        if (!CreateDirectoryW(path, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Qualification case directory already exists or is unavailable.");
        return path;
    }

    public string Resolve(string relative)
    {
        if (Path.IsPathRooted(relative)) throw new ArgumentException("Use an owned relative path.");
        string path = Path.GetFullPath(Path.Combine(Root, relative));
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Evidence must remain inside the owned root.");
        return path;
    }

    public void WriteNew<T>(string relative, T value)
    {
        string path = Resolve(relative);
        RefuseReparseComponents(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, value, Json);
        stream.Flush(true);
    }

    public static async Task<string> HashAsync(string path, CancellationToken cancellationToken = default)
    {
        RefuseReparseComponents(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }
}
