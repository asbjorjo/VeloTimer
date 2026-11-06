using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Data;
using VeloTime.Module.Notifications.Interface.Identity;

namespace VeloTime.Module.Notifications.Service;

public static class NotificationValidation
{
    public static void Identity(NotificationReaderIdentity identity)
    {
        Require(!string.IsNullOrWhiteSpace(identity.Issuer) && !string.IsNullOrWhiteSpace(identity.Subject) &&
            identity.Roles is not null && identity.Roles.All(r => !string.IsNullOrWhiteSpace(r)),
            NotificationErrorCodes.InvalidIdentity, 403);
    }

    public static string SessionId(NotificationReaderIdentity identity)
    {
        Identity(identity);
        Require(!string.IsNullOrWhiteSpace(identity.LoginSessionId) && identity.LoginSessionId.Length <= 256,
            NotificationErrorCodes.LoginSessionRequired, 403);
        return identity.LoginSessionId!;
    }

    public static NotificationDismissalPolicyDTO DefaultPolicy(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Info => new([NotificationDismissalScope.Session, NotificationDismissalScope.Permanent],
            NotificationDismissalScope.Permanent, null),
        NotificationSeverity.Warning => new([NotificationDismissalScope.Duration, NotificationDismissalScope.Session,
            NotificationDismissalScope.Permanent], NotificationDismissalScope.Duration, NotificationLimits.WarningDurationSeconds),
        NotificationSeverity.Error => new([NotificationDismissalScope.Duration, NotificationDismissalScope.Session,
            NotificationDismissalScope.Permanent], NotificationDismissalScope.Duration, NotificationLimits.ErrorDurationSeconds),
        _ => throw new NotificationException(400, NotificationErrorCodes.InvalidSeverity)
    };

    public static void Authoring(NotificationContentDTO content, NotificationSeverity severity,
        NotificationAudienceDTO audience, DateTimeOffset start, DateTimeOffset? end, NotificationDismissalPolicyDTO policy)
    {
        Require(content is not null && ValidText(content.English) && ValidText(content.NorwegianBokmal),
            NotificationErrorCodes.InvalidContent);
        Require(Enum.IsDefined(severity), NotificationErrorCodes.InvalidSeverity);
        Require(audience is not null && audience.Kind switch
        {
            NotificationAudienceKind.Everyone or NotificationAudienceKind.Authenticated =>
                audience.Role is null && audience.RecipientIssuer is null && audience.RecipientSubject is null,
            NotificationAudienceKind.Role => !string.IsNullOrWhiteSpace(audience.Role) &&
                audience.RecipientIssuer is null && audience.RecipientSubject is null,
            NotificationAudienceKind.User => audience.Role is null && !string.IsNullOrWhiteSpace(audience.RecipientIssuer) &&
                !string.IsNullOrWhiteSpace(audience.RecipientSubject),
            _ => false
        }, NotificationErrorCodes.InvalidAudience);
        Require(start != default && start < DateTimeOffset.MaxValue && start.Offset == TimeSpan.Zero &&
            (end is null || end.Value < DateTimeOffset.MaxValue && end.Value.Offset == TimeSpan.Zero &&
                NotificationTimestamp.Persisted(end.Value) > NotificationTimestamp.Persisted(start)),
            NotificationErrorCodes.InvalidSchedule);
        Require(policy is not null && policy.AllowedScopes is { Count: > 0 } &&
            policy.AllowedScopes.All(Enum.IsDefined) &&
            policy.AllowedScopes.Distinct().Count() == policy.AllowedScopes.Count &&
            policy.AllowedScopes.Contains(policy.DefaultScope), NotificationErrorCodes.InvalidPolicy);
        Require(policy!.AllowedScopes.Contains(NotificationDismissalScope.Duration)
            ? policy.DurationSeconds is >= NotificationLimits.MinimumDurationSeconds and <= NotificationLimits.MaximumDurationSeconds
            : policy.DurationSeconds is null, NotificationErrorCodes.InvalidDuration);
    }

    private static bool ValidText(NotificationTextDTO? text) => text is not null &&
        !string.IsNullOrWhiteSpace(text.Title) && text.Title.Length <= NotificationLimits.MaximumTitleLength &&
        !string.IsNullOrWhiteSpace(text.Body) && text.Body.Length <= NotificationLimits.MaximumBodyLength;

    public static void Require(bool valid, string code, int status = 400)
    {
        if (!valid) throw new NotificationException(status, code);
    }
}
