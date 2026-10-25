using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Common;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Mapping;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Api.Endpoints;

internal static class TimingAdministrationEndpoints
{
    internal static void MapTimingAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin/installations").RequireAuthorization(AdministrationAuthorization.TimingPolicy)
            .AddEndpointFilter<AdministrationDependencyFilter>();
        admin.MapPost("", async (SaveInstallationRequest request, TimingAdministrationService service, CancellationToken cancellationToken) =>
        {
            var installation = await service.SaveInstallationAsync(null, request, cancellationToken);
            return Results.Created($"/api/timing/installations/{installation.Id}", installation.ToDto());
        }).WithName("CreateManagedInstallation").Produces<InstallationDTO>(201);
        admin.MapPut("/{id:guid}", async (Guid id, SaveInstallationRequest request, TimingAdministrationService service, CancellationToken cancellationToken) =>
            Results.Ok((await service.SaveInstallationAsync(id, request, cancellationToken)).ToDto())).WithName("UpdateManagedInstallation").Produces<InstallationDTO>();
        admin.MapDelete("/{id:guid}", async (Guid id, TimingAdministrationService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteInstallationAsync(id, cancellationToken);
            return Results.NoContent();
        }).WithName("DeleteManagedInstallation");
        admin.MapPost("/{installationId:guid}/points", async (Guid installationId, SaveTimingPointRequest request, TimingAdministrationService service, CancellationToken cancellationToken) =>
            Results.Ok((await service.SavePointAsync(installationId, null, request, cancellationToken)).ToDto())).WithName("CreateTimingPoint").Produces<TimingPointDTO>();
        admin.MapPut("/{installationId:guid}/points/{id:guid}", async (Guid installationId, Guid id, SaveTimingPointRequest request, TimingAdministrationService service, CancellationToken cancellationToken) =>
            Results.Ok((await service.SavePointAsync(installationId, id, request, cancellationToken)).ToDto())).WithName("UpdateTimingPoint").Produces<TimingPointDTO>();
        admin.MapDelete("/{installationId:guid}/points/{id:guid}", async (Guid installationId, Guid id, TimingAdministrationService service, CancellationToken cancellationToken) =>
        {
            await service.DeletePointAsync(installationId, id, cancellationToken);
            return Results.NoContent();
        }).WithName("DeleteTimingPoint");

        var references = endpoints.MapGroup("/references").RequireAuthorization(AdministrationAuthorization.ReferencePolicy);
        references.MapGet("/timingpoints/{id:guid}", async (Guid id, TimingDbContext storage, CancellationToken cancellationToken) =>
        {
            var point = await storage.Set<TimingPoint>().Where(point => point.Id == id).Select(point => new EntityReference(point.Id, point.Installation.Facility)).SingleOrDefaultAsync(cancellationToken);
            return point is null ? Results.NotFound() : Results.Ok(point);
        }).Produces<EntityReference>();
        references.MapGet("/facilities/{id:guid}/dependencies", async (Guid id, TimingDbContext storage, CancellationToken cancellationToken) =>
        {
            var count = await storage.Set<Installation>().CountAsync(installation => installation.Facility == id, cancellationToken);
            return Results.Ok(new DependencySummary(count, $"This facility is assigned to {count} timing installation(s)."));
        }).Produces<DependencySummary>();
    }
}