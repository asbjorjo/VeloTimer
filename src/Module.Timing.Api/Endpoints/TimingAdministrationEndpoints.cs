using System.Security.Claims;
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
        }).WithName("DeleteManagedInstallation").Produces(StatusCodes.Status204NoContent);
        admin.MapPost("/{id:guid}/capabilities/refresh", async (Guid id, TimingAdministrationService service, CancellationToken cancellationToken) =>
            Results.Accepted(value: new CapabilityReportRequestedDTO { RequestId = await service.RequestCapabilityReportAsync(id, cancellationToken) }))
            .WithName("RefreshInstallationCapabilities").Produces<CapabilityReportRequestedDTO>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound);
        admin.MapPost("/{id:guid}/passing-replays", CreatePassingReplay)
            .WithName("CreatePassingReplay").Produces<PassingReplayRequestDTO>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).Produces(StatusCodes.Status404NotFound);
        admin.MapGet("/{id:guid}/passing-replays", async (Guid id, PassingReplayRequestService replays, CancellationToken cancellationToken, int limit = 50) =>
                Results.Ok((await replays.ListAsync(id, limit, cancellationToken)).Select(replay => replay.ToDto()).ToList()))
            .WithName("ListPassingReplays").Produces<List<PassingReplayRequestDTO>>().Produces(StatusCodes.Status404NotFound);
        admin.MapGet("/{id:guid}/passing-replays/{requestId:guid}", async (Guid id, Guid requestId, PassingReplayRequestService replays, CancellationToken cancellationToken) =>
                Results.Ok((await replays.GetAsync(id, requestId, cancellationToken)).ToDto()))
            .WithName("GetPassingReplay").Produces<PassingReplayRequestDTO>().Produces(StatusCodes.Status404NotFound);
        admin.MapPost("/{installationId:guid}/points", async (Guid installationId, SaveTimingPointRequest request, TimingAdministrationService service, CancellationToken cancellationToken) =>
            Results.Ok((await service.SavePointAsync(installationId, null, request, cancellationToken)).ToDto())).WithName("CreateTimingPoint").Produces<TimingPointDTO>();
        admin.MapPut("/{installationId:guid}/points/{id:guid}", async (Guid installationId, Guid id, SaveTimingPointRequest request, TimingAdministrationService service, CancellationToken cancellationToken) =>
            Results.Ok((await service.SavePointAsync(installationId, id, request, cancellationToken)).ToDto())).WithName("UpdateTimingPoint").Produces<TimingPointDTO>();
        admin.MapDelete("/{installationId:guid}/points/{id:guid}", async (Guid installationId, Guid id, TimingAdministrationService service, CancellationToken cancellationToken) =>
        {
            await service.DeletePointAsync(installationId, id, cancellationToken);
            return Results.NoContent();
        }).WithName("DeleteTimingPoint").Produces(StatusCodes.Status204NoContent);

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

    private static async Task<IResult> CreatePassingReplay(Guid id, CreatePassingReplayRequest request, ClaimsPrincipal user,
        PassingReplayRequestService replays, CancellationToken cancellationToken)
    {
        try
        {
            var userId = Guid.TryParse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var subject)
                && subject != Guid.Empty ? subject : (Guid?)null;
            var replay = await replays.CreateAsync(id, request, userId, cancellationToken);
            return Results.Created($"/api/timing/admin/installations/{id}/passing-replays/{replay.Id}", replay.ToDto());
        }
        catch (PassingReplayValidationException exception)
        {
            // The detail is the reason code, so the UI can explain it in the user's language.
            return Results.Problem(title: "The replay cannot be requested.", detail: exception.Reason,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }
}
