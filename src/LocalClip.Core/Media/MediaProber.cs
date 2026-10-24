using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LocalClip.Core.Models;
using LocalClip.Core.Process;
using LocalClip.Core.Rules;

namespace LocalClip.Core.Media;

public sealed class MediaWorkerExitUnconfirmedException(string message) : IOException(message);

public class ProbeResult
{
    public double DurationSeconds { get; set; }
    public long FileSizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Fps { get; set; }
    public string VideoCodec { get; set; } = "";
    public List<AudioTrackInfo> AudioTracks { get; set; } = new();
    public bool IsValid { get; set; }
    public string ErrorMessage { get; set; } = "";
}

public class MediaProber
{
    private readonly string _ffprobePath;
    private readonly string _ffmpegPath;

    public MediaProber(string? ffprobePath = null, string? ffmpegPath = null)
    {
        string baseDir = AppContext.BaseDirectory;
        _ffprobePath = ffprobePath ?? FindExecutable("ffprobe.exe", baseDir);
        _ffmpegPath = ffmpegPath ?? FindExecutable("ffmpeg.exe", baseDir);
    }

    private static string FindExecutable(string name, string baseDir)
    {
        string probe1 = Path.Combine(baseDir, name);
        string probe2 = Path.Combine(baseDir, "tools", "ffmpeg", name);
        // Development builds: project bin/{config}/{tfm} is five levels below root.
        string devPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "tools", "ffmpeg", name));
        if (File.Exists(probe1)) return probe1;
        if (File.Exists(probe2)) return probe2;
        if (File.Exists(devPath)) return devPath;
        return name;
    }

    public async Task<ProbeResult> ProbeFileAsync(string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return new ProbeResult { IsValid = false, ErrorMessage = "File does not exist: " + filePath };
        }

        var psi = new ProcessStartInfo
        {
            FileName = _ffprobePath,
            Arguments = $"-v quiet -show_streams -show_format -print_format json \"{filePath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = new System.Diagnostics.Process { StartInfo = psi };
        using var job = new JobObject();
        proc.Start();
        string output;
        Task<string>? stderr = null;
        try
        {
            if (!job.AssignProcess(proc) && !proc.HasExited)
                throw new InvalidOperationException("Failed to confine ffprobe worker");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var interrupt = linked.Token.Register(() =>
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            });
            stderr = proc.StandardError.ReadToEndAsync(linked.Token);
            output = await proc.StandardOutput.ReadToEndAsync(linked.Token);
            await proc.WaitForExitAsync(linked.Token);
            _ = await stderr;
        }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            job.Dispose();
            try { await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (Exception) { throw new LocalClip.Core.Audio.MediaWorkerCleanupException(); }
            if (stderr != null) { try { await stderr.ConfigureAwait(false); } catch (Exception) { } }
        }

        if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            return new ProbeResult { IsValid = false, ErrorMessage = $"ffprobe returned error code {proc.ExitCode}" };
        }

        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            double duration = 0.0;
            long size = new FileInfo(filePath).Length;
            int width = 0;
            int height = 0;
            int fps = 30;
            string videoCodec = "";
            var audioTracks = new List<AudioTrackInfo>();

            if (root.TryGetProperty("format", out var fmt))
            {
                if (fmt.TryGetProperty("duration", out var durProp) && double.TryParse(durProp.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedDur))
                {
                    duration = parsedDur;
                }
            }

            if (root.TryGetProperty("streams", out var streams))
            {
                int audioIndex = 0;
                foreach (var stream in streams.EnumerateArray())
                {
                    string codecType = stream.TryGetProperty("codec_type", out var ct) ? ct.GetString() ?? "" : "";
                    if (codecType == "video" && width == 0)
                    {
                        (width, height) = ReadDisplayDimensions(stream);
                        videoCodec = stream.TryGetProperty("codec_name", out var cn) ? cn.GetString() ?? "" : "";

                        if (stream.TryGetProperty("r_frame_rate", out var rfr))
                        {
                            string[] parts = (rfr.GetString() ?? "30/1").Split('/');
                            if (parts.Length == 2 && double.TryParse(parts[0], out var num) && double.TryParse(parts[1], out var den) && den > 0)
                            {
                                fps = (int)Math.Round(num / den);
                            }
                        }
                    }
                    else if (codecType == "audio")
                    {
                        string title = "";
                        if (stream.TryGetProperty("tags", out var tags) && tags.TryGetProperty("title", out var tProp))
                        {
                            title = tProp.GetString() ?? "";
                        }
                        if (string.IsNullOrEmpty(title))
                        {
                            title = audioIndex == 0 ? "Combined Audio" : $"Audio Track {audioIndex + 1}";
                        }

                        int streamIdx = stream.TryGetProperty("index", out var idx) ? idx.GetInt32() : audioIndex;
                        audioTracks.Add(new AudioTrackInfo
                        {
                            StreamIndex = streamIdx,
                            TrackIndex = audioIndex,
                            Name = title,
                            Codec = stream.TryGetProperty("codec_name", out var ac) ? ac.GetString() ?? "" : "",
                            IsMuted = false,
                            Gain = 1.0
                        });
                        audioIndex++;
                    }
                }
            }

            // Track zero is a combined preview mix when dedicated tracks exist.
            // Default share export must not remix it with its own constituent tracks.
            if (audioTracks.Count > 1) audioTracks[0].IsMuted = true;

            return new ProbeResult
            {
                IsValid = duration > 0 && width > 0 && height > 0 && !string.IsNullOrEmpty(videoCodec),
                DurationSeconds = duration,
                FileSizeBytes = size,
                Width = width,
                Height = height,
                Fps = fps,
                VideoCodec = videoCodec,
                AudioTracks = audioTracks
            };
        }
        catch (Exception ex)
        {
            return new ProbeResult { IsValid = false, ErrorMessage = "Failed to parse probe JSON: " + ex.Message };
        }
    }

    /// <summary>Dimensions after FFmpeg's default display-matrix autorotation.</summary>
    public static (int Width, int Height) ReadDisplayDimensions(JsonElement videoStream)
    {
        int width = videoStream.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
        int height = videoStream.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
        double rotation = 0;
        bool matrixRotation = false;
        if (videoStream.TryGetProperty("side_data_list", out var sideData) && sideData.ValueKind == JsonValueKind.Array)
        {
            foreach (var side in sideData.EnumerateArray())
            {
                if (side.ValueKind != JsonValueKind.Object || !side.TryGetProperty("rotation", out var angle) ||
                    !TryReadRotation(angle, out rotation)) continue;
                matrixRotation = true;
                break;
            }
        }
        if (!matrixRotation && videoStream.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object &&
            tags.TryGetProperty("rotate", out var legacyRotation))
            _ = TryReadRotation(legacyRotation, out rotation);
        if (Math.Abs(Math.Abs(Math.IEEERemainder(rotation, 180)) - 90) < 0.001)
            (width, height) = (height, width);
        return (width, height);
    }

    private static bool TryReadRotation(JsonElement angle, out double rotation)
    {
        rotation = 0;
        return (angle.ValueKind == JsonValueKind.Number && angle.TryGetDouble(out rotation) ||
                angle.ValueKind == JsonValueKind.String && double.TryParse(angle.GetString(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out rotation)) && double.IsFinite(rotation);
    }

    public virtual async Task<string> GenerateThumbnailAsync(string videoPath, string outputThumbnailPath,
        double positionSec = 1.0, CancellationToken cancellationToken = default)
    {
        string dir = Path.GetDirectoryName(outputThumbnailPath)!;
        Directory.CreateDirectory(dir);

        // Keep thumbnail source pixels bounded. A decoded 1080p JPEG can hold
        // ~8 MiB of pixels even when displayed as a 74x48 WPF clip row.
        // FFmpeg's explicit argument list also handles Unicode/spaces safely.
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string value in new[]
        {
            "-hide_banner", "-loglevel", "error", "-nostats", "-y",
            "-ss", positionSec.ToString("F2", CultureInfo.InvariantCulture),
            "-i", videoPath,
            "-vf", "scale=320:180:force_original_aspect_ratio=decrease:flags=fast_bilinear",
            "-frames:v", "1", "-q:v", "3", outputThumbnailPath
        })
            psi.ArgumentList.Add(value);

        using var proc = new System.Diagnostics.Process { StartInfo = psi };
        using var job = new JobObject();
        proc.Start();
        if (!job.AssignProcess(proc) && !proc.HasExited)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            using var confinementCleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await proc.WaitForExitAsync(confinementCleanup.Token); }
            catch (OperationCanceledException) { throw new MediaWorkerExitUnconfirmedException("Unconfined thumbnail worker exit could not be confirmed within five seconds."); }
            throw new InvalidOperationException("Failed to confine thumbnail worker");
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var interrupt = linked.Token.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
            catch { }
        });
        try
        {
            await proc.WaitForExitAsync(linked.Token);
            return proc.ExitCode == 0 && File.Exists(outputThumbnailPath) ? outputThumbnailPath : "";
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            job.Dispose();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await proc.WaitForExitAsync(cleanup.Token); }
            catch (OperationCanceledException) { throw new MediaWorkerExitUnconfirmedException("Thumbnail worker exit could not be confirmed within five seconds."); }
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("Thumbnail generation reached its time limit.");
        }
    }
    /// <summary>
    /// Explicit, on-demand WPF-compatible preview for MKVs that the Windows
    /// MediaElement cannot decode. Originals remain immutable; bounded cache
    /// and one owned FFmpeg worker are created only after the user's request.
    /// Longer imported videos are limited to the first three minutes for preview.
    /// </summary>
    public async Task<string> BuildPlaybackPreviewAsync(string original,
        CancellationToken cancellationToken = default, bool forceTranscode = false,
        IReadOnlyList<AudioTrackInfo>? audioMix = null, double trimStart = 0, double? trimEnd = null,
        string? cacheDirectory = null)
    {
        if (!File.Exists(original)) throw new FileNotFoundException("Original video is offline.", original);
        var info = new FileInfo(original);
        var media = await ProbeFileAsync(original, cancellationToken);
        if (!media.IsValid || !double.IsFinite(media.DurationSeconds) || media.DurationSeconds <= 0)
            throw new InvalidDataException("The original video is not a valid preview source.");

        string cache = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LocalClip", "PreviewCache");
        Directory.CreateDirectory(cache);
        if (!double.IsFinite(trimStart) || trimStart < 0 || trimStart >= media.DurationSeconds ||
            (trimEnd.HasValue && (!double.IsFinite(trimEnd.Value) || trimEnd.Value <= trimStart || trimEnd.Value > media.DurationSeconds + .1)))
            throw new ArgumentException("Invalid preview trim.");
        string? mixFilter = audioMix == null ? null : ClipExporter.BuildAudioMixFilter(audioMix.ToList());
        string fingerprint = Path.GetFullPath(original).ToUpperInvariant() +
                             "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks +
                             (forceTranscode ? "|preview-safe-v3" : "|preview-copy-v3") +
                             "|" + mixFilter + "|" + trimStart.ToString("R", CultureInfo.InvariantCulture) + "|" + trimEnd?.ToString("R", CultureInfo.InvariantCulture);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
        string resultFile = Path.Combine(cache, key + ".mp4");

        if (File.Exists(resultFile) && new FileInfo(resultFile).Length > 1024)
        {
            var existing = await ProbeFileAsync(resultFile, cancellationToken);
            if (existing.IsValid && existing.VideoCodec.Equals("h264",
                    StringComparison.OrdinalIgnoreCase))
                return resultFile;
        }

        // Keep the app's cache from growing on the first user-requested preview.
        try
        {
            foreach (var old in new DirectoryInfo(cache).EnumerateFiles("*.mp4")
                .OrderByDescending(x => x.LastWriteTimeUtc)
                .Skip(24).Take(80))
            {
                try { old.Delete(); } catch (IOException) { }
            }
        }
        catch (IOException) { }

        string temp = Path.Combine(cache, ".preview-" + Guid.NewGuid().ToString("N") + ".mp4");
        double seconds = PlaybackPreviewPolicy.PreviewSeconds((trimEnd ?? media.DurationSeconds) - trimStart);
        bool copyH264 = !forceTranscode && trimStart == 0 && PlaybackPreviewPolicy.CanStreamCopy(
            media.VideoCodec, media.DurationSeconds, info.Length);

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        string[] args = {
            "-hide_banner", "-loglevel", "error", "-nostats",
            "-y", "-ss", trimStart.ToString("F3", CultureInfo.InvariantCulture), "-i", original,
            "-t", seconds.ToString("F3", CultureInfo.InvariantCulture),
            "-map", "0:v:0",
            "-c:v", copyH264 ? "copy" : "libx264"
        };
        foreach (string value in args) psi.ArgumentList.Add(value);
        foreach (string value in mixFilter == null ? new[] { "-map", "0:a:0?" } : new[] { "-filter_complex", mixFilter, "-map", "[outa]" })
            psi.ArgumentList.Add(value);
        if (!copyH264)
        {
            foreach (string value in new[]
            {
                "-preset", "ultrafast", "-threads", "2", "-crf", "28",
                "-maxrate", "2500k", "-bufsize", "5000k",
                "-vf", "scale=w='min(960,iw)':h=-2",
                "-pix_fmt", "yuv420p"
            }) psi.ArgumentList.Add(value);
        }
        foreach (string value in new[]
        {
            "-c:a", "aac", "-b:a", "96k", "-ar", "48000", "-ac", "2",
            "-movflags", "+faststart", temp
        }) psi.ArgumentList.Add(value);

        using var process = new System.Diagnostics.Process { StartInfo = psi };
        using var job = new JobObject();
        using var maxTime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, maxTime.Token);
        bool workerStarted = false;

        try
        {
            workerStarted = process.Start();
            if (!workerStarted)
                throw new IOException("Could not start the local preview encoder.");
            if (!job.AssignProcess(process) && !process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new IOException("Could not confine the preview encoder.");
            }

            using var killOnCancellation = linked.Token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { }
            });
            Task<string> errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(linked.Token);
            string diagnostic = await errors;
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 || !File.Exists(temp))
                throw new IOException("Preview encoding failed: " +
                                      diagnostic[..Math.Min(400, diagnostic.Length)]);
            long length = new FileInfo(temp).Length;
            if (length < 1024 || length > PlaybackPreviewPolicy.MaximumOutputBytes)
                throw new IOException("Preview exceeds the bounded local preview size.");
            var verification = await ProbeFileAsync(temp, cancellationToken);
            if ((mixFilter != null && (verification.AudioTracks.Count != 1 || verification.AudioTracks[0].Codec != "aac")) || !verification.IsValid || !verification.VideoCodec.Equals(
                    "h264", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Generated preview failed H.264 media verification.");

            File.Move(temp, resultFile, overwrite: true);
            return resultFile;
        }
        finally
        {
            try
            {
                if (workerStarted)
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                    job.Dispose();
                    try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                    catch (Exception) { throw new LocalClip.Core.Audio.MediaWorkerCleanupException(); }
                }
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }
    }

}
