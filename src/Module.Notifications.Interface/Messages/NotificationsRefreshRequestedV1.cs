namespace VeloTime.Module.Notifications.Interface.Messages;

/// <summary>
/// Static broadcast invalidation, not content delivery. Every connected viewer refreshes its
/// authorized snapshot. No notification ID, target, recipient, session or change reason is exposed.
/// EventId identifies a durable outbox entry and remains stable across retries.
/// </summary>
public sealed record NotificationsRefreshRequestedV1(Guid EventId);

/// <summary>Shared broker contract for topology, processor and WebUI bridge owners.</summary>
public static class NotificationMessaging
{
    public const string Topic = "velotime-notifications";
    public const string WebUiSubscription = "webui-notifications";
    public const string MessageNameHeader = "MessageName";
    public const string RefreshMessageName = nameof(NotificationsRefreshRequestedV1);
    public const string RefreshSqlFilter = "MessageName = 'NotificationsRefreshRequestedV1'";
    public const bool RequiresSession = false;
    public const string RefreshHubMethod = "NotificationsRefresh";
}
