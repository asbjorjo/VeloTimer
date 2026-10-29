using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace VeloTime.Module.Common;

public static class AdministrationAuthorization
{
    public const string PermissionClaimType = "velotime_permission";
    public const string GlobalPermission = "velotime.admin";
    public const string FacilitiesPermission = "facilities.admin";
    public const string TimingPermission = "timing.admin";
    public const string StatisticsPermission = "statistics.admin";
    public const string TimingLinkPermission = "coursepoint-timing-link.write";
    public const string ReferencesPermission = "administration.references.read";
    public const string OwnershipReadPermission = "timing.ownership.read";

    public const string FacilitiesPolicy = "Facilities.Admin";
    public const string TimingPolicy = "Timing.Admin";
    public const string StatisticsPolicy = "Statistics.Admin";
    public const string TimingLinkPolicy = "CoursePointTimingLink.Write";
    public const string ReferencePolicy = "Administration.References";
    public const string OwnershipReadPolicy = "Timing.Ownership.Read";

    public static IServiceCollection AddAdministrationAuthorization(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Transient<IClaimsTransformation, RealmPermissionClaimsTransformation>());
        services.AddAuthorizationBuilder()
            .AddPolicy(FacilitiesPolicy, policy => RequireAnyPermission(policy, GlobalPermission, FacilitiesPermission))
            .AddPolicy(TimingPolicy, policy => RequireAnyPermission(policy, GlobalPermission, TimingPermission))
            .AddPolicy(StatisticsPolicy, policy => RequireAnyPermission(policy, GlobalPermission, StatisticsPermission))
            .AddPolicy(TimingLinkPolicy, policy => RequireAnyPermission(policy, GlobalPermission, TimingLinkPermission))
            .AddPolicy(ReferencePolicy, policy => RequireAnyPermission(policy, GlobalPermission, ReferencesPermission))
            .AddPolicy(OwnershipReadPolicy, policy => RequireAnyPermission(policy, GlobalPermission, TimingPermission, OwnershipReadPermission));
        return services;
    }

    public static bool HasPermission(ClaimsPrincipal principal, string permission) =>
        principal.HasClaim(PermissionClaimType, permission);

    private static void RequireAnyPermission(AuthorizationPolicyBuilder policy, params string[] permissions) =>
        policy.RequireAuthenticatedUser().RequireAssertion(context => permissions.Any(permission => HasPermission(context.User, permission)));
}

public sealed class RealmPermissionClaimsTransformation : IClaimsTransformation
{
    private static readonly IReadOnlyDictionary<string, string[]> PermissionsByRealmRole = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["statistics-processor"] = [AdministrationAuthorization.OwnershipReadPermission],
        ["velotime-admin"] = [
            AdministrationAuthorization.GlobalPermission,
            AdministrationAuthorization.FacilitiesPermission,
            AdministrationAuthorization.TimingPermission,
            AdministrationAuthorization.StatisticsPermission,
            AdministrationAuthorization.TimingLinkPermission,
            AdministrationAuthorization.ReferencesPermission],
        ["facilities-admin"] = [AdministrationAuthorization.FacilitiesPermission, AdministrationAuthorization.TimingLinkPermission, AdministrationAuthorization.ReferencesPermission],
        ["timing-admin"] = [AdministrationAuthorization.TimingPermission, AdministrationAuthorization.TimingLinkPermission, AdministrationAuthorization.ReferencesPermission],
        ["statistics-admin"] = [AdministrationAuthorization.StatisticsPermission, AdministrationAuthorization.ReferencesPermission]
    };

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        foreach (var identity in principal.Identities.Where(identity => identity.IsAuthenticated))
        {
            foreach (var realmAccess in identity.FindAll("realm_access").ToArray())
            {
                try
                {
                    using var document = JsonDocument.Parse(realmAccess.Value);
                    if (document.RootElement.ValueKind != JsonValueKind.Object ||
                        !document.RootElement.TryGetProperty("roles", out var roles) ||
                        roles.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var role in roles.EnumerateArray())
                    {
                        if (role.ValueKind != JsonValueKind.String || role.GetString() is not { Length: > 0 } roleName ||
                            !PermissionsByRealmRole.TryGetValue(roleName, out var permissions))
                        {
                            continue;
                        }

                        foreach (var permission in permissions)
                        {
                            if (!identity.HasClaim(AdministrationAuthorization.PermissionClaimType, permission))
                            {
                                identity.AddClaim(new Claim(AdministrationAuthorization.PermissionClaimType, permission, ClaimValueTypes.String, realmAccess.Issuer));
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                }
            }
        }

        return Task.FromResult(principal);
    }
}