using VeloTime.Module.Common;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.WebUI.Mud.Endpoints;

internal static class TransponderManagementEndpoints
{
    internal static void MapTransponderManagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        Map(endpoints, false);
        Map(endpoints.MapGroup("/admin/timing").RequireAuthorization(AdministrationAuthorization.TimingPolicy), true);
    }

    private static void Map(IEndpointRouteBuilder endpoints, bool administration)
    {
        endpoints.MapGet("/transponders", async (ITransponderManagementService service, CancellationToken cancellationToken,
            int page = 0, int pageSize = 10, string search = "", bool includeHistory = true) =>
            Results.Ok(await service.GetTranspondersAsync(administration, page, pageSize, search, includeHistory, cancellationToken)));
        endpoints.MapPost("/transponders", async (ClaimTransponderRequest request, ITransponderManagementService service, CancellationToken cancellationToken) =>
        {
            await service.RegisterAsync(request.Code, administration, cancellationToken);
            return Results.NoContent();
        });
        endpoints.MapPost("/transponders/register", async (RegisterTransponderRequest request, ITransponderManagementService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.RegisterOwnershipAsync(request, administration, cancellationToken)));
        endpoints.MapPost("/ownerships/{id:guid}/end", async (Guid id, ITransponderManagementService service, CancellationToken cancellationToken) =>
        {
            await service.EndOwnershipAsync(id, administration, cancellationToken);
            return Results.NoContent();
        });
        endpoints.MapDelete("/ownerships/{id:guid}", async (Guid id, ITransponderManagementService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteOwnershipAsync(id, administration, cancellationToken);
            return Results.NoContent();
        });
        if (!administration) return;
        endpoints.MapGet("/transponders/lookup", async (ITransponderManagementService service, CancellationToken cancellationToken,
            int page = 0, string search = "") =>
            Results.Ok(await service.LookupTranspondersAsync(page, search, cancellationToken)));
        endpoints.MapPut("/transponders/{id:guid}", async (Guid id, SaveTransponderRequest request, ITransponderManagementService service, CancellationToken cancellationToken) =>
        {
            await service.SaveTransponderAsync(id, request.Label, cancellationToken);
            return Results.NoContent();
        });
        endpoints.MapDelete("/transponders/{id:guid}", async (Guid id, ITransponderManagementService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteTransponderAsync(id, cancellationToken);
            return Results.NoContent();
        });
        endpoints.MapPost("/ownerships", async (SaveTransponderOwnershipRequest request, ITransponderManagementService service, CancellationToken cancellationToken) =>
        {
            await service.SaveOwnershipAsync(null, request, cancellationToken);
            return Results.NoContent();
        });
        endpoints.MapPut("/ownerships/{id:guid}", async (Guid id, SaveTransponderOwnershipRequest request, ITransponderManagementService service, CancellationToken cancellationToken) =>
        {
            await service.SaveOwnershipAsync(id, request, cancellationToken);
            return Results.NoContent();
        });
    }
}