using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Statistics.Service;

namespace VeloTime.Module.Statistics.Api.Endpoints;

internal static class StatsProfileEndpoints
{
    internal static void MapStatsProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var profiles = endpoints.MapGroup("/profiles")
            .RequireAuthorization("Statistics.Read")
            .WithTags(["StatsProfiles"]);

        profiles.MapGet("", GetAllStatsProfiles)
            .WithName("GetAllStatsProfiles")
            .Produces<List<StatsProfileResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        profiles.MapGet("{userId}", GetStatsProfileById)
            .WithName("GetStatsProfileById")
            .Produces<StatsProfileResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        profiles.MapPost("", CreateStatsProfile)
            .WithName("CreateStatsProfile")
            .Produces<StatsProfileResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization(AdministrationAuthorization.StatisticsPolicy);

        profiles.MapPost("/ensure", EnsureStatsProfile)
            .WithName("EnsureStatsProfile")
            .WithSummary("Create a missing statistics profile without overwriting existing preferences")
            .WithDescription("Returns 201 with a Location header when created, or 200 with the existing profile unchanged. Requested name and visibility are used only on creation.")
            .Produces<StatsProfileResponse>(StatusCodes.Status200OK)
            .Produces<StatsProfileResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .RequireAuthorization(AdministrationAuthorization.StatisticsPolicy);

        profiles.MapPut("{userId}", UpdateStatsProfile)
            .WithName("UpdateStatsProfile")
            .RequireAuthorization(AdministrationAuthorization.StatisticsPolicy)
            .Produces<StatsProfileResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        profiles.MapDelete("{userId}", DeleteStatsProfile)
            .WithName("DeleteStatsProfile")
            .RequireAuthorization(AdministrationAuthorization.StatisticsPolicy)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Get all statistics profiles
    /// </summary>
    private static async Task<Ok<List<StatsProfileResponse>>> GetAllStatsProfiles(
        StatisticsDbContext storage)
    {
        var profiles = await storage.Set<StatsProfile>()
            .AsNoTracking()
            .Select(p => new StatsProfileResponse(
                p.UserId,
                p.ProfileName,
                p.IsPublic))
            .ToListAsync();

        return TypedResults.Ok(profiles);
    }

    /// <summary>
    /// Get a specific statistics profile by user ID
    /// </summary>
    private static async Task<Results<Ok<StatsProfileResponse>, NotFound>> GetStatsProfileById(
        Guid userId,
        StatisticsDbContext storage)
    {
        var profile = await storage.Set<StatsProfile>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile == null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new StatsProfileResponse(
            profile.UserId,
            profile.ProfileName,
            profile.IsPublic));
    }

    /// <summary>
    /// Create a new statistics profile
    /// </summary>
    private static async Task<Results<Created<StatsProfileResponse>, BadRequest<string>, Conflict<string>>> CreateStatsProfile(
        CreateStatsProfileRequest request,
        StatsProfileCreationService service,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
            return TypedResults.BadRequest("A valid user ID is required.");
        if (string.IsNullOrWhiteSpace(request.ProfileName))
        {
            return TypedResults.BadRequest("Profile name cannot be empty.");
        }

        var (profile, created) = await service.EnsureAsync(request.UserId, request.ProfileName, request.IsPublic, cancellationToken);
        if (!created)
        {
            return TypedResults.Conflict("A profile for this user already exists.");
        }

        return TypedResults.Created(
            $"/api/statistics/profiles/{profile.UserId}",
            new StatsProfileResponse(profile.UserId, profile.ProfileName, profile.IsPublic));
    }

    private static async Task<Results<Created<StatsProfileResponse>, Ok<StatsProfileResponse>, BadRequest<string>>> EnsureStatsProfile(
        CreateStatsProfileRequest request, StatsProfileCreationService service, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
            return TypedResults.BadRequest("A valid user ID is required.");
        if (string.IsNullOrWhiteSpace(request.ProfileName))
            return TypedResults.BadRequest("Profile name cannot be empty.");

        var (profile, created) = await service.EnsureAsync(request.UserId, request.ProfileName, request.IsPublic, cancellationToken);
        var response = new StatsProfileResponse(profile.UserId, profile.ProfileName, profile.IsPublic);
        return created
            ? TypedResults.Created($"/api/statistics/profiles/{profile.UserId}", response)
            : TypedResults.Ok(response);
    }

    /// <summary>
    /// Update an existing statistics profile
    /// </summary>
    private static async Task<Results<Ok<StatsProfileResponse>, NotFound, BadRequest<string>>> UpdateStatsProfile(
        Guid userId,
        UpdateStatsProfileRequest request,
        StatisticsDbContext storage)
    {
        if (string.IsNullOrWhiteSpace(request.ProfileName))
        {
            return TypedResults.BadRequest("Profile name cannot be empty.");
        }

        var profile = await storage.Set<StatsProfile>()
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile == null)
        {
            return TypedResults.NotFound();
        }

        // Update the properties on the existing profile
        profile.ProfileName = request.ProfileName;
        profile.IsPublic = request.IsPublic;

        await storage.SaveChangesAsync();

        return TypedResults.Ok(new StatsProfileResponse(
            profile.UserId,
            profile.ProfileName,
            profile.IsPublic));
    }

    /// <summary>
    /// Delete a statistics profile
    /// </summary>
    private static async Task<Results<NoContent, NotFound>> DeleteStatsProfile(
        Guid userId,
        StatisticsDbContext storage)
    {
        var profile = await storage.Set<StatsProfile>()
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile == null)
        {
            return TypedResults.NotFound();
        }

        storage.Set<StatsProfile>().Remove(profile);
        await storage.SaveChangesAsync();

        return TypedResults.NoContent();
    }
}

/// <summary>
/// Response DTO for StatsProfile
/// </summary>
public record StatsProfileResponse(
    Guid UserId,
    string ProfileName,
    bool IsPublic);

/// <summary>
/// Request DTO for creating a new StatsProfile
/// </summary>
public record CreateStatsProfileRequest(
    Guid UserId,
    string ProfileName,
    bool IsPublic = true);

/// <summary>
/// Request DTO for updating an existing StatsProfile
/// </summary>
public record UpdateStatsProfileRequest(
    string ProfileName,
    bool IsPublic);
