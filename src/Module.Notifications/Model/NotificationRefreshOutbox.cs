namespace VeloTime.Module.Notifications.Model;

/// <summary>
/// Durable row for the sole initial event, NotificationsRefreshRequestedV1. EventId
/// is also the transport MessageId; claims are leases and publication is at-least-once.
/// </summary>
public sealed class NotificationRefreshOutbox
{
    public Guid EventId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset AvailableAtUtc { get; set; }
    public Guid? ClaimId { get; set; }
    public DateTimeOffset? ClaimedUntilUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
}
