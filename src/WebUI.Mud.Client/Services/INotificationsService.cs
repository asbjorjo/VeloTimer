using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

public interface INotificationsService
{
    Task<NotificationUiSnapshot> ActiveAsync(CancellationToken ct);
    Task<IReadOnlyList<NotificationDismissalListItemDTO>> DismissalsAsync(CancellationToken ct);
    Task<IReadOnlyList<NotificationAdministrationDTO>> AdministrationAsync(CancellationToken ct);
    Task<NotificationDismissalDTO> DismissAsync(Guid id, NotificationDismissalScope scope, CancellationToken ct);
    Task<RestoreNotificationDismissalDTO> RestoreAsync(Guid id, NotificationRestoreScope scope, CancellationToken ct);
    Task<NotificationAdministrationDTO> GetAsync(Guid id, CancellationToken ct);
    Task<NotificationAdministrationDTO> CreateAsync(CreateNotificationRequest request, CancellationToken ct);
    Task<NotificationAdministrationDTO> UpdateAsync(Guid id, UpdateNotificationRequest request, CancellationToken ct);
    Task<NotificationAdministrationDTO> EndAsync(Guid id, long version, CancellationToken ct);
}

public sealed record NotificationUiSnapshot(ActiveNotificationsDTO Snapshot, bool Authenticated, bool HasSession = false);

public sealed class NotificationUiException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public string ResourceKey => Status switch
    {
        401 => "SignInRequired",
        403 when Code == "notifications.login_session_required" => "NotificationsLoginRequired",
        403 => "NoPermission",
        404 => "NotificationsNotAvailable",
        409 when Code == "notifications.version_conflict" => "NotificationsVersionConflict",
        409 => "NotificationsSnapshotChanged",
        400 => Code switch
        {
            "notifications.invalid_content" => "NotificationsInvalidContent",
            "notifications.invalid_audience" => "NotificationsInvalidAudience",
            "notifications.invalid_schedule" => "NotificationsInvalidSchedule",
            "notifications.invalid_policy" or "notifications.invalid_duration" => "NotificationsInvalidPolicy",
            _ => "NotificationsInvalidRequest"
        },
        _ => "NotificationsFailed"
    };
}
