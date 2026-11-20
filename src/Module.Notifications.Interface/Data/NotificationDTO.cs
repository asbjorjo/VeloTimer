namespace VeloTime.Module.Notifications.Interface.Data;

/// <summary>Admin-only targeting. Unused fields must be null, not empty strings.</summary>
public sealed record NotificationAudienceDTO(
    NotificationAudienceKind Kind,
    string? Role = null,
    string? RecipientIssuer = null,
    string? RecipientSubject = null);

/// <summary>Plain text in both initially supported locales; no HTML or template execution.</summary>
public sealed record NotificationTextDTO(string Title, string Body);

public sealed record NotificationContentDTO(NotificationTextDTO English, NotificationTextDTO NorwegianBokmal);

/// <summary>Effective persisted policy. Duration is required iff Duration is allowed.</summary>
public sealed record NotificationDismissalPolicyDTO(
    IReadOnlyList<NotificationDismissalScope> AllowedScopes,
    NotificationDismissalScope DefaultScope,
    int? DurationSeconds);

/// <summary>Reader-safe projection: deliberately excludes audience, recipient IDs and author metadata.</summary>
public sealed record NotificationPresentationDTO(
    Guid Id,
    long Version,
    NotificationContentDTO Content,
    NotificationSeverity Severity,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset? EndsAtUtc,
    NotificationDismissalPolicyDTO DismissalPolicy);

/// <summary>
/// Bounded, revision-checked snapshot page. NextChangeAtUtc covers all matching notices,
/// including future starts and suppressed snoozes, not just this page. See README for restart rules.
/// </summary>
public sealed record ActiveNotificationsDTO(
    DateTimeOffset ServerNowUtc,
    DateTimeOffset AsOfUtc,
    DateTimeOffset SnapshotExpiresAtUtc,
    DateTimeOffset? NextChangeAtUtc,
    IReadOnlyList<NotificationPresentationDTO> Items,
    string? ContinuationToken);

/// <summary>Admin-only projection, never returned by active or dismissal-management endpoints.</summary>
public sealed record NotificationAdministrationDTO(
    Guid Id,
    long Version,
    NotificationContentDTO Content,
    NotificationSeverity Severity,
    NotificationAudienceDTO Audience,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset? EndsAtUtc,
    NotificationDismissalPolicyDTO DismissalPolicy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Null policy selects the severity defaults; non-null is a complete override.</summary>
public sealed record CreateNotificationRequest(
    NotificationContentDTO Content,
    NotificationSeverity Severity,
    NotificationAudienceDTO Audience,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset? EndsAtUtc,
    NotificationDismissalPolicyDTO? DismissalPolicy = null);

/// <summary>Full replacement with optimistic concurrency. Effective policy must be explicit.</summary>
public sealed record UpdateNotificationRequest(
    long ExpectedVersion,
    NotificationContentDTO Content,
    NotificationSeverity Severity,
    NotificationAudienceDTO Audience,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset? EndsAtUtc,
    NotificationDismissalPolicyDTO DismissalPolicy);

/// <summary>End immediately at server UTC now; no physical delete or UUID reuse.</summary>
public sealed record EndNotificationRequest(long ExpectedVersion);

/// <summary>pageSize and continuationToken are query parameters; never identity selectors.</summary>
public sealed record NotificationPageRequest(
    int PageSize = NotificationLimits.DefaultPageSize,
    string? ContinuationToken = null);

public sealed record NotificationAdministrationPageDTO(
    DateTimeOffset ServerNowUtc,
    DateTimeOffset AsOfUtc,
    DateTimeOffset SnapshotExpiresAtUtc,
    IReadOnlyList<NotificationAdministrationDTO> Items,
    string? ContinuationToken);
