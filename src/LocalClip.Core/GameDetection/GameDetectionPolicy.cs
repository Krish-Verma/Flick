namespace LocalClip.Core.GameDetection;

public sealed record ApprovedGameProfile
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string ExecutablePath { get; init; } = "";
    public bool Enabled { get; init; }
    public string QualityPreset { get; init; } = "Balanced";
    public int ReplayDurationSeconds { get; init; } = 30;
    public bool AllowMicrophone { get; init; }
    public override string ToString() => Name;
}

public sealed record GameProcessIdentity(string ExecutablePath, uint Pid, ulong CreationFileTime)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Key => ExecutablePath.ToUpperInvariant() + "|" + Pid + "|" + CreationFileTime;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsComplete => Pid != 0 && CreationFileTime != 0 &&
        GameDetectionPolicy.TryNormalizePath(ExecutablePath, out _);
    public bool Matches(GameProcessIdentity other) => Pid == other.Pid && CreationFileTime == other.CreationFileTime &&
        string.Equals(ExecutablePath, other.ExecutablePath, StringComparison.OrdinalIgnoreCase);
}

public sealed record GameWindowObservation(GameProcessIdentity Process, ulong Hwnd, string Title,
    string WindowClass, bool Visible, bool Minimized, bool Cloaked, bool ToolWindow,
    int Width, int Height, bool IdentityComplete = true, bool Elevated = false)
{
    public bool Eligible => IdentityComplete && !Elevated && Process.IsComplete && Hwnd != 0 &&
        Visible && !Minimized && !Cloaked && !ToolWindow && Width >= 100 && Height >= 100 &&
        !string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(WindowClass) &&
        WindowClass is not ("Progman" or "WorkerW" or "Shell_TrayWnd");
}

public enum GameScanStatus { Complete, Unavailable, Truncated }
public sealed record GameWindowSnapshot(GameScanStatus Status, IReadOnlyList<GameWindowObservation> Windows);
public enum GameDetectionReason { Disabled, NoProfiles, InvalidProfiles, Unavailable, NoMatch, Ineligible, Ambiguous, Suppressed, Eligible }
public sealed record GameDetectionDecision(GameDetectionReason Reason, ApprovedGameProfile? Profile = null,
    GameWindowObservation? Window = null)
{
    public string StatusText => Reason switch
    {
        GameDetectionReason.Disabled => "Automatic game replay is off.",
        GameDetectionReason.NoProfiles => "Approve a running game in Settings to enable automatic replay.",
        GameDetectionReason.InvalidProfiles => "Review the approved game profiles before enabling automatic replay.",
        GameDetectionReason.Unavailable => "Game detection is unavailable. Automatic recording is paused.",
        GameDetectionReason.NoMatch => "Waiting for an approved game.",
        GameDetectionReason.Ineligible => "Approved game is minimized, hidden, elevated or unavailable.",
        GameDetectionReason.Ambiguous => "More than one approved game or window is open. Start replay manually.",
        GameDetectionReason.Suppressed => "Automatic replay is paused for this game session. Start manually or restart the game.",
        _ => "Approved game ready for automatic replay."
    };
}

/// <summary>Pure policy: no process, window, filesystem, clock or capture access.</summary>
public static class GameDetectionPolicy
{
    public const int MaximumProfiles = 32;

    // Approval is a local executable location, not a cryptographic attestation.
    // Only observed ordinary Win32 drive paths are supported; no expansion,
    // relative paths, wildcards, device names or network locations are guessed.
    public static bool TryNormalizePath(string? value, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32767 || value.IndexOf('\0') >= 0 || value.Any(char.IsControl)) return false;
        var candidate = value.Replace('/', '\\');
        if (candidate.Length < 7 || !char.IsAsciiLetter(candidate[0]) || candidate[1] != ':' || candidate[2] != '\\' ||
            candidate[3..].IndexOfAny([':', '*', '?', '"', '<', '>', '|']) >= 0 ||
            !candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (string part in candidate[3..].Split('\\'))
            if (part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.')) return false;
        path = char.ToUpperInvariant(candidate[0]) + candidate[1..];
        return true;
    }

    public static bool ValidateProfiles(IReadOnlyList<ApprovedGameProfile>? profiles, out string error)
    {
        error = "";
        if (profiles == null || profiles.Count > MaximumProfiles) { error = "Keep at most 32 approved games."; return false; }
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<Guid>();
        foreach (var profile in profiles)
        {
            if (profile == null || profile.Id == Guid.Empty || !ids.Add(profile.Id) ||
                string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 120 || profile.Name.Any(char.IsControl) ||
                !TryNormalizePath(profile.ExecutablePath, out string path) || !paths.Add(path) ||
                profile.QualityPreset is not ("Light" or "Balanced" or "Smooth") || profile.ReplayDurationSeconds is < 15 or > 120)
            { error = "Each game needs a unique observed executable path, a name and a valid replay profile."; return false; }
        }
        return true;
    }

    public static bool ShouldScan(bool enabled, IReadOnlyList<ApprovedGameProfile>? profiles) =>
        enabled && ValidateProfiles(profiles, out _) && profiles!.Any(profile => profile.Enabled);

    public static GameDetectionDecision Evaluate(bool enabled, IReadOnlyList<ApprovedGameProfile>? profiles,
        GameWindowSnapshot snapshot, uint selfPid, string selfExecutablePath,
        IReadOnlyList<GameProcessIdentity>? suppressed = null)
    {
        if (!enabled) return new(GameDetectionReason.Disabled);
        if (!ValidateProfiles(profiles, out _)) return new(GameDetectionReason.InvalidProfiles);
        var approved = profiles!.Where(profile => profile.Enabled).ToArray();
        if (approved.Length == 0) return new(GameDetectionReason.NoProfiles);
        if (snapshot.Status != GameScanStatus.Complete) return new(GameDetectionReason.Unavailable);
        var matches = snapshot.Windows.Where(window => window.Process.Pid != selfPid &&
            !string.Equals(window.Process.ExecutablePath, selfExecutablePath, StringComparison.OrdinalIgnoreCase) &&
            window.IdentityComplete && window.Process.IsComplete && approved.Any(profile =>
                string.Equals(profile.ExecutablePath.Replace('/', '\\'), window.Process.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (matches.Length == 0) return new(GameDetectionReason.NoMatch);
        // Even a minimized second approved process makes automatic choice ambiguous.
        if (matches.Select(window => window.Process.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            return new(GameDetectionReason.Ambiguous);
        var eligible = matches.Where(window => window.Eligible).GroupBy(window => window.Hwnd).Select(group => group.First()).ToArray();
        if (eligible.Length == 0) return new(GameDetectionReason.Ineligible);
        if (eligible.Length != 1) return new(GameDetectionReason.Ambiguous);
        var candidate = eligible[0];
        if (suppressed?.Any(process => process.Matches(candidate.Process)) == true)
            return new(GameDetectionReason.Suppressed);
        return new(GameDetectionReason.Eligible, approved.Single(profile =>
            string.Equals(profile.ExecutablePath.Replace('/', '\\'), candidate.Process.ExecutablePath, StringComparison.OrdinalIgnoreCase)), candidate);
    }
}

public enum GameProcessPresence { Alive, Exited, Unknown }
public interface IGameWindowEnumerator
{
    GameWindowSnapshot Scan(CancellationToken cancellationToken);
    GameProcessPresence Probe(GameProcessIdentity process);
    GameWindowObservation? Revalidate(GameWindowObservation window);
}

public static class GameDetectionScanner
{
    // This guard is intentionally outside the native boundary: disabled and
    // empty/invalid configurations cannot even call an injected enumerator.
    public static GameWindowSnapshot ScanIfEnabled(bool enabled, IReadOnlyList<ApprovedGameProfile>? profiles,
        IGameWindowEnumerator enumerator, CancellationToken cancellationToken) =>
        GameDetectionPolicy.ShouldScan(enabled, profiles) ? enumerator.Scan(cancellationToken) :
            new(GameScanStatus.Complete, Array.Empty<GameWindowObservation>());
}
