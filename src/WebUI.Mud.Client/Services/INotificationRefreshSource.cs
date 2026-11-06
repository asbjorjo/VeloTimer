namespace VeloTime.WebUI.Mud.Client.Services;

// UI invalidations only; never notification content or user identity.
public enum NotificationRefreshKind { Refresh, Connected, TransportFailed }

public interface INotificationRefreshSource
{
    // Called on first interactive render, not during prerender. Returns inactive;
    // callbacks start only after SetActiveAsync(true), once the host owns the handle.
    ValueTask<INotificationRefreshSubscription> SubscribeAsync(
        Func<NotificationRefreshKind, Task> callback, CancellationToken cancellationToken);
}

public interface INotificationRefreshSubscription : IAsyncDisposable
{
    // Suspend on logout; restarting also repairs hints missed during auth changes.
    Task SetActiveAsync(bool active, CancellationToken cancellationToken);
}
