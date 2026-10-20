using System;
using System.IO;
using System.Text.Json.Serialization;

namespace LocalClip.Core.Models;

/// <summary>
/// User-authorized local library source. Files remain in their original location.
/// No vendor account, integration, watcher, or automatic bulk copy is required.
/// </summary>
public sealed class ConnectedClipFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IncludeSubfolders { get; set; } = true;
    public DateTime ConnectedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public bool IsAvailable => Directory.Exists(Path);

    [JsonIgnore]
    public string Status => IsAvailable ? "Connected" : "Folder Unavailable";
}

/// <summary>Bounded incremental progress, never a fabricated percentage.</summary>
public sealed class ClipImportProgress
{
    public int Examined { get; init; }
    public int TotalDiscovered { get; init; }
    public int Imported { get; init; }
    public int AlreadyIndexed { get; init; }
    public int Ignored { get; init; }
    public int Failed { get; init; }
}

public sealed class ClipImportSummary
{
    public int TotalDiscovered { get; init; }
    public int Imported { get; init; }
    public int AlreadyIndexed { get; init; }
    public int Ignored { get; init; }
    public int Failed { get; init; }
    public bool FolderUnavailable { get; init; }
    public bool ScanLimitReached { get; init; }
}
