using System.Collections.Concurrent;
using StackExchange.Redis;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.WebUI.Mud.Notifications;

// The Service Bus logical subscription competes across replicas. This separate
// Redis channel reaches every replica's circuit subscribers; it never broadcasts
// into SignalR (the broker bridge does that exactly once through the backplane).
public sealed class NotificationRefreshFanout(
    IConnectionMultiplexer redis, ILogger<NotificationRefreshFanout> logger,
    string channel = "velotime:webui:notifications:server-refresh:v1") : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, Subscription> _subscriptions = new();
    private readonly RedisChannel _channel = RedisChannel.Literal(channel);
    private readonly object _stateLock = new();
    private ChannelMessageQueue? _queue;
    private volatile bool _connected;
    private volatile bool _stopped;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        redis.ConnectionFailed += ConnectionFailed;
        redis.ConnectionRestored += ConnectionRestored;
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            Task<ChannelMessageQueue>? pending = null;
            try
            {
                var subscriber = redis.GetSubscriber();
                pending = subscriber.SubscribeAsync(_channel);
                var queue = await pending.WaitAsync(stoppingToken);
                queue.OnMessage(message =>
                {
                    // Do not hold Redis's serial queue behind a slow circuit/BFF read.
                    if (!_stopped && Guid.TryParse(message.Message.ToString(), out var id))
                        _ = DispatchAsync(NotificationRefreshKind.Refresh, id);
                });
                lock (_stateLock)
                {
                    if (!_stopped)
                    {
                        _queue = queue;
                        _connected = subscriber.IsConnected(_channel);
                    }
                }
                if (_stopped && _queue is null) await queue.UnsubscribeAsync();
                if (_connected) _ = DispatchAsync(NotificationRefreshKind.Connected);
                return; // StackExchange.Redis restores an established subscription itself.
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // SubscribeAsync has no cancellation token. If it completes after shutdown,
                // release its queue without delaying host shutdown or disposing the multiplexer.
                if (pending is not null) _ = CleanupPendingAsync(pending);
                return;
            }
            catch (Exception e) when (e is RedisException or TimeoutException)
            {
                logger.LogWarning(e, "Notification refresh Redis subscription failed; retrying in {Delay}.", delay);
                _connected = false;
                _ = DispatchAsync(NotificationRefreshKind.TransportFailed);
                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }

    private async Task CleanupPendingAsync(Task<ChannelMessageQueue> pending)
    {
        try { await (await pending).UnsubscribeAsync(); }
        catch (Exception e) { logger.LogWarning(e, "Failed to clean up pending notification refresh subscription."); }
    }

    public async Task PublishAsync(Guid eventId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await redis.GetSubscriber().PublishAsync(_channel, eventId.ToString("D"));
    }

    public INotificationRefreshSubscription Subscribe(Func<NotificationRefreshKind, Task> callback, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var subscription = new Subscription(this, id, callback, token);
        if (!_subscriptions.TryAdd(id, subscription)) throw new InvalidOperationException();
        return subscription;
    }

    private void ConnectionFailed(object? sender, ConnectionFailedEventArgs e)
    {
        if (e.ConnectionType != ConnectionType.Subscription || _stopped) return;
        lock (_stateLock) _connected = false;
        _ = DispatchAsync(NotificationRefreshKind.TransportFailed);
    }
    private void ConnectionRestored(object? sender, ConnectionFailedEventArgs e)
    {
        if (e.ConnectionType != ConnectionType.Subscription || _stopped) return;
        lock (_stateLock)
        {
            if (_queue is null || _stopped) return;
            _connected = true;
        }
        _ = DispatchAsync(NotificationRefreshKind.Connected);
    }

    private Task DispatchAsync(NotificationRefreshKind kind, Guid? id = null) =>
        Task.WhenAll(_subscriptions.Values.Select(subscription => NotifyAsync(subscription, kind, id)));

    private async Task NotifyAsync(Subscription subscription, NotificationRefreshKind kind, Guid? id = null)
    {
        try { await subscription.NotifyAsync(kind, id); }
        catch (Exception e) { logger.LogWarning(e, "Notification refresh subscriber failed."); }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_stateLock) { _stopped = true; _connected = false; }
        redis.ConnectionFailed -= ConnectionFailed;
        redis.ConnectionRestored -= ConnectionRestored;
        await base.StopAsync(cancellationToken);
        if (_queue is not null) await _queue.UnsubscribeAsync();
        foreach (var subscription in _subscriptions.Values) await subscription.DisposeAsync();
        // The existing DI-owned cache multiplexer is reused, never disposed here.
    }

    private sealed class Subscription : INotificationRefreshSubscription
    {
        private readonly NotificationRefreshFanout _owner;
        private readonly Guid _id;
        private readonly NotificationRefreshCallbacks _callbacks;
        private readonly CancellationTokenRegistration _cancellation;
        private readonly CancellationToken _token;

        public Subscription(NotificationRefreshFanout owner, Guid id,
            Func<NotificationRefreshKind, Task> callback, CancellationToken token)
        {
            _owner = owner; _id = id; _callbacks = new(callback); _token = token;
            _cancellation = token.Register(() => _callbacks.Active = false);
        }

        public Task NotifyAsync(NotificationRefreshKind kind, Guid? id) => _callbacks.NotifyAsync(kind, id);

        public Task SetActiveAsync(bool active, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _callbacks.Active = active && !_token.IsCancellationRequested && !_owner._stopped;
            return _callbacks.NotifyAsync(_owner._connected
                ? NotificationRefreshKind.Connected : NotificationRefreshKind.TransportFailed);
        }

        public async ValueTask DisposeAsync()
        {
            _owner._subscriptions.TryRemove(_id, out _);
            _cancellation.Dispose();
            await _callbacks.DisposeAsync();
        }
    }
}
