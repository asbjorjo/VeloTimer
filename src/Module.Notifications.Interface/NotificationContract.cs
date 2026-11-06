using System.Text.Json.Serialization;

namespace VeloTime.Module.Notifications.Interface;

/// <summary>Stable string-only enum encoding, independent of host serializer options.</summary>
public sealed class NotificationEnumConverter<T> : JsonStringEnumConverter<T> where T : struct, Enum
{
    public NotificationEnumConverter() : base(namingPolicy: null, allowIntegerValues: false) { }
}

[JsonConverter(typeof(NotificationEnumConverter<NotificationSeverity>))]
public enum NotificationSeverity
{
    [JsonStringEnumMemberName("info")] Info = 1,
    [JsonStringEnumMemberName("warning")] Warning = 2,
    [JsonStringEnumMemberName("error")] Error = 3
}

[JsonConverter(typeof(NotificationEnumConverter<NotificationAudienceKind>))]
public enum NotificationAudienceKind
{
    [JsonStringEnumMemberName("everyone")] Everyone = 1,
    [JsonStringEnumMemberName("authenticated")] Authenticated = 2,
    [JsonStringEnumMemberName("role")] Role = 3,
    [JsonStringEnumMemberName("user")] User = 4
}

[JsonConverter(typeof(NotificationEnumConverter<NotificationDismissalScope>))]
public enum NotificationDismissalScope
{
    [JsonStringEnumMemberName("duration")] Duration = 1,
    [JsonStringEnumMemberName("session")] Session = 2,
    [JsonStringEnumMemberName("permanent")] Permanent = 3
}

[JsonConverter(typeof(NotificationEnumConverter<NotificationRestoreScope>))]
public enum NotificationRestoreScope
{
    [JsonStringEnumMemberName("account")] Account = 1,
    [JsonStringEnumMemberName("currentSession")] CurrentSession = 2,
    [JsonStringEnumMemberName("accountAndCurrentSession")] AccountAndCurrentSession = 3
}

public static class NotificationRoutes
{
    public const string Active = "/api/notifications/active";
    public const string Dismissal = "/api/notifications/{id:guid}/dismissal";
    public const string Dismissals = "/api/notifications/dismissals";
    public const string Administration = "/api/admin/notifications";
    public const string AdministrationItem = "/api/admin/notifications/{id:guid}";
    public const string End = "/api/admin/notifications/{id:guid}/end";
}

/// <summary>Use the existing permission transformation; no new Administrator role.</summary>
public static class NotificationAuthorization
{
    public const string AdministrationPolicy = "Notifications.Admin";
    public const string GlobalAdministrationPermission = "velotime.admin";
    public const string PermissionClaimType = "velotime_permission";
}

public static class NotificationLimits
{
    public const int MinimumDurationSeconds = 1;
    public const int MaximumDurationSeconds = 31_536_000; // 365 SI days
    public const int WarningDurationSeconds = 86_400;
    public const int ErrorDurationSeconds = 3_600;
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 100;
    public const int MaximumSnapshotLifetimeSeconds = 300;
    public const int MaximumTitleLength = 200;
    public const int MaximumBodyLength = 4_000;
}

/// <summary>Stable ProblemDetails extension "code" values; never success-shaped failures.</summary>
public static class NotificationErrorCodes
{
    public const string InvalidIdentity = "notifications.invalid_identity";
    public const string LoginSessionRequired = "notifications.login_session_required";
    public const string InvalidContent = "notifications.invalid_content";
    public const string InvalidSeverity = "notifications.invalid_severity";
    public const string InvalidAudience = "notifications.invalid_audience";
    public const string InvalidSchedule = "notifications.invalid_schedule";
    public const string InvalidPolicy = "notifications.invalid_policy";
    public const string InvalidDuration = "notifications.invalid_duration";
    public const string ScopeNotAllowed = "notifications.scope_not_allowed";
    public const string InvalidRestoreScope = "notifications.invalid_restore_scope";
    public const string InvalidPage = "notifications.invalid_page";
    public const string InvalidContinuation = "notifications.invalid_continuation";
    public const string SnapshotExpired = "notifications.snapshot_expired";
    public const string VersionConflict = "notifications.version_conflict";
    public const string NotFound = "notifications.not_found";
}
