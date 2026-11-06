using VeloTime.Module.Notifications.Interface;

namespace VeloTime.Module.Notifications.Model;

/// <summary>
/// One account-level choice per exact issuer/subject/notice key. Duration and permanent
/// are mutually exclusive; a session choice is stored separately.
/// </summary>
public sealed class NotificationAccountDismissal
{
    public Guid Id { get; set; }
    public required string Issuer { get; set; }
    public required string Subject { get; set; }
    public Guid NotificationId { get; set; }
    public NotificationDismissalScope Scope { get; set; }
    public DateTimeOffset DismissedAtUtc { get; set; }
    public DateTimeOffset? DismissedUntilUtc { get; set; }
}
