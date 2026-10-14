using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using LocalClip.Core.Models;

namespace LocalClip.Core.Media;

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
        string probe3 = Path.Combine(@"D:\Documents\ClipMax\tools\ffmpeg", name);

        if (File.Exists(probe1)) return probe1;
        if (File.Exists(probe2)) return probe2;
        if (File.Exists(probe3)) return probe3;
        return name;
    }

    public async Task<ProbeResult> ProbeFileAsync(string filePath)
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
        proc.Start();
        string output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();

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
                        width = stream.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                        height = stream.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
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
                            IsMuted = false,
                            Gain = 1.0
                        });
                        audioIndex++;
                    }
                }
            }

            return new ProbeResult
            {
                IsValid = true,
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

    public async Task<string> GenerateThumbnailAsync(string videoPath, string outputThumbnailPath, double positionSec = 1.0)
    {
        string dir = Path.GetDirectoryName(outputThumbnailPath)!;
        Directory.CreateDirectory(dir);

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = $"-y -ss {positionSec.ToString("F2", CultureInfo.InvariantCulture)} -i \"{videoPath}\" -vframes 1 -q:v 2 \"{outputThumbnailPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = new System.Diagnostics.Process { StartInfo = psi };
        proc.Start();
        await proc.WaitForExitAsync();
        return File.Exists(outputThumbnailPath) ? outputThumbnailPath : "";
    }
}
