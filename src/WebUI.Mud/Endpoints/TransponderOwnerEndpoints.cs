using Microsoft.AspNetCore.Mvc;
using VeloTime.Module.Common;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.WebUI.Mud.Endpoints;

internal static class TransponderOwnerEndpoints
{
    internal static void MapTransponderOwnerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/transponder-owners/me", async (ITransponderOwnerService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetOwnProfileAsync(cancellationToken)));
        var admin = endpoints.MapGroup("/admin/timing/transponder-owners").RequireAuthorization(AdministrationAuthorization.TimingPolicy);
        admin.MapPost("/resolve", async (IReadOnlyList<Guid> userIds, ITransponderOwnerService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.ResolveAsync(userIds, cancellationToken)));
        admin.MapGet("/search", async (string query, [FromQuery] int? profileOffset, [FromQuery] int? keycloakOffset,
            ITransponderOwnerService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.SearchAsync(query, profileOffset, keycloakOffset, cancellationToken)));
    }
}
