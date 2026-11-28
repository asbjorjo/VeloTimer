namespace VeloTime.Module.Notifications.Interface.Identity;

/// <summary>
/// Internal service input only, NOT an HTTP DTO. Construct exclusively from a validated
/// Keycloak JWT; exact ordinal comparisons. A missing sid is permitted for account-level
/// operations, but cannot address session dismissal state.
/// </summary>
public sealed record NotificationReaderIdentity(
    string Issuer,
    string Subject,
    IReadOnlyList<string> Roles,
    string? LoginSessionId);

/// <summary>
/// API-phase authentication adapter. Null means truly anonymous, not invalid credentials.
/// Authenticated issuer/subject and roles come from the validated JWT. A nonblank sid is
/// optional except for operations that address current-session dismissal state.
/// </summary>
public interface INotificationReaderIdentityAccessor
{
    NotificationReaderIdentity? GetCurrent();
}
