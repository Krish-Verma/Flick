using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace LocalClip.App.Services;

/// <summary>
/// One same-user/session, payload-free activation signal. The mutex still owns
/// single-instance selection; this event only asks its owner to show its window.
/// The channel has no polling, file/registry writes, or capture side effects.
/// </summary>
public sealed class FlickSingleInstanceActivation : IDisposable
{
    private const string DefaultName = "LocalClip_Flick_Activation_v1";
    private readonly EventWaitHandle _signal;
    private readonly object _gate = new();
    private RegisteredWaitHandle? _wait;
    private Func<Action, bool>? _queueActivation;
    private Action? _activate;
    private int _disposed;
    private int _activationPending;

    /// <param name="channelName">Tests supply an isolated GUID name; normal apps use the private default channel.</param>
    public FlickSingleInstanceActivation(string channelName = DefaultName)
    {
        if (string.IsNullOrWhiteSpace(channelName)) throw new ArgumentException("An activation channel name is required.", nameof(channelName));
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, channelName,
            new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = true });
    }

    public static bool ShouldActivateExisting(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return !arguments.Any(argument => argument.Equals("--startup", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("--design-preview", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Start only after this process owns the existing single-instance mutex.
    /// The queue returns false if its UI dispatcher is already shutting down.
    /// </summary>
    public void Listen(Func<Action, bool> queueActivation, Action activate)
    {
        ArgumentNullException.ThrowIfNull(queueActivation);
        ArgumentNullException.ThrowIfNull(activate);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (_wait != null) throw new InvalidOperationException("Activation is already listening.");
            _queueActivation = queueActivation;
            _activate = activate;
            _wait = ThreadPool.RegisterWaitForSingleObject(_signal,
                (_, timedOut) => { if (!timedOut) OnSignal(); }, null, Timeout.Infinite, executeOnlyOnce: false);
        }
    }

    public bool RequestActivation()
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        try { return _signal.Set(); }
        catch (ObjectDisposedException) { return false; }
    }

    private void OnSignal()
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.CompareExchange(ref _activationPending, 1, 0) != 0) return;
        try
        {
            var queue = _queueActivation;
            var activate = _activate;
            if (queue == null || activate == null || Volatile.Read(ref _disposed) != 0)
            {
                Volatile.Write(ref _activationPending, 0);
                return;
            }
            bool queued = queue(() =>
            {
                try { if (Volatile.Read(ref _disposed) == 0) activate(); }
                catch (InvalidOperationException) { /* The window may have closed after this work was queued. */ }
                finally { Volatile.Write(ref _activationPending, 0); }
            });
            if (!queued) Volatile.Write(ref _activationPending, 0);
        }
        catch (Exception)
        {
            // A UI dispatcher closing concurrently must not crash a background
            // ThreadPool callback or keep activation permanently reserved.
            Volatile.Write(ref _activationPending, 0);
        }
    }

    public void Dispose()
    {
        RegisteredWaitHandle? wait;
        lock (_gate)
        {
            if (_disposed != 0) return;
            Volatile.Write(ref _disposed, 1);
            wait = _wait;
            _wait = null;
            _queueActivation = null;
            _activate = null;
        }
        try { wait?.Unregister(null); }
        finally { _signal.Dispose(); }
    }
}
