using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using LocalClip.Core.Storage;

namespace Flick.RecordingHarness;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }
    };

    [STAThread]
    public static int Main(string[] arguments)
    {
        try
        {
            if (arguments.Length == 1 && arguments[0] == "--help")
            {
                Console.WriteLine("Flick isolated synthetic-window qualification. No desktop or audio devices are captured.\n" +
                    "--self-check [--receipt <new absolute JSON path>] (no windows, profile or capture)\n" +
                    "--host <absolute.exe> --ffmpeg <absolute.exe> --ffprobe <absolute.exe>\n" +
                    "--output <new Flick-RecordingQualification-32hexGUID directory>\n" +
                    "--encoder <explicit OBS encoder ID> --run-capture\n" +
                    "Requires an unlocked interactive user session. Writes CANCEL under the owned output to stop.\n" +
                    "Work deadline: 8 minutes; cleanup is awaited. Outputs and failed-case originals are preserved.");
                return 0;
            }
            if (arguments.Length > 0 && arguments[0] == "--self-check") return SelfCheck(arguments);
            // Refuse the default before any Application, Window, directory or helper construction.
            HarnessOptions options = HarnessOptions.Parse(arguments);
            options.ValidateExistingInputs();
            int session = InteractiveSession.Require();
            OwnedOutput output = OwnedOutput.Create(options.Output);
            return RunInteractive(options, output, session);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Qualification refused/failed: " + error.Message);
            return 2;
        }
    }

    private static int SelfCheck(string[] arguments)
    {
        string? receiptPath = null;
        if (arguments.Length != 1)
        {
            if (arguments.Length != 3 || arguments[1] != "--receipt" || !Path.IsPathFullyQualified(arguments[2]))
                throw new ArgumentException("Use --self-check [--receipt <new absolute JSON path>].");
            receiptPath = Path.GetFullPath(arguments[2]);
            if (receiptPath.StartsWith(@"\\", StringComparison.Ordinal)) throw new ArgumentException("Use a local receipt path.");
            OwnedOutput.RefuseReparseComponents(Path.GetDirectoryName(receiptPath)!);
        }
        SelfCheckReceipt receipt = SelfChecks.Run();
        string content = JsonSerializer.Serialize(receipt, Json);
        if (receiptPath != null)
        {
            using var file = new FileStream(receiptPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(file);
            writer.Write(content);
        }
        Console.WriteLine(content);
        return receipt.Accepted ? 0 : 1;
    }

    private static int RunInteractive(HarnessOptions options, OwnedOutput output, int session)
    {
        using var overall = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        using var watcherStop = new CancellationTokenSource();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var fixture = new PatternWindow();
        bool finishing = false;
        fixture.Closed += (_, _) => { if (!finishing) _ = overall.CancelAsync(); };
        ConsoleCancelEventHandler consoleCancel = (_, e) => { e.Cancel = true; _ = overall.CancelAsync(); };
        Console.CancelKeyPress += consoleCancel;
        Task watcher = WatchCancellationAsync(output.Resolve("CANCEL"), overall, watcherStop.Token);
        app.Startup += async (_, _) =>
        {
            int code = 1;
            var timer = Stopwatch.StartNew();
            try
            {
                output.WriteNew("runtime.json", new
                {
                    SchemaVersion = 1, output.RunId, SessionId = session,
                    Os = Environment.OSVersion.VersionString, StartedUtc = DateTimeOffset.UtcNow,
                    options.Encoder, Scope = "Owned synthetic window only; system audio off; microphone off",
                    HostSha256 = await OwnedOutput.HashAsync(options.Host),
                    FfmpegSha256 = await OwnedOutput.HashAsync(options.Ffmpeg),
                    FfprobeSha256 = await OwnedOutput.HashAsync(options.Ffprobe),
                    HarnessSha256 = await OwnedOutput.HashAsync(typeof(Program).Assembly.Location),
                    CoreSha256 = await OwnedOutput.HashAsync(typeof(ClipStorage).Assembly.Location),
                    HarnessMemory = OwnMemory()
                });
                await fixture.ShowReadyAsync(overall.Token);
                output.WriteNew("fixture.json", new
                {
                    SchemaVersion = 1, Scope = "Only the harness-owned generated window",
                    fixture.ProcessId, WindowHwnd = fixture.Identity.Hwnd, fixture.ExecutableName,
                    fixture.PhysicalClientWidth, fixture.PhysicalClientHeight,
                    PatternWindow.FrameWidth, PatternWindow.FrameHeight, PatternWindow.TargetFramesPerSecond,
                    PatternWindow.MotionBandTop, PatternWindow.MotionBandBottom, PatternWindow.Samples
                });
                var suite = new QualificationSuite(options, output, fixture);
                code = await suite.RunAsync(overall.Token) ? 0 : 1;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                try { output.WriteNew("fatal-error.json", new { SchemaVersion = 1, Error = error.Message }); } catch { }
            }
            finally
            {
                finishing = true;
                await watcherStop.CancelAsync();
                try { await watcher; } catch (OperationCanceledException) { }
                try { await fixture.CloseAsync(); } catch { }
                try { output.WriteNew("harness-exit.json", new { SchemaVersion = 1, ExitCode = code,
                    ElapsedMilliseconds = timer.Elapsed.TotalMilliseconds, Cancelled = overall.IsCancellationRequested,
                    HarnessMemory = OwnMemory() }); }
                catch { code = 1; }
                Console.CancelKeyPress -= consoleCancel;
                app.Shutdown(code);
            }
        };
        return app.Run();
    }

    private static async Task WatchCancellationAsync(string sentinel, CancellationTokenSource overall, CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false))
            if (Path.Exists(sentinel)) { await overall.CancelAsync().ConfigureAwait(false); return; }
    }

    private static object OwnMemory()
    {
        using var current = Process.GetCurrentProcess();
        current.Refresh();
        return new { PrivateBytes = current.PrivateMemorySize64, WorkingSetBytes = current.WorkingSet64 };
    }
}
