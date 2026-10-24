namespace LocalClip.Core.Audio;

/// <summary>One owned worker. A timeout refuses the transition without releasing worker ownership.</summary>
public sealed class AudioPreviewOperation
{
    private readonly object _gate = new();
    private Owner? _owner;
    private bool _cleanupFailed;
    private sealed class Owner(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task<string> Execution { get; set; } = null!;
        public Task? CancellationDispatch { get; set; }
    }
    public bool IsBusy { get { lock (_gate) return _owner != null; } }
    public bool CleanupFailed { get { lock (_gate) return _cleanupFailed; } }
    public Task<string> StartAsync(Func<CancellationToken, Task<string>> worker, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(worker);
        lock (_gate)
        {
            if (_owner != null || _cleanupFailed) throw new InvalidOperationException("Finish the existing audio preview before starting another operation.");
            var owner = new Owner(CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
            _owner = owner;
            owner.Execution = Task.Run(async () =>
            {
                try { return await worker(owner.Cancellation.Token).ConfigureAwait(false); }
                catch (MediaWorkerCleanupException) { lock (_gate) _cleanupFailed = true; throw; }
                finally
                {
                    lock (_gate) { if (ReferenceEquals(_owner, owner)) _owner = null; }
                    owner.Cancellation.Dispose();
                }
            });
            return owner.Execution;
        }
    }
    public async Task<bool> CancelAndWaitAsync(TimeSpan? cleanupBudget = null)
    {
        Owner? owner;
        lock (_gate) owner = _owner;
        if (owner == null) return !CleanupFailed;
        try
        {
            lock (_gate)
            {
                try { owner.CancellationDispatch ??= owner.Cancellation.CancelAsync(); }
                catch (ObjectDisposedException) { owner.CancellationDispatch ??= Task.CompletedTask; }
            }
            await Task.WhenAll(owner.CancellationDispatch!, owner.Execution).WaitAsync(cleanupBudget ?? TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        }
        catch (TimeoutException) { return false; }
        catch (Exception) { /* Worker failure/cancellation still requires settled ownership below. */ }
        lock (_gate) return _owner == null && !_cleanupFailed;
    }
}

public sealed class MediaWorkerCleanupException() : IOException("The preview worker did not confirm exit within its cleanup deadline.");
