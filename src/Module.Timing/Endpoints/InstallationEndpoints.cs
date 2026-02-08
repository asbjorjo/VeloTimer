using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Mapping;

namespace VeloTime.Module.Timing.Endpoints;

internal static class InstallationEndpoints
{
    internal static void MapInstallationEndpoints(this IEndpointRouteBuilder builder)
    {
        var installations = builder.MapGroup("/installation");

        installations.MapGet("", ListInstallations);
        installations.MapGet("{id}", GetInstallationById);
        installations.MapPost("", CreateInstallation);
        installations.MapPut("{id}", UpdateInstallation);
        
        installations.MapDelete("{id}", DeleteInstallation);
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

    static async Task<Results<Created<InstallationDTO>, NotFound>> CreateInstallation(InstallationDTO installationData, TimingDbContext storage)
    {
        Installation installation = new()
        {
            Id = installationData.Id,
            AgentId = installationData.AgentId,
            Description = installationData.Description,
            TimingSystem = installationData.TimingSystem.ToModel()
        };
        var timingPoints = installationData.TimingPoints.Select(tp => new TimingPoint
        {
            Id = tp.Id,
            SystemId = tp.SystemId,
            Installation = installation,
            Description = tp.Description,
        }).ToList();
        installation.TimingPoints.AddRange(timingPoints);
        await storage.AddAsync(installation);
        
        await storage.SaveChangesAsync();
        
        return TypedResults.Created($"/api/timing/installation/{installationData.Id}", installation.ToDto());
    }

    static async Task<Results<Ok, NotFound>> UpdateInstallation(Guid id, InstallationDTO installationData, TimingDbContext storage)
    {
        var installation = await storage.Set<Installation>()
            .Include(i => i.TimingPoints)
            .FirstOrDefaultAsync(i => i.Id == id);
        
        if (installation == null)
        {
            return TypedResults.NotFound();
        }
        installation.Description = installationData.Description;
 
        await storage.SaveChangesAsync();

        return TypedResults.Ok();
    }

    static async Task<Results<NoContent, NotFound>> DeleteInstallation(Guid id, TimingDbContext storage)
    {
        if (await storage.Set<Installation>().FindAsync(id) is not Installation installation)
        {
            return TypedResults.NotFound();
        }

        storage.Set<Installation>().Remove(installation);
        await storage.SaveChangesAsync();

        return TypedResults.NoContent();
    }
}
