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
using SlimMessageBus;

namespace VeloTime.Module.Timing.Api.Endpoints;

internal static class InstallationEndpoints
{
    internal static void MapInstallationEndpoints(this IEndpointRouteBuilder builder)
    {
        var installations = builder.MapGroup("/installations");

        installations.MapGet("", ListInstallations);
        installations.MapGet("{id}", GetInstallationById);
        installations.MapGet("{id}/facility-assignments", GetFacilityAssignments).WithName("GetInstallationFacilityAssignments");
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

    /// <summary>The installation's whole facility assignment history, oldest first (plan §4.3).</summary>
    static async Task<Results<Ok<List<InstallationFacilityAssignmentDTO>>, NotFound>> GetFacilityAssignments(Guid id,
        TimingDbContext storage, CancellationToken cancellationToken)
    {
        if (!await storage.Set<Installation>().AnyAsync(i => i.Id == id, cancellationToken)) return TypedResults.NotFound();
        var assignments = await storage.Set<InstallationFacilityAssignment>().AsNoTracking()
            .Where(a => a.InstallationId == id).OrderBy(a => a.ValidFromUtc ?? DateTime.MinValue).ThenBy(a => a.Id).ToListAsync(cancellationToken);
        return TypedResults.Ok(assignments.Select(a => a.ToDto()).ToList());
    }

    static async Task<Results<Created<InstallationDTO>, NotFound>> CreateInstallation(InstallationDTO installationData, TimingDbContext storage,
        TimingAdministrationService administration, HybridCache cache, ICacheInvalidationOperations invalidation,
        ILogger<TimingDbContext> logger, IServiceProvider services, CancellationToken cancellationToken)
    {
        var system = await administration.ValidateInstallationAsync(null, new(installationData.FacilityId == Guid.Empty ? null : installationData.FacilityId,
            installationData.AgentId, installationData.TimingSystem?.Name ?? "", installationData.Description), cancellationToken);
        if (installationData.Id == Guid.Empty || installationData.TimingPoints.Any(point => point.Id == Guid.Empty || string.IsNullOrWhiteSpace(point.SystemId)) ||
            installationData.TimingPoints.Select(point => point.SystemId).Distinct().Count() != installationData.TimingPoints.Count)
            throw new ArgumentException("Installation and timing-point IDs must be valid and hardware system IDs unique.");
        Installation installation = new()
        {
            Id = installationData.Id,
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
        await using var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
        await storage.AddAsync(installation);
        // A new installation's facility covers its whole history, including passings an agent replays later.
        if (installationData.FacilityId != Guid.Empty)
            await InstallationFacilityService.AssignAsync(storage, services.GetRequiredService<IMessageBus>(), installation, installationData.FacilityId, null, cancellationToken);
        var identity = TimingInstallationCacheIdentity.Capture(installation);

        await storage.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
