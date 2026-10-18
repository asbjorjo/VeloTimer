using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Mapping;
using VeloTime.Module.Timing.Service;

namespace VeloTime.Module.Timing.Api.Endpoints;

internal static class TransponderEndpoints
{
    internal static void MapTransponderEdnpoints(this IEndpointRouteBuilder builder)
    {
        var installations = builder.MapGroup("/transponders");

        installations.MapGet("", ListTransponders);
        installations.MapGet("{id}", GetTransponderById);
        installations.MapPost("", RegisterTransponder);
        installations.MapPost("{id}/owners", AddTransponderOwner);
    }

    private static async Task<Results<Ok<Guid>, UnprocessableEntity<List<string>>>> RegisterTransponder(
        InstallationService installationService,
        [FromQuery] string? SystemId,
        [FromQuery] string? SystemLabel,
        [FromQuery] string Type)
    {
        List<string> errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Type))
        {
            errors.Add("Type is required.");
        }
        if (string.IsNullOrWhiteSpace(SystemId) && string.IsNullOrWhiteSpace(SystemLabel))
        {
            errors.Add("Either SystemId or SystemLabel is required.");
        }
        if (!string.IsNullOrWhiteSpace(SystemId) && !string.IsNullOrWhiteSpace(SystemLabel))
        {
            errors.Add("Only one of SystemId or SystemLabel should be provided.");
        }
        if (errors.Count > 0)
        {
            return TypedResults.UnprocessableEntity(errors);
        }

        if (Type == "X2")
        {
            var id = string.IsNullOrWhiteSpace(SystemLabel) ? long.Parse(SystemId!) : MylapsX2Transponder.CodeToId(SystemLabel);
            var result = await installationService.RegisterTransponder(new MylapsX2Transponder
            {
                SystemId = id.ToString(),
            });

            return TypedResults.Ok(result.Id);
        } else
        {
            errors.Add($"Unsupported transponder type: {Type}");
            return TypedResults.UnprocessableEntity(errors);
        }
    }

    static async Task<Ok<IEnumerable<InstallationDTO>>> ListTransponders(TimingDbContext storage, [FromQuery] Guid? FacilityId)
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

    static async Task<Results<Ok<InstallationDTO>, NotFound>> GetTransponderById(Guid id, TimingDbContext storage)
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

    static async Task<Results<Ok, Conflict, NotFound>> AddTransponderOwner(TransponderService transponderService, Guid id, TransponderOwnership ownership)
    {
        var transponder = await transponderService.GetTransponderById(id);

        if (transponder == null)
        {
            return TypedResults.NotFound();
        }

        try
        {
            transponder.AddOwner(ownership.OwnerId, ownership.OwnedFrom, ownership.OwnedTo);
            await transponderService.Update(transponder);
            return TypedResults.Ok();
        }
        catch (InvalidOperationException)
        {
            return TypedResults.Conflict();
        }
    }
}
