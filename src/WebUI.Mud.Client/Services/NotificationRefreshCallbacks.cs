namespace VeloTime.WebUI.Mud.Client.Services;

// Each subscription has one pump. Pending refresh hints and connection-state
// transitions coalesce in bounded storage while callbacks are running.
public sealed class NotificationRefreshCallbacks(Func<NotificationRefreshKind, Task> callback) : IAsyncDisposable
{
    private const int SeenEventLimit = 256;
    private readonly object _sync = new();
    private readonly HashSet<Guid> _seen = [];
    private readonly Queue<Guid> _order = new();
    private readonly Queue<NotificationRefreshKind> _pendingStates = new(2);
    private Task? _pump;
    private bool _refreshPending;
    private bool _active;
    private bool _disposed;

    public bool Active
    {
        get { lock (_sync) return _active; }
        set
        {
            lock (_sync)
            {
                if (_disposed) return;
                if (_active == value) return;
                _active = value;
                ClearPending();
            }
        }
    }

    public Task NotifyAsync(NotificationRefreshKind kind, Guid? eventId = null)
    {
        lock (_sync)
        {
            if (_disposed || !_active) return Task.CompletedTask;
            if (eventId.HasValue)
            {
                if (!_seen.Add(eventId.Value)) return _pump ?? Task.CompletedTask;
                _order.Enqueue(eventId.Value);
                if (_order.Count > SeenEventLimit) _seen.Remove(_order.Dequeue());
            }

            if (kind == NotificationRefreshKind.Refresh)
            {
                _refreshPending = true;
            }
            else
            {
                QueueConnectionState(kind);
            }

            return _pump ?? StartPump();
        }
    }

    private Task StartPump()
    {
        _pump = PumpAsync();
        return _pump;
    }

    private void QueueConnectionState(NotificationRefreshKind kind)
    {
        if (_pendingStates.Count > 0 && _pendingStates.Last() == kind) return;

        // Keep only the latest ordering of the two connection states. If a
        // state repeats after its opposite, move it to the end so the final
        // callback still reflects the latest connection transition.
        if (_pendingStates.Contains(kind))
        {
            var remaining = _pendingStates.Where(state => state != kind).ToArray();
            _pendingStates.Clear();
            foreach (var state in remaining) _pendingStates.Enqueue(state);
        }

        _pendingStates.Enqueue(kind);
    }

    private async Task PumpAsync()
    {
        // Ensure StartPump has published the one shared task before the pump can
        // complete or accept more work.
        await Task.Yield();
        Exception? firstFailure = null;
        while (true)
        {
            NotificationRefreshKind kind;
            lock (_sync)
            {
                if (_disposed || !_active)
                {
                    ClearPending();
                    _pump = null;
                    break;
                }

                if (!_pendingStates.TryDequeue(out kind))
                {
                    if (_refreshPending)
                    {
                        _refreshPending = false;
                        kind = NotificationRefreshKind.Refresh;
                    }
                    else
                    {
                        ClearPending();
                        _pump = null;
                        break;
                    }
                }
            }

            try { await callback(kind); }
            catch (Exception exception) { firstFailure ??= exception; }
        }

        if (firstFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstFailure).Throw();
    }

    private void ClearPending()
    {
        _refreshPending = false;
        _pendingStates.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        Task? pump;
        lock (_sync)
        {
            if (!_disposed)
            {
                _disposed = true;
                _active = false;
                ClearPending();
            }
            pump = _pump;
        }

        if (pump is null) return;
        try { await pump; }
        catch (Exception) { /* NotifyAsync callers observe callback failures. */ }
    }
}
