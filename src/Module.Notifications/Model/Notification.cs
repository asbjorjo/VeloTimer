using VeloTime.Module.Notifications.Interface;

namespace VeloTime.Module.Notifications.Model;

/// <summary>
/// Authored notice and its effective (creation-time) dismissal policy.
/// Dismissals remain attached to this UUID when the presentation is edited.
/// </summary>
public sealed class Notification
{
    public Guid Id { get; set; }
    public long Version { get; set; } = 1;

    public required string EnglishTitle { get; set; }
    public required string EnglishBody { get; set; }
    public required string NorwegianBokmalTitle { get; set; }
    public required string NorwegianBokmalBody { get; set; }
    public NotificationSeverity Severity { get; set; }

    public NotificationAudienceKind AudienceKind { get; set; }
    public string? AudienceRole { get; set; }
    public string? RecipientIssuer { get; set; }
    public string? RecipientSubject { get; set; }

    public DateTimeOffset StartsAtUtc { get; set; }
    public DateTimeOffset? EndsAtUtc { get; set; }

    public bool AllowsDurationDismissal { get; set; }
    public bool AllowsSessionDismissal { get; set; }
    public bool AllowsPermanentDismissal { get; set; }
    public NotificationDismissalScope DefaultDismissalScope { get; set; }
    public int? DismissalDurationSeconds { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
