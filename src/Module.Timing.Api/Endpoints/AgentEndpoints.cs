using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Mapping;

namespace VeloTime.Module.Timing.Api.Endpoints;

internal static class AgentEndpoints
{
    internal static void MapAgentEdnpoints(this IEndpointRouteBuilder builder)
    {
        var installations = builder.MapGroup("/agents");

        installations.MapGet("", ListAgents);
        installations.MapGet("{id}", GetAgentById);
    }
    static async Task<Ok<IEnumerable<InstallationDTO>>> ListAgents(TimingDbContext storage, [FromQuery] Guid? FacilityId)
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

    static async Task<Results<Ok<InstallationDTO>, NotFound>> GetAgentById(Guid id, TimingDbContext storage)
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
}
