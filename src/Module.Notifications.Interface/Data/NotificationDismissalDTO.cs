namespace VeloTime.Module.Notifications.Interface.Data;

/// <summary>The caller picks only a permitted scope, never duration, expiry or identity.</summary>
public sealed record DismissNotificationRequest(NotificationDismissalScope Scope);

/// <summary>Account state is Duration or Permanent; current-session state is Session only.</summary>
public sealed record NotificationDismissalStateDTO(
    NotificationDismissalScope Scope,
    DateTimeOffset DismissedAtUtc,
    DateTimeOffset? DismissedUntilUtc);

/// <summary>No issuer, subject or login-session ID is exposed.</summary>
public sealed record NotificationDismissalDTO(
    Guid NotificationId,
    DateTimeOffset ServerNowUtc,
    NotificationDismissalStateDTO? Account,
    NotificationDismissalStateDTO? CurrentSession);

/// <summary>Show-again response includes remaining state; restore need not make the notice active.</summary>
public sealed record RestoreNotificationDismissalDTO(
    NotificationDismissalDTO Dismissal,
    bool IsActive);

/// <summary>Audience-authorized management view, including future/ended notices with valid state.</summary>
public sealed record NotificationDismissalListItemDTO(
    NotificationPresentationDTO Notification,
    NotificationDismissalStateDTO? Account,
    NotificationDismissalStateDTO? CurrentSession);

public sealed record NotificationDismissalsPageDTO(
    DateTimeOffset ServerNowUtc,
    DateTimeOffset AsOfUtc,
    DateTimeOffset SnapshotExpiresAtUtc,
    IReadOnlyList<NotificationDismissalListItemDTO> Items,
    string? ContinuationToken);
