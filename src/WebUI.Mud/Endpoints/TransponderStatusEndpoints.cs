using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.WebUI.Mud.Endpoints;

internal static class TransponderStatusEndpoints
{
    internal static void MapTransponderStatusEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/transponder-status/{id:guid}", async (Guid id, ITransponderStatusService service,
            CancellationToken cancellationToken, int ownershipPage = 0, int warningPage = 0, int pageSize = 10) =>
            TypedResults.Ok(await service.GetAsync(id, ownershipPage, warningPage, pageSize, cancellationToken)));
}
