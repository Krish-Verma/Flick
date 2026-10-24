using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using LocalClip.Core.Audio;
using LocalClip.Core.Ipc;
using LocalClip.Core.Media;
using LocalClip.Core.Models;
using LocalClip.Core.Process;

namespace Flick.AudioQualification;

/// <summary>Explicit synthetic signals only; never opens a microphone/device loopback or app profile.</summary>
internal static class Program
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(string? path, IntPtr module, uint flags);
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--tone") return Tone(int.Parse(args[1]), args[2]);
        if (args.Length != 3 || args[0] != "--run")
        {
            Console.Error.WriteLine("Explicit only: --run <new-output-directory> <encoder>, or --tone <440|880|1320> <existing-owned-directory>."); return 2;
        }
        try { return RunAsync(args[1], args[2]).GetAwaiter().GetResult(); }
        catch (Exception error) { Console.Error.WriteLine(error.GetType().Name + ": " + error.Message); return 1; }
    }
    private static int Tone(int frequency, string directory)
    {
        if (frequency is not (440 or 880 or 1320) || !File.Exists(Path.Combine(directory, "OWNER.json"))) return 2;
        string wave = Path.Combine(directory, $"tone-{frequency}.wav");
        using (var stream = new BinaryWriter(new FileStream(wave, FileMode.CreateNew)))
        {
            const int samples = 48000;
            stream.Write("RIFF"u8); stream.Write(36 + samples * 2); stream.Write("WAVEfmt "u8);
            stream.Write(16); stream.Write((short)1); stream.Write((short)1); stream.Write(samples);
            stream.Write(samples * 2); stream.Write((short)2); stream.Write((short)16); stream.Write("data"u8); stream.Write(samples * 2);
            for (int i = 0; i < samples; i++) stream.Write((short)(Math.Sin(2 * Math.PI * frequency * i / samples) * 2500));
        }
        if (!PlaySound(wave, IntPtr.Zero, 0x20000 | 1 | 8 | 2)) return 3; // filename, async, loop, no default sound
        ApplicationConfiguration.Initialize();
        using var window = new Form { Text = $"Flick generated {frequency} Hz", Width = 640, Height = 360, BackColor = System.Drawing.Color.MidnightBlue };
        window.Controls.Add(new Label { Text = $"Synthetic {frequency} Hz\nNo microphone or desktop capture", Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter, ForeColor = System.Drawing.Color.White, Font = new System.Drawing.Font("Segoe UI", 20) });
        try { Application.Run(window); } finally { PlaySound(null, IntPtr.Zero, 0); }
        return 0;
    }
    private static async Task<int> RunAsync(string outputDirectory, string encoder)
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive || System.Diagnostics.Process.GetCurrentProcess().SessionId == 0)
            throw new InvalidOperationException("An unlocked authorized interactive Windows session is required.");
        if (!Path.IsPathFullyQualified(outputDirectory) || Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
            throw new InvalidOperationException("Choose a new fully qualified output directory; existing data is never overwritten.");
        Directory.CreateDirectory(outputDirectory);
        await using (var owner = new FileStream(Path.Combine(outputDirectory, "OWNER.json"), FileMode.CreateNew, FileAccess.Write))
            await JsonSerializer.SerializeAsync(owner, new { Purpose = "Flick independent-process audio qualification", StartedUtc = DateTimeOffset.UtcNow, Microphone = false, DeviceLoopback = false });
        var children = new List<System.Diagnostics.Process>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var host = new CaptureHostClient();
        string? saved = null; bool exited = false;
        System.Diagnostics.Process? ownedHost = null;
        var evidence = new Dictionary<string, object?> { ["microphone"] = false, ["systemLoopback"] = false, ["hardwareQualified"] = false };
        try
        {
            foreach (int frequency in new[] { 440, 880, 1320 })
            {
                var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                info.ArgumentList.Add("--tone"); info.ArgumentList.Add(frequency.ToString()); info.ArgumentList.Add(outputDirectory);
                children.Add(System.Diagnostics.Process.Start(info) ?? throw new IOException("Could not start owned tone process."));
            }
            foreach (var child in children)
            {
                for (int attempt = 0; attempt < 100; attempt++) { child.Refresh(); if (child.HasExited) throw new IOException("Generated tone process exited."); if (child.MainWindowHandle != IntPtr.Zero) break; await Task.Delay(50, deadline.Token); }
                child.Refresh(); if (child.MainWindowHandle == IntPtr.Zero) throw new IOException("Generated tone window did not appear.");
            }
            var capabilities = await host.GetCapabilitiesAsync();
            if (!capabilities.ExactProcessAudio) throw new InvalidOperationException("Exact app audio is unavailable; refusing system loopback.");
            var windows = (await host.GetTargetsAsync()).Windows;
            var game = windows.Single(window => window.Pid == children[0].Id);
            var voice = windows.Single(window => window.Pid == children[1].Id);
            var settings = new AppSettings { TargetType = "Window", WindowHwnd = game.Hwnd, WindowTitle = game.Title, WindowExecutable = game.Exe,
                VideoWidth = 640, VideoHeight = 360, VideoFps = 30, VideoBitrateKbps = 2500, VideoEncoder = encoder, AllowSoftwareFallback = true,
                ReplayDurationSeconds = 8, BufferMemoryLimitMb = 64, OriginalsFolder = Path.Combine(outputDirectory, "Originals"), AudioMode = "Separated",
                EnableMicrophone = false, GameAudioSource = AudioProcessIdentity.FromWindow(game), VoiceAudioSource = AudioProcessIdentity.FromWindow(voice) };
            string saveId = Guid.NewGuid().ToString("N");
            var terminal = new TaskCompletionSource<(string Path, IReadOnlyList<string> Tracks)>(TaskCreationOptions.RunContinuationsAsynchronously);
            host.ReplaySavedWithTracks += (id, path, tracks) => { if (id == saveId) terminal.TrySetResult((path, tracks)); };
            host.ReplaySaveAborted += reason => terminal.TrySetException(new IOException(reason));
            bool started = await host.StartCaptureAsync(settings, new(ExpectedWindow: new(game.Hwnd, game.Pid, game.Title, game.Exe)));
            if (!started) throw new IOException(host.LastStartError);
            ownedHost = System.Diagnostics.Process.GetProcessById(host.HostProcessId ?? throw new IOException("Capture helper PID unavailable."));
            _ = ownedHost.SafeHandle; // Retain the exact owned process instance before cleanup.
            CaptureHostStatus? last = null;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                deadline.Token.ThrowIfCancellationRequested(); await Task.Delay(250, deadline.Token); last = await host.GetStatusAsync();
                if (last.State == "Faulted") throw new IOException(last.FaultReason);
                if (last.State == "Armed" && last.BufferedSec >= 3 && last.Audio?.Sources.Count(source => source.SignalObserved) == 2) break;
            }
            if (last?.State != "Armed" || last.Audio?.ActualMode != "Separated" || last.Audio.Sources.Count(source => source.SignalObserved) != 2)
                throw new IOException("Both exact processes did not produce independently observed audio.");
            evidence["captureStatus"] = last;
            if (!await host.SaveReplayAsync(saveId, "Flick")) throw new IOException("Synthetic replay save was refused.");
            var replay = await terminal.Task.WaitAsync(TimeSpan.FromSeconds(20), deadline.Token); saved = replay.Path;
            if (!Path.GetFullPath(saved).StartsWith(Path.GetFullPath(settings.OriginalsFolder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(saved) != $"Flick_{saveId}.mkv")
                throw new IOException("Synthetic save provenance does not match the owned run.");
            if (!replay.Tracks.SequenceEqual(new[] { "Combined Audio", "Game Audio (process tree)", "Voice Audio (process tree)" })) throw new IOException("Unexpected real-source track layout.");
            var amplitudes = new List<object>();
            for (int track = 0; track < 3; track++)
            {
                float[] samples = await DecodeAudioAsync(saved, track, deadline.Token);
                double gameTone = Amplitude(samples, 440), voiceTone = Amplitude(samples, 880), rejected = Amplitude(samples, 1320);
                if ((track != 2 && gameTone < .005) || (track != 1 && voiceTone < .005) ||
                    (track == 1 && voiceTone > gameTone * .03) || (track == 2 && gameTone > voiceTone * .03) || rejected > Math.Max(gameTone, voiceTone) * .03)
                    throw new IOException($"Independent-tone isolation failed on track {track}.");
                amplitudes.Add(new { Track = track, Game440 = gameTone, Voice880 = voiceTone, Unselected1320 = rejected });
            }
            evidence["spectralAmplitudes"] = amplitudes; evidence["sourceIsolationPassed"] = true;
            // Closing a chosen source must stop/fault capture, never reconnect
            // to the independent unselected process with the same executable.
            children[1].CloseMainWindow(); await children[1].WaitForExitAsync(deadline.Token);
            bool faulted = false;
            for (int attempt = 0; attempt < 20; attempt++) { await Task.Delay(250, deadline.Token); var status = await host.GetStatusAsync(); if (status.State is "Faulted" or "Off") { faulted = true; break; } }
            if (!faulted) throw new IOException("Lost app process did not stop capture.");
            evidence["sourceLossStoppedCapture"] = true;
            return 0;
        }
        catch (Exception error) { evidence["error"] = error.GetType().Name + ": " + error.Message; throw; }
        finally
        {
            try { await host.ShutdownAsync(); }
            catch (Exception error) { evidence["helperCleanupError"] = error.GetType().Name; }
            finally {
                try { host.Dispose(); } catch (Exception error) { evidence["helperDisposeError"] = error.GetType().Name; }
                try { exited = ownedHost == null ? !host.IsRunning : ownedHost.HasExited; } catch { exited = false; }
                ownedHost?.Dispose();
            }
            foreach (var child in children)
            {
                try { if (!child.HasExited) { child.CloseMainWindow(); if (!child.WaitForExit(2000)) child.Kill(true); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } } catch { evidence["ownedToneCleanupUnconfirmed"] = true; }
                finally { child.Dispose(); }
            }
            evidence["helperExitConfirmed"] = exited; evidence["savedOriginal"] = saved; evidence["finishedUtc"] = DateTimeOffset.UtcNow;
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "audio-qualification.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            if (!exited || evidence.ContainsKey("ownedToneCleanupUnconfirmed") || evidence.ContainsKey("helperCleanupError") || evidence.ContainsKey("helperDisposeError")) throw new IOException("Owned audio qualification processes did not confirm exit.");
        }
    }
    private static double Amplitude(float[] samples, double frequency)
    {
        int start = Math.Min(4800, samples.Length / 4), count = samples.Length - start; double sin = 0, cos = 0;
        for (int i = start; i < samples.Length; i++) { double angle = 2 * Math.PI * frequency * i / 48000; sin += samples[i] * Math.Sin(angle); cos += samples[i] * Math.Cos(angle); }
        return count > 0 ? 2 * Math.Sqrt(sin * sin + cos * cos) / count : 0;
    }
    private static async Task<float[]> DecodeAudioAsync(string path, int track, CancellationToken token)
    {
        string executable = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("Pinned bundled FFmpeg is required.", executable);
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string value in new[] { "-v", "error", "-nostdin", "-i", path, "-map", $"0:a:{track}", "-t", "4", "-ac", "1", "-ar", "48000", "-f", "f32le", "pipe:1" }) info.ArgumentList.Add(value);
        using var process = new System.Diagnostics.Process { StartInfo = info }; using var job = new JobObject();
        process.Start(); if (!job.AssignProcess(process) && !process.HasExited) { process.Kill(true); throw new IOException("Could not confine decoder."); }
        using var cancel = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        using var bytes = new MemoryStream(); var copy = process.StandardOutput.BaseStream.CopyToAsync(bytes, token); var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(token); await copy; if (process.ExitCode != 0) throw new IOException(await errors);
        var data = bytes.ToArray(); var samples = new float[data.Length / 4]; Buffer.BlockCopy(data, 0, samples, 0, samples.Length * 4); return samples;
    }
}
