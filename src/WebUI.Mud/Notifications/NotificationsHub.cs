using Microsoft.AspNetCore.SignalR;
using SlimMessageBus;
using VeloTime.Module.Notifications.Interface.Messages;

namespace VeloTime.WebUI.Mud.Notifications;

// Anonymous is deliberate: only a public, minimal invalidation is broadcast.
// No client methods, identity, content, or selectable groups.
public sealed class NotificationsHub : Hub { }

public sealed class NotificationsRefreshBridge(IHubContext<NotificationsHub> hub)
    : IConsumer<NotificationsRefreshRequestedV1>
{
    public Task OnHandle(NotificationsRefreshRequestedV1 message, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync("NotificationsRefresh", message, cancellationToken);
}
