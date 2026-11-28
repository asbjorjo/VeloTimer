namespace VeloTime.Module.Notifications.Model;

/// <summary>
/// Session dismissal is keyed by validated Keycloak sid. This row represents session scope only.
/// </summary>
public sealed class NotificationSessionDismissal
{
    public Guid Id { get; set; }
    public required string Issuer { get; set; }
    public required string Subject { get; set; }
    public Guid NotificationId { get; set; }
    public required string LoginSessionId { get; set; }
    public DateTimeOffset DismissedAtUtc { get; set; }
}
