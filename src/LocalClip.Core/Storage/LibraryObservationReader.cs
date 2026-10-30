namespace LocalClip.Core.Storage;

public enum LibraryObservationPriority { SelectedClip, Availability, RecentExports }

/// <summary>
/// One read-only worker and at most one queued request per purpose. A newer
/// request replaces queued work; cancellation never waits for a native stat.
/// Selected-clip work runs before the next bounded background batch.
/// </summary>
public sealed class LibraryObservationReader : IDisposable
{
    private interface IRequest { void Run(); void Cancel(); }
    private sealed class Request<T> : IRequest
    {
        private readonly Func<CancellationToken, T> _read;
        private readonly CancellationToken _token;
        private readonly CancellationTokenRegistration _registration;
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T> Task => _completion.Task;
        public Request(Func<CancellationToken, T> read, CancellationToken token)
        {
            _read = read; _token = token;
            _registration = token.Register(() => _completion.TrySetCanceled(token));
        }
        public void Cancel() { _completion.TrySetCanceled(); _registration.Unregister(); }
        public void Run()
        {
            try
            {
                _token.ThrowIfCancellationRequested();
                T result = _read(_token);
                _token.ThrowIfCancellationRequested();
                _completion.TrySetResult(result);
            }
            catch (OperationCanceledException) { _completion.TrySetCanceled(); }
            catch (Exception error) { _completion.TrySetException(error); }
            finally { _registration.Unregister(); }
        }
    }

    private readonly object _gate = new();
    private readonly IRequest?[] _queued = new IRequest?[3];
    private IRequest? _active;
    private bool _running, _disposed;

    public Task<T> ReadAsync<T>(LibraryObservationPriority priority, Func<CancellationToken, T> read, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (!Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(priority));
        token.ThrowIfCancellationRequested();
        var request = new Request<T>(read, token);
        lock (_gate)
        {
            if (_disposed) { request.Cancel(); return request.Task; }
            int slot = (int)priority;
            _queued[slot]?.Cancel();
            _queued[slot] = request;
            if (!_running) { _running = true; _ = System.Threading.Tasks.Task.Run(Pump); }
        }
        return request.Task;
    }

    private void Pump()
    {
        while (true)
        {
            IRequest? request;
            lock (_gate)
            {
                request = null;
                for (int slot = 0; slot < _queued.Length; slot++)
                    if (_queued[slot] is { } pending) { request = pending; _queued[slot] = null; break; }
                if (request == null) { _active = null; _running = false; return; }
                _active = request;
            }
            request.Run();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _active?.Cancel();
            for (int slot = 0; slot < _queued.Length; slot++) { _queued[slot]?.Cancel(); _queued[slot] = null; }
        }
    }
}
