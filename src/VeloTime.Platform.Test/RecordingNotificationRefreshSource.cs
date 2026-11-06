using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.Platform.Test;

internal sealed class RecordingNotificationRefreshSource : INotificationRefreshSource, INotificationRefreshSubscription
{
    public Func<NotificationRefreshKind, Task>? Callback { get; private set; }
    public int Subscriptions { get; private set; }
    public bool Disposed { get; private set; }
    public NotificationRefreshKind ActivationKind { get; set; } = NotificationRefreshKind.Connected;
    public List<bool> Activations { get; } = [];
    public ValueTask<INotificationRefreshSubscription> SubscribeAsync(
        Func<NotificationRefreshKind, Task> callback, CancellationToken cancellationToken)
    {
        Subscriptions++; Callback = callback;
        return ValueTask.FromResult<INotificationRefreshSubscription>(this);
    }
    public Task SetActiveAsync(bool active, CancellationToken cancellationToken)
    {
        Activations.Add(active);
        return active ? Callback!(ActivationKind) : Task.CompletedTask;
    }
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}
