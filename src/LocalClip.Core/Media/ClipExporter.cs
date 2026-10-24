using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using LocalClip.Core.Process;

namespace LocalClip.Core.Media;

public class ExportProgress
{
    public double Percent { get; set; }
    public string Stage { get; set; } = "";
    public int Attempt { get; set; }
}

public class ExportResult
{
    public bool Success { get; set; }
    public string ExportFilePath { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public double DurationSeconds { get; set; }
    public string Fingerprint { get; set; } = "";
    public int AttemptsUsed { get; set; }
    public string ErrorMessage { get; set; } = "";
}

public class ClipExporter
{
    private readonly string _ffmpegPath;
    private readonly MediaProber _prober;
    private static readonly SemaphoreSlim ExportGate = new(1, 1);

    public ClipExporter(string? ffmpegPath = null, MediaProber? prober = null)
    {
        string baseDir = AppContext.BaseDirectory;
        _ffmpegPath = ffmpegPath ?? FindExecutable("ffmpeg.exe", baseDir);
        _prober = prober ?? new MediaProber(ffmpegPath: _ffmpegPath);
    }

    private static string FindExecutable(string name, string baseDir)
    {
        string probe1 = Path.Combine(baseDir, name);
        string probe2 = Path.Combine(baseDir, "tools", "ffmpeg", name);
        string devPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "tools", "ffmpeg", name));

        if (File.Exists(probe1)) return probe1;
        if (File.Exists(probe2)) return probe2;
        if (File.Exists(devPath)) return devPath;
        return name;
    }

    public async Task<ExportResult> ExportForDiscordAsync(
        ClipMetadata clip,
        AppSettings settings,
        IProgress<ExportProgress>? progress = null,
        CancellationToken ct = default)
    {
        await ExportGate.WaitAsync(ct);
        string? temporaryFileToDelete = null;
        try
        {
        if (!ExportAspectPresets.IsValid(clip.ExportAspectPreset))
            return new ExportResult { Success = false, ErrorMessage = "Choose a supported export aspect preset." };
        if (!File.Exists(clip.OriginalFilePath))
        {
            return new ExportResult { Success = false, ErrorMessage = "Original file does not exist: " + clip.OriginalFilePath };
        }

        // Validate trim
        var trimRes = TrimValidator.Validate(clip.TrimStartSeconds, clip.TrimEndSeconds, clip.DurationSeconds);
        if (!trimRes.IsValid)
        {
            return new ExportResult { Success = false, ErrorMessage = trimRes.ErrorMessage };
        }

        double startSec = trimRes.ValidatedStart;
        double endSec = trimRes.ValidatedEnd;
        double duration = trimRes.ValidatedDuration;

        string fingerprint = ExportFingerprint.Compute(
            clip.OriginalFilePath,
            startSec,
            endSec,
            clip.AudioTracks,
            settings.DiscordTargetSizeMb,
            clip.ExportAspectPreset
        );

        long targetCapBytes = BitrateBudgetCalculator.TargetMbToBytes(settings.DiscordTargetSizeMb);

        string exportsDir = settings.ExportsFolder;
        if (string.IsNullOrWhiteSpace(exportsDir))
            exportsDir = Path.Combine(Path.GetDirectoryName(clip.OriginalFilePath)!, "..", "Exports");
        string finalExportPath = Path.Combine(exportsDir,
            ShareExportNaming.ForOriginal(clip.OriginalFilePath, settings.DiscordTargetSizeMb, clip.ExportAspectPreset));

        // Never reuse an old export at a different destination or one with a
        // legacy filename that another imported clip may have overwritten.
        if (!string.IsNullOrEmpty(clip.ExportedFilePath) &&
            File.Exists(clip.ExportedFilePath) &&
            string.Equals(Path.GetFullPath(clip.ExportedFilePath),
                Path.GetFullPath(finalExportPath), StringComparison.OrdinalIgnoreCase) &&
            clip.LastExportFingerprint == fingerprint &&
            clip.IsExportReady)
        {
            long existingSize = new FileInfo(clip.ExportedFilePath).Length;
            if (existingSize <= targetCapBytes &&
                IsValidShareMedia(await ProbeExportAsync(clip.ExportedFilePath, ct), duration, clip.ExportAspectPreset) &&
                await VerifyFullDecodeAsync(clip.ExportedFilePath, ct))
            {
                return new ExportResult
                {
                    Success = true,
                    ExportFilePath = clip.ExportedFilePath,
                    FileSizeBytes = existingSize,
                    DurationSeconds = duration,
                    Fingerprint = fingerprint,
                    AttemptsUsed = 0
                };
            }
        }

        Directory.CreateDirectory(exportsDir);
        // Keep temporary export on the final destination volume for atomic promotion.
        string tempExportPath = Path.Combine(exportsDir, $".flick-export-{Guid.NewGuid():N}.tmp.mp4");
        temporaryFileToDelete = tempExportPath;

        // Bounded retries (up to 3 attempts)
        const int MAX_ATTEMPTS = 3;
        long previousOvershoot = 0;
        int attempt = 0;
        bool hardwareEncodeFailed = false;

        while (attempt < MAX_ATTEMPTS)
        {
            ct.ThrowIfCancellationRequested();
            attempt++;

            progress?.Report(new ExportProgress
            {
                Percent = (attempt - 1) * 30.0 + 5.0,
                Stage = $"Calculating bitrate budget (Attempt {attempt}/{MAX_ATTEMPTS})...",
                Attempt = attempt
            });

            var budget = BitrateBudgetCalculator.Calculate(
                settings.DiscordTargetSizeMb,
                duration,
                clip.Width > 0 ? clip.Width : 1920,
                clip.Height > 0 ? clip.Height : 1080,
                retryAttempt: attempt - 1,
                previousOvershootBytes: previousOvershoot
            );
            (budget.RecommendedWidth, budget.RecommendedHeight) = ExportAspectPresets.Canvas(
                clip.ExportAspectPreset, budget.RecommendedWidth, budget.RecommendedHeight);
            budget.AspectPreset = clip.ExportAspectPreset;

            // Audio filter graph for mixing enabled tracks
            string audioFilter = BuildAudioMixFilter(clip.AudioTracks);

            // Execute encode
            bool useSoftware = hardwareEncodeFailed ||
                (attempt == MAX_ATTEMPTS && settings.UseTwoPassSoftwareIfHardwareOvershoots);
            progress?.Report(new ExportProgress
            {
                Percent = (attempt - 1) * 30.0 + 15.0,
                Stage = $"Encoding MP4 with {(useSoftware ? "CPU compatibility" : "NVIDIA hardware")} ({budget.RecommendedWidth}x{budget.RecommendedHeight} @ {budget.VideoBitrateBps / 1000} kbps)...",
                Attempt = attempt
            });

            bool encodeSuccess = await RunFfmpegEncodeAsync(
                clip.OriginalFilePath,
                tempExportPath,
                startSec,
                duration,
                budget,
                audioFilter,
                useSoftware,
                ct
            );
            ct.ThrowIfCancellationRequested();

            if (!encodeSuccess || !File.Exists(tempExportPath))
            {
                // NVENC may be registered in FFmpeg on a PC with no NVIDIA
                // encoder. After an actual failed attempt, use the bounded CPU
                // path for all remaining retries, independently of size overshoot.
                if (!useSoftware) hardwareEncodeFailed = true;
                try { File.Delete(tempExportPath); } catch { }
                if (attempt < MAX_ATTEMPTS) continue;
                return new ExportResult
                {
                    Success = false,
                    ErrorMessage = $"FFmpeg encoding failed after {attempt} attempts.",
                    AttemptsUsed = attempt
                };
            }

            long outputBytes = new FileInfo(tempExportPath).Length;

            // Verify size cap
            if (outputBytes > targetCapBytes)
            {
                previousOvershoot = outputBytes - targetCapBytes;
                try { File.Delete(tempExportPath); } catch { }

                if (attempt < MAX_ATTEMPTS)
                {
                    continue; // Retry with penalizing budget
                }
                else
                {
                    return new ExportResult
                    {
                        Success = false,
                        ErrorMessage = $"Export exceeded {settings.DiscordTargetSizeMb} MB cap ({outputBytes / 1_000_000.0:F2} MB) after {MAX_ATTEMPTS} attempts.",
                        AttemptsUsed = attempt
                    };
                }
            }

            // Probe output verification
            progress?.Report(new ExportProgress
            {
                Percent = 85.0,
                Stage = "Validating exported streams and duration...",
                Attempt = attempt
            });

            var probeOut = await ProbeExportAsync(tempExportPath, ct);
            if (!IsValidShareMedia(probeOut, duration, clip.ExportAspectPreset) ||
                clip.ExportAspectPreset != ExportAspectPresets.Original &&
                (probeOut.Width != budget.RecommendedWidth || probeOut.Height != budget.RecommendedHeight))
            {
                try { File.Delete(tempExportPath); } catch { }
                return new ExportResult
                {
                    Success = false,
                    ErrorMessage = "Exported file validation failed: " + probeOut.ErrorMessage,
                    AttemptsUsed = attempt
                };
            }

            // Full decode check for corruption
            progress?.Report(new ExportProgress
            {
                Percent = 92.0,
                Stage = "Running full decode corruption check...",
                Attempt = attempt
            });

            bool decodeClean = await VerifyFullDecodeAsync(tempExportPath, ct);
            if (!decodeClean)
            {
                try { File.Delete(tempExportPath); } catch { }
                return new ExportResult
                {
                    Success = false,
                    ErrorMessage = "Exported file failed full decode corruption check.",
                    AttemptsUsed = attempt
                };
            }

            // Atomically promote temporary file to final destination
            progress?.Report(new ExportProgress
            {
                Percent = 98.0,
                Stage = "Finalizing share file...",
                Attempt = attempt
            });

            try
            {
                File.Move(tempExportPath, finalExportPath, overwrite: true);
            }
            catch (Exception ex)
            {
                return new ExportResult
                {
                    Success = false,
                    ErrorMessage = "Failed to promote export artifact to final path: " + ex.Message,
                    AttemptsUsed = attempt
                };
            }

            long finalSize = new FileInfo(finalExportPath).Length;
            return new ExportResult
            {
                Success = true,
                ExportFilePath = finalExportPath,
                FileSizeBytes = finalSize,
                DurationSeconds = duration,
                Fingerprint = fingerprint,
                AttemptsUsed = attempt
            };
        }

        return new ExportResult
        {
            Success = false,
            ErrorMessage = "Export aborted after maximum retry attempts.",
            AttemptsUsed = attempt
        };
        }
        finally
        {
            if (!string.IsNullOrEmpty(temporaryFileToDelete))
                try { File.Delete(temporaryFileToDelete); } catch { }
            ExportGate.Release();
        }
    }

    private static bool IsValidShareMedia(ProbeResult media, double requestedDuration, string aspectPreset) =>
        media.IsValid &&
        media.VideoCodec.Equals("h264", StringComparison.OrdinalIgnoreCase) &&
        media.AudioTracks.Count == 1 &&
        media.AudioTracks[0].Codec.Equals("aac", StringComparison.OrdinalIgnoreCase) &&
        Math.Abs(media.DurationSeconds - requestedDuration) <= 1.0 &&
        ExportAspectPresets.MatchesDimensions(aspectPreset, media.Width, media.Height);

    public static string BuildAudioMixFilter(List<AudioTrackInfo>? tracks)
    {
        var activeTracks = LocalClip.Core.Audio.AudioTrackRoles.EffectiveMix(tracks);

        if (activeTracks.Count == 0)
        {
            // All muted: generate silent stereo
            return "anullsrc=r=48000:cl=stereo[outa]";
        }

        if (activeTracks.Count == 1)
        {
            var t = activeTracks[0];
            string gainStr = t.Gain.ToString("F2", CultureInfo.InvariantCulture);
            return $"[0:a:{t.TrackIndex}]volume={gainStr},alimiter=limit=0.98[outa]";
        }

        var sb = new StringBuilder();
        for (int i = 0; i < activeTracks.Count; i++)
        {
            var t = activeTracks[i];
            string gainStr = t.Gain.ToString("F2", CultureInfo.InvariantCulture);
            sb.Append($"[0:a:{t.TrackIndex}]volume={gainStr}[a{i}];");
        }

        for (int i = 0; i < activeTracks.Count; i++)
        {
            sb.Append($"[a{i}]");
        }
        sb.Append($"amix=inputs={activeTracks.Count}:normalize=0,alimiter=limit=0.98[outa]");
        return sb.ToString();
    }

    protected virtual Task<ProbeResult> ProbeExportAsync(string filePath, CancellationToken ct) =>
        _prober.ProbeFileAsync(filePath, ct);

    protected virtual async Task<bool> RunFfmpegEncodeAsync(
        string inputPath,
        string outputPath,
        double startSec,
        double durationSec,
        BitrateBudget budget,
        string audioFilter,
        bool useSoftware,
        CancellationToken ct)
    {
        string startStr = startSec.ToString("F3", CultureInfo.InvariantCulture);
        string durStr = durationSec.ToString("F3", CultureInfo.InvariantCulture);

        string vEncoder = useSoftware ? "libx264 -preset fast -threads 4" : "h264_nvenc -preset p4 -tune hq";
        // Imported videos may be rotated or use odd coded dimensions. Preserve
        // their displayed aspect ratio and produce even YUV420-compatible output
        // rather than stretching a portrait frame or repeatedly failing x264.
        string scaleFilter = ExportAspectPresets.VideoFilter(budget.AspectPreset, budget.RecommendedWidth, budget.RecommendedHeight);

        string filterComplex = $"[0:v]{scaleFilter}[outv];{audioFilter}";

        var args = new StringBuilder();
        args.Append($"-hide_banner -loglevel error -nostats -y -ss {startStr} -t {durStr} -i \"{inputPath}\" ");
        args.Append($"-filter_complex \"{filterComplex}\" ");
        args.Append($"-map \"[outv]\" -map \"[outa]\" ");
        args.Append($"-t {durStr} -shortest ");
        args.Append($"-c:v {vEncoder} -b:v {budget.VideoBitrateBps} -maxrate {budget.VideoBitrateBps} -bufsize {budget.VideoBitrateBps * 2} ");
        args.Append($"-c:a aac -b:a {budget.AudioBitrateBps} -ar 48000 -ac 2 ");
        args.Append("-movflags +faststart ");
        args.Append($"\"{outputPath}\"");

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = args.ToString(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        using var proc = new System.Diagnostics.Process { StartInfo = psi };
        using var job = new JobObject();
        proc.Start();
        if (!job.AssignProcess(proc) && !proc.HasExited)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException("Failed to confine FFmpeg encode worker");
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        Task<string> stderr = proc.StandardError.ReadToEndAsync(linked.Token);
        using var registration = linked.Token.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
        });

        try
        {
            await proc.WaitForExitAsync(linked.Token);
            await stderr;
            return proc.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            ct.ThrowIfCancellationRequested();
            return false;
        }
    }

    protected virtual async Task<bool> VerifyFullDecodeAsync(string filePath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = $"-v error -i \"{filePath}\" -f null -",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        using var proc = new System.Diagnostics.Process { StartInfo = psi };
        using var job = new JobObject();
        proc.Start();
        if (!job.AssignProcess(proc) && !proc.HasExited)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException("Failed to confine FFmpeg validation worker");
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        using var registration = linked.Token.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
        });

        try
        {
            string err = await proc.StandardError.ReadToEndAsync(linked.Token);
            await proc.WaitForExitAsync(linked.Token);
            return proc.ExitCode == 0 && string.IsNullOrWhiteSpace(err);
        }
        catch
        {
            ct.ThrowIfCancellationRequested();
            return false;
        }
    }
}
