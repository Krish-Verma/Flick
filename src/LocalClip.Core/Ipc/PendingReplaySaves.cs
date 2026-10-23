namespace LocalClip.Core.Ipc;

/// <summary>
/// An accepted command is not a completed save. Only a matching terminal event,
/// rejection, or confirmed helper exit settles the corresponding request.
/// </summary>
public sealed class PendingReplaySaves
{
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public int Count { get { lock (_gate) return _pending.Count; } }

    public void Begin(string saveId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveId);
        lock (_gate)
            if (!_pending.Add(saveId)) throw new InvalidOperationException("This replay save is already pending.");
    }

    public bool Acknowledge(string saveId, bool accepted) => !accepted && Complete(saveId);

    public bool Complete(string saveId)
    {
        lock (_gate) return _pending.Remove(saveId);
    }

    public IReadOnlyList<string> HostExited(bool confirmed)
    {
        lock (_gate)
        {
            if (!confirmed || _pending.Count == 0) return Array.Empty<string>();
            var abandoned = _pending.ToArray();
            _pending.Clear();
            return abandoned;
        }
    }
}
