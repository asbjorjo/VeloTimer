namespace VeloTime.WebUI.Mud.Client.Services;

// Each subscription has one pump. Refresh hints coalesce to one follow-up while
// a callback is running; connection-state notifications have two bounded slots.
public sealed class NotificationRefreshCallbacks(Func<NotificationRefreshKind, Task> callback) : IAsyncDisposable
{
    private const int SeenEventLimit = 256;
    private const int RefreshCallbacksPerPump = 2;
    private const int PendingConnectionStateLimit = 2;
    private readonly object _sync = new();
    private readonly HashSet<Guid> _seen = [];
    private readonly Queue<Guid> _order = new();
    private readonly Queue<NotificationRefreshKind> _pendingStates = new(2);
    private readonly HashSet<NotificationRefreshKind> _pendingStateSet = [];
    private Task? _pump;
    private bool _refreshPending;
    private int _refreshCallbacks;
    private int _deliveredStateMask;
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
                _refreshCallbacks = 0;
                _deliveredStateMask = 0;
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
                if (_refreshCallbacks < RefreshCallbacksPerPump) _refreshPending = true;
            }
            else
            {
                var bit = 1 << (int)kind;
                if ((_deliveredStateMask & bit) == 0 && _pendingStateSet.Add(kind))
                {
                    if (_pendingStates.Count < PendingConnectionStateLimit) _pendingStates.Enqueue(kind);
                    else _pendingStateSet.Remove(kind);
                }
            }

            return _pump ?? StartPump();
        }
    }

    private Task StartPump()
    {
        _refreshCallbacks = 0;
        _deliveredStateMask = 0;
        _pump = PumpAsync();
        return _pump;
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

                if (_pendingStates.TryDequeue(out kind))
                {
                    _pendingStateSet.Remove(kind);
                    _deliveredStateMask |= 1 << (int)kind;
                }
                else if (_refreshPending && _refreshCallbacks < RefreshCallbacksPerPump)
                {
                    _refreshPending = false;
                    _refreshCallbacks++;
                    kind = NotificationRefreshKind.Refresh;
                }
                else
                {
                    // Any refreshes arriving during the one follow-up are
                    // deliberately coalesced away; the next hint starts a new
                    // bounded pump instead of extending a continuous drain.
                    ClearPending();
                    _pump = null;
                    break;
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
        _pendingStateSet.Clear();
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
