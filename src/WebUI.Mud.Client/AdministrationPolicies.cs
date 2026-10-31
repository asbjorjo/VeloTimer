using Microsoft.AspNetCore.Authorization;

namespace VeloTime.WebUI.Mud.Client;

public static class AdministrationPolicies
{
    public const string PermissionClaimType = "velotime_permission";
    public const string GlobalPermission = "velotime.admin";
    public const string FacilitiesPermission = "facilities.admin";
    public const string TimingPermission = "timing.admin";
    public const string StatisticsPermission = "statistics.admin";
    public const string TimingLinkPermission = "coursepoint-timing-link.write";
    public const string ReferencesPermission = "administration.references.read";

    public const string Facilities = "Facilities.Admin";
    public const string Timing = "Timing.Admin";
    public const string Statistics = "Statistics.Admin";
    public const string TimingLink = "CoursePointTimingLink.Write";
    public const string References = "Administration.References";
    public const string TransponderStatusAdmin = "Transponder.Status.Admin";

    public static void Configure(AuthorizationOptions options)
    {
        Add(options, Facilities, GlobalPermission, FacilitiesPermission);
        Add(options, Timing, GlobalPermission, TimingPermission);
        Add(options, Statistics, GlobalPermission, StatisticsPermission);
        Add(options, TimingLink, GlobalPermission, FacilitiesPermission, TimingPermission);
        Add(options, References, GlobalPermission, FacilitiesPermission, TimingPermission, StatisticsPermission);
        Add(options, TransponderStatusAdmin, GlobalPermission, FacilitiesPermission, TimingPermission, StatisticsPermission);
    }

    private static void Add(AuthorizationOptions options, string policy, params string[] permissions) =>
        options.AddPolicy(policy, builder => builder.RequireAuthenticatedUser().RequireAssertion(context =>
            permissions.Any(permission => context.User.HasClaim(PermissionClaimType, permission))));
}