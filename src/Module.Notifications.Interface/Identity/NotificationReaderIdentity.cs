namespace VeloTime.Module.Notifications.Interface.Identity;

/// <summary>
/// Internal service input only, NOT an HTTP DTO. Construct exclusively from validated
/// authentication and a trusted application-login session; exact ordinal comparisons.
/// </summary>
public sealed record NotificationReaderIdentity(
    string Issuer,
    string Subject,
    IReadOnlyList<string> Roles,
    string LoginSessionId);

/// <summary>
/// API-phase authentication adapter. Null means truly anonymous, not invalid credentials.
/// Authenticated missing issuer/subject or trusted login-session identity must fail explicitly,
/// never downgrade to anonymous or ignore persisted session dismissals.
/// </summary>
public interface INotificationReaderIdentityAccessor
{
    NotificationReaderIdentity? GetCurrent();
}
