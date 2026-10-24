using System.Diagnostics;
using System.Text;
using LocalClip.Core.Process;

namespace Flick.RecordingHarness;

internal sealed record ToolResult(byte[] Output, string Error);
internal sealed class OwnedWorkerCleanupException(string message) : InvalidOperationException(message);

internal static class ToolProcess
{
    public static async Task<ToolResult> RunAsync(string executable, IEnumerable<string> arguments,
        int maximumOutputBytes, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        using var job = new JobObject();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var work = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        process.Start();
        Task<byte[]>? output = null, error = null;
        try
        {
            if (!job.AssignProcess(process) && !process.HasExited)
                throw new InvalidOperationException("Cannot confine the owned media verification worker.");
            using var interrupt = work.Token.Register(() => Kill(process));
            async Task<byte[]> ReadPipe(Stream stream, int maximum)
            {
                try { return await ReadBoundedAsync(stream, maximum, work.Token).ConfigureAwait(false); }
                catch { await work.CancelAsync().ConfigureAwait(false); throw; }
            }
            output = ReadPipe(process.StandardOutput.BaseStream, maximumOutputBytes);
            error = ReadPipe(process.StandardError.BaseStream, 128 * 1024);
            await Task.WhenAll(output, error, process.WaitForExitAsync(work.Token)).WaitAsync(work.Token).ConfigureAwait(false);
            string stderr = Encoding.UTF8.GetString(await error.ConfigureAwait(false));
            if (process.ExitCode != 0)
                throw new InvalidDataException("Owned media verifier exited " + process.ExitCode + ": " + stderr);
            return new(await output.ConfigureAwait(false), stderr);
        }
        catch
        {
            // Cancellation/overflow kills only this worker, then observes both
            // the process and its pipes under one separate cleanup deadline.
            await work.CancelAsync().ConfigureAwait(false);
            job.Dispose();
            Kill(process);
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
                Task readers = Task.WhenAll(new[] { output, error }.OfType<Task<byte[]>>());
                try { await readers.WaitAsync(cleanup.Token).ConfigureAwait(false); }
                catch when (readers.IsCompleted) { /* Reader errors caused the abort; all are observed. */ }
            }
            catch (OperationCanceledException)
            {
                process.StandardOutput.Close();
                process.StandardError.Close();
                foreach (var reader in new[] { output, error }.OfType<Task<byte[]>>())
                    _ = reader.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                throw new OwnedWorkerCleanupException("Owned media worker cleanup exceeded 10 seconds; exit or pipe completion was not confirmed.");
            }
            throw;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[16 * 1024];
        while (true)
        {
            int count = await stream.ReadAsync(chunk, token).ConfigureAwait(false);
            if (count == 0) return buffer.ToArray();
            if (buffer.Length + count > maximum) throw new InvalidDataException("Media verifier exceeded its bounded output.");
            buffer.Write(chunk, 0, count);
        }
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
