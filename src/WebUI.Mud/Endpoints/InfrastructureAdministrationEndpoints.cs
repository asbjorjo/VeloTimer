using VeloTime.Module.Common;
using VeloTime.WebUI.Mud.Client.Services;
using Facilities = VeloTime.Module.Facilities.Interface.Data;
using Timing = VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.WebUI.Mud.Endpoints;

internal static class InfrastructureAdministrationEndpoints
{
    internal static void MapInfrastructureAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var lookups = endpoints.MapGroup("/admin/lookups").RequireAuthorization(AdministrationAuthorization.ReferencePolicy);
        lookups.MapGet("/facilities", async (IInfrastructureAdministrationService service, CancellationToken cancellationToken) => Results.Ok(await service.GetFacilitiesAsync(cancellationToken)));
        lookups.MapGet("/facilities/{facilityId:guid}/layouts", async (Guid facilityId, IInfrastructureAdministrationService service, CancellationToken cancellationToken) => Results.Ok(await service.GetLayoutsAsync(facilityId, cancellationToken)));
        lookups.MapGet("/installations", async (IInfrastructureAdministrationService service, CancellationToken cancellationToken) => Results.Ok(await service.GetInstallationsAsync(cancellationToken)));

        var facilities = endpoints.MapGroup("/admin/facilities").RequireAuthorization(AdministrationAuthorization.FacilitiesPolicy);
        facilities.MapPost("", async (Facilities.SaveFacilityRequest request, IInfrastructureAdministrationService service, CancellationToken cancellationToken) => Results.Ok(await service.SaveFacilityAsync(null, request, cancellationToken)));
        facilities.MapPut("/{id:guid}", async (Guid id, Facilities.SaveFacilityRequest request, IInfrastructureAdministrationService service, CancellationToken cancellationToken) => Results.Ok(await service.SaveFacilityAsync(id, request, cancellationToken)));
        facilities.MapDelete("/{id:guid}", async (Guid id, IInfrastructureAdministrationService service, CancellationToken cancellationToken) => { await service.DeleteFacilityAsync(id, cancellationToken); return Results.NoContent(); });
        facilities.MapPost("/{facilityId:guid}/layouts", async (Guid facilityId, Facilities.SaveCourseLayoutRequest request, IInfrastructureAdministrationService service, CancellationToken cancellationToken) => Results.Ok(await service.SaveLayoutAsync(facilityId, null, request, cancellationToken)));
        facilities.MapPut("/{facilityId:guid}/layouts/{id:guid}", async (Guid facilityId, Guid id, Facilities.SaveCourseLayoutRequest request, IInfrastructureAdministrationService service, CancellationToken cancellationToken) => Results.Ok(await service.SaveLayoutAsync(facilityId, id, request, cancellationToken)));
        facilities.MapDelete("/{facilityId:guid}/layouts/{id:guid}", async (Guid facilityId, Guid id, IInfrastructureAdministrationService service, CancellationToken cancellationToken) => { await service.DeleteLayoutAsync(facilityId, id, cancellationToken); return Results.NoContent(); });
        endpoints.MapPut("/admin/coursepoints/{id:guid}/timing-link", async (Guid id, Facilities.SetCoursePointTimingLinkRequest request, IInfrastructureAdministrationService service, CancellationToken cancellationToken) =>
        { await service.SetTimingLinkAsync(id, request.TimingPointId, cancellationToken); return Results.NoContent(); }).RequireAuthorization(AdministrationAuthorization.TimingLinkPolicy);

        var installations = endpoints.MapGroup("/admin/timing/installations").RequireAuthorization(AdministrationAuthorization.TimingPolicy);
        installations.MapPost("", async (Timing.SaveInstallationRequest request, IInfrastructureAdministrationService service, CancellationToken cancellationToken) => Results.Ok(await service.SaveInstallationAsync(null, request, cancellationToken)));
        installations.MapPut("/{id:guid}", async (Guid id, Timing.SaveInstallationRequest request, IInfrastructureAdministrationService service, CancellationToken cancellationToken) => Results.Ok(await service.SaveInstallationAsync(id, request, cancellationToken)));
        installations.MapDelete("/{id:guid}", async (Guid id, IInfrastructureAdministrationService service, CancellationToken cancellationToken) => { await service.DeleteInstallationAsync(id, cancellationToken); return Results.NoContent(); });
        installations.MapPost("/{installationId:guid}/points", async (Guid installationId, Timing.SaveTimingPointRequest request, IInfrastructureAdministrationService service, CancellationToken cancellationToken) => Results.Ok(await service.SaveTimingPointAsync(installationId, null, request, cancellationToken)));
        installations.MapPut("/{installationId:guid}/points/{id:guid}", async (Guid installationId, Guid id, Timing.SaveTimingPointRequest request, IInfrastructureAdministrationService service, CancellationToken cancellationToken) => Results.Ok(await service.SaveTimingPointAsync(installationId, id, request, cancellationToken)));
        installations.MapDelete("/{installationId:guid}/points/{id:guid}", async (Guid installationId, Guid id, IInfrastructureAdministrationService service, CancellationToken cancellationToken) => { await service.DeleteTimingPointAsync(installationId, id, cancellationToken); return Results.NoContent(); });
    }
}