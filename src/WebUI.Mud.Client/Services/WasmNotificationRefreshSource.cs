using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using VeloTime.Module.Notifications.Interface.Messages;

namespace VeloTime.WebUI.Mud.Client.Services;

public sealed class WasmNotificationRefreshSource : INotificationRefreshSource
{
    private readonly Func<HubConnection> _create;

    public WasmNotificationRefreshSource(NavigationManager navigation)
        : this(() => new HubConnectionBuilder()
            .WithUrl(navigation.ToAbsoluteUri("/hubs/notifications"))
            .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)])
            .Build()) { }

    // Factory also permits real Microsoft-client lifecycle tests against TestServer.
    public WasmNotificationRefreshSource(Func<HubConnection> create) => _create = create;

    public ValueTask<INotificationRefreshSubscription> SubscribeAsync(
        Func<NotificationRefreshKind, Task> callback, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<INotificationRefreshSubscription>(
            new Subscription(_create(), callback, cancellationToken));
    }

    private sealed class Subscription : INotificationRefreshSubscription
    {
        private readonly HubConnection _hub;
        private readonly NotificationRefreshCallbacks _callbacks;
        private readonly CancellationTokenSource _lifetime;
        private readonly SemaphoreSlim _lifecycle = new(1);
        private readonly IDisposable _handler;
        private readonly CancellationTokenRegistration _cancellation;
        private CancellationTokenSource? _run;
        private Task _startup = Task.CompletedTask;
        private bool _disposed;

        public Subscription(HubConnection hub, Func<NotificationRefreshKind, Task> callback, CancellationToken token)
        {
            _hub = hub; _callbacks = new(callback);
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            _handler = hub.On<NotificationsRefreshRequestedV1>("NotificationsRefresh",
                hint => _callbacks.NotifyAsync(NotificationRefreshKind.Refresh, hint.EventId));
            hub.Reconnecting += _ => _callbacks.NotifyAsync(NotificationRefreshKind.TransportFailed);
            hub.Reconnected += _ => _callbacks.NotifyAsync(NotificationRefreshKind.Connected);
            hub.Closed += _ => _callbacks.NotifyAsync(NotificationRefreshKind.TransportFailed);
            _cancellation = _lifetime.Token.Register(() => _ = SetActiveAsync(false, CancellationToken.None));
        }

        public async Task SetActiveAsync(bool active, CancellationToken cancellationToken)
        {
            await _lifecycle.WaitAsync(cancellationToken);
            try
            {
                if (_disposed) return;
                _callbacks.Active = false;
                _run?.Cancel();
                await _startup;
                await _hub.StopAsync(cancellationToken);
                _run?.Dispose(); _run = null;
                if (!active) return;
                _run = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
                _callbacks.Active = true;
                _startup = StartAsync(_run.Token);
            }
            finally { _lifecycle.Release(); }
        }

        private async Task StartAsync(CancellationToken token)
        {
            // Automatic reconnect covers established connections, not initial startup.
            // Four attempts; snapshot deadline polling continues after exhaustion.
            for (var attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    if (attempt > 0) await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), token);
                    await _hub.StartAsync(token);
                    // Do not make startup await UI callbacks: auth changes may stop it.
                    _ = NotifySafelyAsync(NotificationRefreshKind.Connected);
                    return;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception)
                {
                    if (token.IsCancellationRequested) return;
                    _ = NotifySafelyAsync(NotificationRefreshKind.TransportFailed);
                }
            }
        }

        private async Task NotifySafelyAsync(NotificationRefreshKind kind)
        {
            try { await _callbacks.NotifyAsync(kind); }
            catch (Exception) when (_lifetime.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            await _lifecycle.WaitAsync();
            try
            {
                if (_disposed) return;
                _disposed = true; _callbacks.Active = false;
                _lifetime.Cancel();
                _run?.Cancel(); await _startup;
                _handler.Dispose();
                _cancellation.Dispose();
                await _hub.DisposeAsync();
                _run?.Dispose(); _lifetime.Dispose();
            }
            finally { _lifecycle.Release(); }
            await _callbacks.DisposeAsync();
        }
    }
}
