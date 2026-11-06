using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Data;
using VeloTime.Module.Notifications.Model;

namespace VeloTime.Module.Notifications.Mapping;

public static class NotificationMapper
{
    public static NotificationContentDTO Content(Notification n) =>
        new(new(n.EnglishTitle, n.EnglishBody), new(n.NorwegianBokmalTitle, n.NorwegianBokmalBody));

    public static NotificationDismissalPolicyDTO Policy(Notification n)
    {
        List<NotificationDismissalScope> scopes = [];
        if (n.AllowsDurationDismissal) scopes.Add(NotificationDismissalScope.Duration);
        if (n.AllowsSessionDismissal) scopes.Add(NotificationDismissalScope.Session);
        if (n.AllowsPermanentDismissal) scopes.Add(NotificationDismissalScope.Permanent);
        return new(scopes, n.DefaultDismissalScope, n.DismissalDurationSeconds);
    }

    public static NotificationPresentationDTO Presentation(Notification n) =>
        new(n.Id, n.Version, Content(n), n.Severity, n.StartsAtUtc, n.EndsAtUtc, Policy(n));

    public static NotificationAdministrationDTO Administration(Notification n) =>
        new(n.Id, n.Version, Content(n), n.Severity,
            new(n.AudienceKind, n.AudienceRole, n.RecipientIssuer, n.RecipientSubject),
            n.StartsAtUtc, n.EndsAtUtc, Policy(n), n.CreatedAtUtc, n.UpdatedAtUtc);

    public static NotificationDismissalStateDTO? Account(NotificationAccountDismissal? row, DateTimeOffset now) =>
        row is null || row.Scope == NotificationDismissalScope.Duration && row.DismissedUntilUtc <= now
            ? null : new(row.Scope, row.DismissedAtUtc, row.DismissedUntilUtc);

    public static NotificationDismissalStateDTO? Session(NotificationSessionDismissal? row) =>
        row is null ? null : new(NotificationDismissalScope.Session, row.DismissedAtUtc, null);

    public static void Apply(Notification n, NotificationContentDTO content, NotificationSeverity severity,
        NotificationAudienceDTO audience, DateTimeOffset start, DateTimeOffset? end, NotificationDismissalPolicyDTO policy)
    {
        n.EnglishTitle = content.English.Title;
        n.EnglishBody = content.English.Body;
        n.NorwegianBokmalTitle = content.NorwegianBokmal.Title;
        n.NorwegianBokmalBody = content.NorwegianBokmal.Body;
        n.Severity = severity;
        n.AudienceKind = audience.Kind;
        n.AudienceRole = audience.Role;
        n.RecipientIssuer = audience.RecipientIssuer;
        n.RecipientSubject = audience.RecipientSubject;
        n.StartsAtUtc = start;
        n.EndsAtUtc = end;
        n.AllowsDurationDismissal = policy.AllowedScopes.Contains(NotificationDismissalScope.Duration);
        n.AllowsSessionDismissal = policy.AllowedScopes.Contains(NotificationDismissalScope.Session);
        n.AllowsPermanentDismissal = policy.AllowedScopes.Contains(NotificationDismissalScope.Permanent);
        n.DefaultDismissalScope = policy.DefaultScope;
        n.DismissalDurationSeconds = policy.DurationSeconds;
    }
}
