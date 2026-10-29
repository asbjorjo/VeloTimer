using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Endpoints;

internal static class OwnerProfileEndpoints
{
    internal static void MapOwnerProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/owner-profiles/me", GetOwnProfile).RequireAuthorization()
            .WithName("GetOwnOwnerProfile").WithSummary("Get the authenticated user's Statistics display name");
        var admin = endpoints.MapGroup("/admin/owner-profiles").RequireAuthorization(AdministrationAuthorization.TimingPolicy);
        admin.MapGet("/search", Search).WithName("SearchOwnerProfiles")
            .WithSummary("Search owner profile names for transponder administration");
        admin.MapPost("/resolve", Resolve).WithName("ResolveOwnerProfiles")
            .WithSummary("Resolve a bounded batch of owner profile names for transponder administration");
    }

    private static async Task<Results<Ok<OwnerProfileDTO>, ForbidHttpResult>> GetOwnProfile(
        ClaimsPrincipal user, StatisticsDbContext storage, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || id == Guid.Empty)
            return TypedResults.Forbid();
        var name = await storage.Set<StatsProfile>().AsNoTracking().Where(profile => profile.UserId == id)
            .Select(profile => profile.ProfileName).SingleOrDefaultAsync(cancellationToken);
        return TypedResults.Ok(new OwnerProfileDTO(id, name));
    }

    private static async Task<Results<Ok<OwnerProfilePage>, ProblemHttpResult>> Search(
        string query, int offset, int pageSize, StatisticsDbContext storage, CancellationToken cancellationToken)
    {
        query = query.Trim();
        if (query.Length is < 2 or > 100 || offset < 0 || pageSize is < 1 or > 20 || offset > int.MaxValue - pageSize)
            return TypedResults.Problem(detail: "Search requires 2-100 characters, a nonnegative offset, and a page size of 1-20.", statusCode: 400);
        var term = query.ToUpperInvariant();
        var profiles = await storage.Set<StatsProfile>().AsNoTracking()
            .Where(profile => profile.ProfileName.ToUpper().Contains(term))
            .OrderBy(profile => profile.ProfileName).ThenBy(profile => profile.UserId)
            .Skip(offset).Take(pageSize + 1).Select(profile => new OwnerProfileDTO(profile.UserId, profile.ProfileName))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new OwnerProfilePage(profiles.Take(pageSize).ToList(),
            profiles.Count > pageSize ? offset + pageSize : null));
    }

    private static async Task<Results<Ok<IReadOnlyList<OwnerProfileDTO>>, ProblemHttpResult>> Resolve(
        ResolveOwnerProfilesRequest request, StatisticsDbContext storage, CancellationToken cancellationToken)
    {
        if (request.UserIds is null || request.UserIds.Count > 100 || request.UserIds.Any(id => id == Guid.Empty))
            return TypedResults.Problem(detail: "Provide at most 100 valid owner IDs.", statusCode: 400);
        var ids = request.UserIds.Distinct().ToArray();
        var profiles = await storage.Set<StatsProfile>().AsNoTracking().Where(profile => ids.Contains(profile.UserId))
            .Select(profile => new OwnerProfileDTO(profile.UserId, profile.ProfileName)).ToListAsync(cancellationToken);
        return TypedResults.Ok<IReadOnlyList<OwnerProfileDTO>>(profiles);
    }
}
