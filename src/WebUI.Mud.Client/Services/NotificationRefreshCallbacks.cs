namespace VeloTime.WebUI.Mud.Client.Services;

// Serialize callbacks and bound redelivery memory. Each subscription owns its state.
public sealed class NotificationRefreshCallbacks(Func<NotificationRefreshKind, Task> callback) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1);
    private readonly HashSet<Guid> _seen = [];
    private readonly Queue<Guid> _order = new();
    private volatile bool _disposed;
    public bool Active { get; set; }

    public async Task NotifyAsync(NotificationRefreshKind kind, Guid? eventId = null)
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed || !Active) return;
            if (eventId.HasValue)
            {
                if (!_seen.Add(eventId.Value)) return;
                _order.Enqueue(eventId.Value);
                if (_order.Count > 256) _seen.Remove(_order.Dequeue());
            }
            await callback(kind);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true; Active = false;
        await _gate.WaitAsync();
        _gate.Release();
        // Do not dispose the gate: an already queued notification may still exit it.
    }
}
