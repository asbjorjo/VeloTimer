namespace VeloTime.Module.Notifications.Model;

public enum NotificationScheduledWorkKind
{
    NotificationStart = 1,
    NotificationEnd = 2,
    AccountDismissalExpiry = 3,
    SessionDismissalExpiry = 4
}

/// <summary>
/// Durable wake-up slot for one current deadline. Notification start/end slots use
/// SourceId = NotificationId; dismissal-expiry slots use the dismissal row Id.
/// A reschedule increments Generation and clears the old lease/completion. Workers
/// fence claims with both generation and ClaimId, recheck source rows in the same
/// transaction, and commit completion plus a refresh-outbox insert atomically.
/// </summary>
public sealed class NotificationScheduledWork
{
    public Guid Id { get; set; }
    public Guid NotificationId { get; set; }
    public NotificationScheduledWorkKind Kind { get; set; }
    public Guid SourceId { get; set; }
    public long Generation { get; set; } = 1;
    public DateTimeOffset DueAtUtc { get; set; }
    public Guid? ClaimId { get; set; }
    public DateTimeOffset? ClaimedUntilUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
