using System.Collections.Concurrent;
using StackExchange.Redis;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.WebUI.Mud.Notifications;

// The Service Bus logical subscription competes across replicas. This separate
// Redis channel reaches every replica's circuit subscribers; it never broadcasts
// into SignalR (the broker bridge does that exactly once through the backplane).
public sealed class NotificationRefreshFanout(
    IConnectionMultiplexer redis, ILogger<NotificationRefreshFanout> logger,
    string channel = "velotime:webui:notifications:server-refresh:v1") : IHostedService
{
    private readonly ConcurrentDictionary<Guid, Subscription> _subscriptions = new();
    private readonly RedisChannel _channel = RedisChannel.Literal(channel);
    private ChannelMessageQueue? _queue;
    private volatile bool _connected;
    private volatile bool _stopped;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        redis.ConnectionFailed += ConnectionFailed;
        redis.ConnectionRestored += ConnectionRestored;
        try
        {
            _queue = await redis.GetSubscriber().SubscribeAsync(_channel);
            _queue.OnMessage(message =>
            {
                // Do not hold Redis's serial queue behind a slow circuit/BFF read.
                // Per-subscriber callbacks serialize themselves; faults are observed.
                if (Guid.TryParse(message.Message.ToString(), out var id))
                    _ = DispatchAsync(NotificationRefreshKind.Refresh, id);
            });
            _connected = true;
        }
        catch
        {
            redis.ConnectionFailed -= ConnectionFailed;
            redis.ConnectionRestored -= ConnectionRestored;
            throw;
        }
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
        _connected = false;
        _ = DispatchAsync(NotificationRefreshKind.TransportFailed);
    }
    private void ConnectionRestored(object? sender, ConnectionFailedEventArgs e)
    {
        if (e.ConnectionType != ConnectionType.Subscription || _stopped) return;
        _connected = true;
        _ = DispatchAsync(NotificationRefreshKind.Connected);
    }

    private Task DispatchAsync(NotificationRefreshKind kind, Guid? id = null) =>
        Task.WhenAll(_subscriptions.Values.Select(subscription => NotifyAsync(subscription, kind, id)));

    private async Task NotifyAsync(Subscription subscription, NotificationRefreshKind kind, Guid? id = null)
    {
        try { await subscription.NotifyAsync(kind, id); }
        catch (Exception e) { logger.LogWarning(e, "Notification refresh subscriber failed."); }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopped = true; _connected = false;
        redis.ConnectionFailed -= ConnectionFailed;
        redis.ConnectionRestored -= ConnectionRestored;
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
