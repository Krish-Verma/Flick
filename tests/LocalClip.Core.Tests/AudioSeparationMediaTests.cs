using System.Diagnostics;
using LocalClip.Core.Audio;
using LocalClip.Core.Media;
using LocalClip.Core.Models;
using Xunit.Abstractions;
namespace LocalClip.Core.Tests;

/// <summary>Generated audio only. Proves filter contribution, never endpoint/process capture.</summary>
public sealed class AudioSeparationMediaTests(ITestOutputHelper output)
{
    [MediaFact]
    public async Task IndependentTonesVerifyMuteGainDefaultsCombinedAndNoDoubleMix()
    {
        string ffmpeg = Environment.GetEnvironmentVariable("FLICK_TEST_FFMPEG") ?? throw new InvalidOperationException("Set FLICK_TEST_FFMPEG to the explicit test tool.");
        string root = Path.Combine(Path.GetTempPath(), "Flick-AudioMix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string original = Path.Combine(root, "independent-tones.mka");
            await Run(ffmpeg, ["-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=1", "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000:duration=1",
                "-filter_complex", "[0:a]asplit=2[g][gm];[1:a]asplit=2[v][vm];[gm][vm]amix=inputs=2:normalize=0[c]", "-map", "[c]", "-map", "[g]", "-map", "[v]", "-c:a", "pcm_f32le", original]);
            byte[] originalBytes = await File.ReadAllBytesAsync(original);
            var tracks = new List<AudioTrackInfo> {
                new() { TrackIndex = 0, Role = AudioTrackRole.Combined }, new() { TrackIndex = 1, Role = AudioTrackRole.Game }, new() { TrackIndex = 2, Role = AudioTrackRole.Voice } };
            AudioTrackRoles.Reset(new() { AudioTracks = tracks });
            var baseline = await Mix(ffmpeg, original, tracks);
            double game = Amplitude(baseline, 440), voice = Amplitude(baseline, 880);
            Assert.InRange(game, .12, .14); Assert.InRange(voice, .12, .14);
            tracks[1].Gain = .5; tracks[2].IsMuted = true;
            var selected = await Mix(ffmpeg, original, tracks);
            Assert.InRange(Amplitude(selected, 440) / game, .49, .51);
            Assert.True(Amplitude(selected, 880) < voice * .001);
            tracks[0].IsMuted = false; tracks[1].Gain = 2; tracks[2].IsMuted = false;
            var combined = await Mix(ffmpeg, original, tracks);
            Assert.InRange(Amplitude(combined, 440) / game, .99, 1.01);
            Assert.InRange(Amplitude(combined, 880) / voice, .99, 1.01);
            foreach (var track in tracks) track.IsMuted = true;
            var silent = await Mix(ffmpeg, original, tracks);
            Assert.All(silent, sample => Assert.InRange(Math.Abs(sample), 0, .000001));
            AudioTrackRoles.Reset(new() { AudioTracks = tracks });
            string mp4 = Path.Combine(root, "mixed.mp4");
            await Run(ffmpeg, ["-y", "-i", original, "-f", "lavfi", "-i", "color=c=blue:s=320x180:r=30:d=1",
                "-filter_complex", ClipExporter.BuildAudioMixFilter(tracks), "-map", "1:v:0", "-map", "[outa]", "-t", "0.8",
                "-c:v", "libx264", "-preset", "ultrafast", "-threads", "2", "-c:a", "aac", "-movflags", "+faststart", mp4]);
            await Run(ffmpeg, ["-i", mp4, "-f", "null", "-"]);
            Assert.True(new FileInfo(mp4).Length > 1000);
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(original));
            output.WriteLine($"Generated PCM amplitudes: game={game:F6}, voice={voice:F6}; gain/mute/combined/no-double-mix/silence and original immutability passed.");
        }
        finally { Directory.Delete(root, true); }
    }
    private static async Task<float[]> Mix(string ffmpeg, string original, List<AudioTrackInfo> tracks)
    {
        var bytes = await Run(ffmpeg, ["-i", original, "-filter_complex", ClipExporter.BuildAudioMixFilter(tracks), "-map", "[outa]", "-t", "0.8", "-ac", "1", "-c:a", "pcm_f32le", "-f", "f32le", "pipe:1"]);
        float[] values = new float[bytes.Length / 4]; Buffer.BlockCopy(bytes, 0, values, 0, values.Length * 4); return values;
    }
    private static double Amplitude(float[] values, int frequency)
    {
        int start = 4800, end = Math.Min(values.Length, 33600); double sin = 0, cos = 0;
        for (int i = start; i < end; i++) { double phase = 2 * Math.PI * frequency * i / 48000; sin += values[i] * Math.Sin(phase); cos += values[i] * Math.Cos(phase); }
        return 2 * Math.Sqrt(sin * sin + cos * cos) / (end - start);
    }
    private static async Task<byte[]> Run(string executable, string[] args)
    {
        var info = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("-hide_banner"); info.ArgumentList.Add("-loglevel"); info.ArgumentList.Add("error"); info.ArgumentList.Add("-nostdin");
        foreach (string arg in args) info.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(info) ?? throw new IOException("Could not start generated-media worker.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var cancel = timeout.Token.Register(() => { try { process.Kill(true); } catch { } });
        using var data = new MemoryStream(); Task read = process.StandardOutput.BaseStream.CopyToAsync(data, timeout.Token); Task<string> errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(timeout.Token); await read; string detail = await errors;
        Assert.True(process.ExitCode == 0, detail); return data.ToArray();
    }
}
