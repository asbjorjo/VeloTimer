using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Data;

namespace VeloTime.Module.Notifications.Client;

public static class NotificationClientExtensions
{
    /// <summary>Strongly typed restore scope; no caller identity or expiry is accepted.</summary>
    public static Task<RestoreNotificationDismissalDTO> RestoreAsync(this INotificationsClient client, Guid id,
        NotificationRestoreScope scope, CancellationToken ct = default) =>
        client.RestoreNotificationDismissalAsync(id, scope switch
        {
            NotificationRestoreScope.Account => "account",
            NotificationRestoreScope.CurrentSession => "currentSession",
            NotificationRestoreScope.AccountAndCurrentSession => "accountAndCurrentSession",
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        }, ct);
}
