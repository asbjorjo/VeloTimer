using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.WebUI.Mud.Notifications;

// Scoped to the circuit. Auth state belongs to the host's AuthenticationStateProvider;
// no captured HttpContext and no loopback HubConnection.
public sealed class ServerNotificationRefreshSource(NotificationRefreshFanout fanout) : INotificationRefreshSource
{
    public ValueTask<INotificationRefreshSubscription> SubscribeAsync(
        Func<NotificationRefreshKind, Task> callback, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(fanout.Subscribe(callback, cancellationToken));
    }
}
