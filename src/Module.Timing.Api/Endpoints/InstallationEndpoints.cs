using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Mapping;
using VeloTime.Module.Common;
using VeloTime.Module.Timing.Service;

namespace VeloTime.Module.Timing.Api.Endpoints;

internal static class InstallationEndpoints
{
    internal static void MapInstallationEndpoints(this IEndpointRouteBuilder builder)
    {
        var installations = builder.MapGroup("/installations");

        installations.MapGet("", ListInstallations);
        installations.MapGet("{id}", GetInstallationById);
        installations.MapPost("", CreateInstallation).RequireAuthorization(AdministrationAuthorization.TimingPolicy).AddEndpointFilter<AdministrationDependencyFilter>();
        installations.MapPut("{id}", UpdateInstallation).RequireAuthorization(AdministrationAuthorization.TimingPolicy).AddEndpointFilter<AdministrationDependencyFilter>();
        
        installations.MapDelete("{id}", DeleteInstallation).RequireAuthorization(AdministrationAuthorization.TimingPolicy).AddEndpointFilter<AdministrationDependencyFilter>();
    }
    static async Task<Ok<IEnumerable<InstallationDTO>>> ListInstallations(TimingDbContext storage, [FromQuery] Guid? FacilityId)
    {
        var installations = storage.Set<Installation>().AsQueryable();
        if (FacilityId != null && FacilityId != Guid.Empty)
        {
            installations = installations.Where(i => i.Facility == FacilityId);
        }
        IEnumerable<InstallationDTO> installationData = await installations
            .Include(i => i.TimingPoints)
            .Select(i => i.ToDto())
            .ToListAsync();
        return TypedResults.Ok(installationData);
    }

    static async Task<Results<Ok<InstallationDTO>, NotFound>> GetInstallationById(Guid id, TimingDbContext storage)
    {
        var installation = await storage.Set<Installation>()
            .Include(i => i.TimingPoints)
            .FirstOrDefaultAsync(i => i.Id == id);
        if (installation == null)
        {
            return TypedResults.NotFound();
        }
        InstallationDTO installationData = installation.ToDto();
        return TypedResults.Ok(installationData);
    }

    static async Task<Results<Created<InstallationDTO>, NotFound>> CreateInstallation(InstallationDTO installationData, TimingDbContext storage,
        TimingAdministrationService administration, HybridCache cache, ICacheInvalidationOperations invalidation,
        ILogger<TimingDbContext> logger, CancellationToken cancellationToken)
    {
        var system = await administration.ValidateInstallationAsync(null, new(installationData.FacilityId == Guid.Empty ? null : installationData.FacilityId,
            installationData.AgentId, installationData.TimingSystem?.Name ?? "", installationData.Description), cancellationToken);
        if (installationData.Id == Guid.Empty || installationData.TimingPoints.Any(point => point.Id == Guid.Empty || string.IsNullOrWhiteSpace(point.SystemId)) ||
            installationData.TimingPoints.Select(point => point.SystemId).Distinct().Count() != installationData.TimingPoints.Count)
            throw new ArgumentException("Installation and timing-point IDs must be valid and hardware system IDs unique.");
        Installation installation = new()
        {
            Id = installationData.Id,
            Facility = installationData.FacilityId,
            AgentId = installationData.AgentId,
            Description = installationData.Description,
            TimingSystem = system
        };
        var timingPoints = installationData.TimingPoints.Select(tp => new TimingPoint
        {
            Id = tp.Id,
            SystemId = tp.SystemId,
            Installation = installation,
            Description = tp.Description,
        }).ToList();
        installation.TimingPoints.AddRange(timingPoints);
        var identity = TimingInstallationCacheIdentity.Capture(installation);
        await storage.AddAsync(installation);

        await storage.SaveChangesAsync(cancellationToken);
        await TimingCacheInvalidation.InvalidateAsync(cache, logger, identity.Keys, identity.Tags, invalidation);

        return TypedResults.Created($"/api/timing/installations/{installationData.Id}", installation.ToDto());
    }

    static async Task<Results<Ok, NotFound>> UpdateInstallation(Guid id, InstallationDTO installationData, TimingAdministrationService administration, CancellationToken cancellationToken)
    {
        if (installationData.Id != id) throw new ArgumentException("The installation ID must match the route.");
        await administration.SaveInstallationAsync(id, new(installationData.FacilityId == Guid.Empty ? null : installationData.FacilityId,
            installationData.AgentId, installationData.TimingSystem?.Name ?? "", installationData.Description), cancellationToken);
        return TypedResults.Ok();
    }

    static async Task<Results<NoContent, NotFound>> DeleteInstallation(Guid id, TimingAdministrationService administration, CancellationToken cancellationToken)
    {
        await administration.DeleteInstallationAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}
