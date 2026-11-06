namespace VeloTime.Module.Notifications.Model;

/// <summary>
/// Singleton revision fence for bounded revision-checked snapshots. Any tracked
/// notification/account/session dismissal write advances this row in the same
/// SaveChanges transaction; all snapshot operations may conservatively bind it.
/// </summary>
public sealed class NotificationSnapshotRevision
{
    public int Id { get; set; } = 1;
    public long Revision { get; set; }
}
