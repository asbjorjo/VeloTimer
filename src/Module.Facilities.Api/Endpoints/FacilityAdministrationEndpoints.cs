using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using System.Data;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using VeloTime.Module.Common;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Api;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Service;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Api.Endpoints;

internal static class FacilityAdministrationEndpoints
{
    internal static void MapFacilityAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var layouts = endpoints.MapGroup("/facility/{facilityId:guid}/layouts")
            .RequireAuthorization(AdministrationAuthorization.FacilitiesPolicy).AddEndpointFilter<AdministrationDependencyFilter>();
        layouts.MapPost("", CreateLayout).WithName("CreateCourseLayout");
        layouts.MapDelete("/{id:guid}", DeleteLayout).WithName("DeleteCourseLayout");
        layouts.MapGet("/{id:guid}/history", GetLayoutHistory).WithName("GetCourseLayoutHistory")
            .Produces<CourseLayoutHistoryDTO>().Produces(StatusCodes.Status404NotFound);
        layouts.MapPut("/{id:guid}/revision", SetLayoutRevision).WithName("SetCourseLayoutRevision")
            .Produces<SetCourseLayoutRevisionResponse>()
            .Produces<CourseLayoutRevisionConflictPreview>(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithDescription("Replaces the layout's geometry over [ValidFromUtc, ValidToUtc) with a new revision. Overlapping existing revisions need confirmation with the token from the 409 preview. Statistics recompute results from ValidFromUtc.");
        layouts.MapPut("/{id:guid}/assignment", SetLayoutAssignment).WithName("SetFacilityLayoutAssignment")
            .Produces<SetFacilityLayoutAssignmentResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithDescription("Sets whether the facility runs the layout over [ValidFromUtc, ValidToUtc). Statistics recompute results in the interval.");
        endpoints.MapPut("/coursepoint/{id:guid}/timing-link", SetTimingLink)
            .RequireAuthorization(AdministrationAuthorization.TimingLinkPolicy).AddEndpointFilter<AdministrationDependencyFilter>()
            .WithName("SetCoursePointTimingLink")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
        endpoints.MapPut("/coursepoint/{id:guid}/timing-association", SetTimingAssociation)
            .RequireAuthorization(AdministrationAuthorization.TimingLinkPolicy).AddEndpointFilter<AdministrationDependencyFilter>()
            .WithName("SetCoursePointTimingAssociation")
            .Produces<SetCoursePointTimingAssociationResponse>()
            .Produces<TimingAssociationConflictPreview>(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        endpoints.MapGet("/coursepoint/{id:guid}/timing-associations", GetTimingAssociations)
            .RequireAuthorization(AdministrationAuthorization.TimingLinkPolicy)
            .WithName("GetCoursePointTimingAssociations")
            .Produces<IReadOnlyList<CoursePointTimingAssociationSnapshot>>()
            .Produces(StatusCodes.Status404NotFound)
            .WithDescription("Returns effective association history/schedule, not actor audit history. Backdated corrections publish a change event, and Statistics recompute results in the affected interval.");

        var references = endpoints.MapGroup("/references").RequireAuthorization(AdministrationAuthorization.ReferencePolicy);
        references.MapGet("/facilities/{id:guid}", async (Guid id, FacilityDbContext storage, CancellationToken cancellationToken) =>
            await storage.Set<Facility>().AnyAsync(facility => facility.Id == id, cancellationToken)
                ? Results.Ok(new EntityReference(id, id)) : Results.NotFound()).Produces<EntityReference>();
        references.MapGet("/coursepoints/{id:guid}", async (Guid id, FacilityDbContext storage, CancellationToken cancellationToken) =>
        {
            var facilityId = await PointFacility(storage, id, cancellationToken);
            return facilityId.HasValue ? Results.Ok(new EntityReference(id, facilityId.Value)) : Results.NotFound();
        }).Produces<EntityReference>();
        references.MapGet("/timingpoints/{id:guid}/dependencies", async (Guid id, FacilityDbContext storage, CancellationToken cancellationToken) =>
        {
            var count = await storage.Set<CoursePointTimingAssociation>().Where(association => association.TimingPointId == id)
                .Select(association => association.CoursePointId).Distinct().CountAsync(cancellationToken);
            return Results.Ok(new DependencySummary(count,
                $"This timing point has association history for {count} coursepoint(s); historical mappings are retained."));
        }).Produces<DependencySummary>();
    }

    private static async Task<Results<Created<CourseLayoutDTO>, NotFound, ProblemHttpResult>> CreateLayout(Guid facilityId, SaveCourseLayoutRequest request,
        FacilityDbContext storage, [FromServices] ICourseConfigEventPublisher publisher, HybridCache cache, IOutputCacheStore output,
        ICacheInvalidationOperations invalidation, ILogger<FacilityDbContext> logger,
        CancellationToken cancellationToken)
    {
        if (!await storage.Set<Facility>().AnyAsync(facility => facility.Id == facilityId, cancellationToken)) return TypedResults.NotFound();
        var configuration = new CourseConfigurationService(storage);
        if (await configuration.ValidateGeometryAsync(facilityId, request.Points, request.Segments, cancellationToken) is { } error)
            return Invalid(error);
        var now = DateTimeOffset.UtcNow;
        var layout = await configuration.CreateLayoutAsync(facilityId, request.Points, request.Segments, now, publisher.EnqueueAsync, cancellationToken);
        await FacilitiesOutputCache.InvalidateRelationshipsWithOperationsAsync(cache, output, logger, invalidation);
        return TypedResults.Created($"/api/facilities/layouts/{layout.Id}", await LayoutAtAsync(storage, layout.Id, now, cancellationToken));
    }

    private static async Task<IResult> SetLayoutRevision(Guid facilityId, Guid id, SetCourseLayoutRevisionRequest request,
        FacilityDbContext storage, IAdministrationReferences references, IDataProtectionProvider protectionProvider,
        [FromServices] ICourseConfigEventPublisher publisher, HybridCache cache, IOutputCacheStore output,
        ICacheInvalidationOperations invalidation, ILogger<FacilityDbContext> logger, CancellationToken cancellationToken)
    {
        var configuration = new CourseConfigurationService(storage, protectionProvider);
        if (await configuration.ValidateGeometryAsync(facilityId, request.Points, request.Segments, cancellationToken) is { } error)
            return Invalid(error);
        try
        {
            var edit = await configuration.SetRevisionAsync(facilityId, id, request, DateTimeOffset.UtcNow, publisher.EnqueueAsync,
                cancellationToken, StatisticsDependencyCheck(references));
            if (edit.ConflictPreview is not null)
                return Results.Json(edit.ConflictPreview, statusCode: StatusCodes.Status409Conflict);
            await FacilitiesOutputCache.InvalidateRelationshipsWithOperationsAsync(cache, output, logger, invalidation);
            return Results.Ok(new SetCourseLayoutRevisionResponse(edit.ChangeId, edit.RevisionId));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (CourseConfigurationValidationException exception)
        {
            return Invalid(exception.Message);
        }
        catch (CourseConfigurationConflictException exception)
        {
            return Conflict(exception.Message);
        }
        catch (Exception exception) when (IsAssociationConstraintFailure(exception))
        {
            return Conflict("The layout's revisions changed concurrently; reload and try again.");
        }
    }

    private static async Task<Results<Ok<SetFacilityLayoutAssignmentResponse>, NotFound, ProblemHttpResult>> SetLayoutAssignment(
        Guid facilityId, Guid id, SetFacilityLayoutAssignmentRequest request, FacilityDbContext storage,
        [FromServices] ICourseConfigEventPublisher publisher, HybridCache cache, IOutputCacheStore output,
        ICacheInvalidationOperations invalidation, ILogger<FacilityDbContext> logger, CancellationToken cancellationToken)
    {
        var configuration = new CourseConfigurationService(storage);
        try
        {
            var edit = await configuration.SetAssignmentAsync(facilityId, id, request, DateTimeOffset.UtcNow, publisher.EnqueueAsync, cancellationToken);
            if (edit.ChangeId.HasValue)
                await FacilitiesOutputCache.InvalidateRelationshipsWithOperationsAsync(cache, output, logger, invalidation);
            return TypedResults.Ok(new SetFacilityLayoutAssignmentResponse(edit.ChangeId, edit.Assignments));
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (CourseConfigurationValidationException exception)
        {
            return Invalid(exception.Message);
        }
        catch (Exception exception) when (IsAssociationConstraintFailure(exception))
        {
            return Conflict("The layout's assignments changed concurrently; reload and try again.");
        }
    }

    private static async Task<Results<Ok<CourseLayoutHistoryDTO>, NotFound>> GetLayoutHistory(Guid facilityId, Guid id,
        FacilityDbContext storage, CancellationToken cancellationToken)
    {
        var layout = await CourseConfigurationService.LayoutsWithHistory(storage).AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id && candidate.FacilityId == facilityId, cancellationToken);
        return layout is null ? TypedResults.NotFound() : TypedResults.Ok(FacilityCourseHistoryReader.ToHistory(layout));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteLayout(Guid facilityId, Guid id, FacilityDbContext storage,
        IAdministrationReferences references, [FromServices] ICourseConfigEventPublisher publisher, HybridCache cache, IOutputCacheStore output,
        ICacheInvalidationOperations invalidation, ILogger<FacilityDbContext> logger, CancellationToken cancellationToken)
    {
        try
        {
            if (!await new CourseConfigurationService(storage).DeleteLayoutAsync(facilityId, id, DateTimeOffset.UtcNow, publisher.EnqueueAsync,
                    cancellationToken, StatisticsDependencyCheck(references)))
                return TypedResults.NotFound();
        }
        catch (CourseConfigurationConflictException exception)
        {
            return Conflict(exception.Message);
        }
        await FacilitiesOutputCache.InvalidateRelationshipsWithOperationsAsync(cache, output, logger, invalidation);
        return TypedResults.NoContent();
    }

    /// <summary>A course point a statistics definition refers to must not be deleted with the last layout using it.</summary>
    private static PointDeletionCheck StatisticsDependencyCheck(IAdministrationReferences references) => async (pointId, cancellationToken) =>
    {
        var dependencies = await references.GetDependenciesAsync("statistics", "coursepoints", pointId, cancellationToken);
        return dependencies.Count > 0 ? dependencies.Description : null;
    };

    private static async Task<Results<Ok<CoursePointDTO>, NotFound, ProblemHttpResult>> SetTimingLink(Guid id, SetCoursePointTimingLinkRequest request,
        FacilityDbContext storage, IAdministrationReferences references,
        IDataProtectionProvider protectionProvider, [FromServices] ICourseConfigEventPublisher publisher,
        HybridCache cache, IOutputCacheStore output,
        ICacheInvalidationOperations invalidation, ILogger<FacilityDbContext> logger, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var associations = new TimingAssociationService(storage, protectionProvider);
        try
        {
            var edit = await associations.SetIntervalAsync(id,
                new SetCoursePointTimingAssociationRequest
                {
                    TimingPointId = request.TimingPointId,
                    ValidFromUtc = TimingAssociationService.Normalize(now),
                    ValidToUtc = null
                }, now, references, publisher.EnqueueAsync, cancellationToken);
            if (edit.ConflictPreview is not null)
                return Conflict("The timing point is already linked to another coursepoint during the requested interval.");
            if (edit.ChangeId.HasValue)
                await FacilitiesOutputCache.InvalidateRelationshipsWithOperationsAsync(cache, output, logger, invalidation);
            var point = await storage.Set<CoursePoint>().AsNoTracking()
                .SingleAsync(candidate => candidate.Id == id, cancellationToken);
            var timingPointId = await associations.ResolveTimingPointIdAsync(id, now, cancellationToken);
            return TypedResults.Ok(new CoursePointDTO
            {
                Id = point.Id,
                Name = point.Name ?? string.Empty,
                TimingPointId = timingPointId
            });
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (TimingAssociationValidationException exception)
        {
            return Invalid(exception.Message);
        }
        catch (Exception exception) when (IsAssociationConstraintFailure(exception))
        {
            return Conflict("The requested association conflicts with a mapping that changed concurrently.");
        }
    }

    private static async Task<IResult> SetTimingAssociation(Guid id,
        SetCoursePointTimingAssociationRequest request, FacilityDbContext storage,
        IAdministrationReferences references, IDataProtectionProvider protectionProvider,
        [FromServices] ICourseConfigEventPublisher publisher,
        HybridCache cache, IOutputCacheStore output, ICacheInvalidationOperations invalidation,
        ILogger<FacilityDbContext> logger, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var associations = new TimingAssociationService(storage, protectionProvider);
        try
        {
            var edit = await associations.SetIntervalAsync(id, request, now, references, publisher.EnqueueAsync, cancellationToken);
            if (edit.ConflictPreview is not null)
                return Results.Json(edit.ConflictPreview, statusCode: StatusCodes.Status409Conflict);
            if (edit.ChangeId.HasValue)
                await FacilitiesOutputCache.InvalidateRelationshipsWithOperationsAsync(cache, output, logger, invalidation);
            return Results.Ok(new SetCoursePointTimingAssociationResponse(edit.ChangeId, edit.Changes));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (TimingAssociationValidationException exception)
        {
            return Invalid(exception.Message);
        }
        catch (Exception exception) when (IsAssociationConstraintFailure(exception))
        {
            // SetIntervalAsync has disposed (and rolled back) its transaction before recovery.
            // Never retry a write: the existing 409 contract requires a fresh state-bound preview.
            try
            {
                var edit = await associations.GetConcurrencyPreviewAsync(id, request, references, cancellationToken);
                return Results.Json(edit.ConflictPreview, statusCode: StatusCodes.Status409Conflict);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (TimingAssociationValidationException validation)
            {
                return Invalid(validation.Message);
            }
        }
    }

    private static async Task<IResult> GetTimingAssociations(Guid id, FacilityDbContext storage,
        CancellationToken cancellationToken)
    {
        var isolation = storage.Database.IsNpgsql() ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable;
        await using var transaction = await storage.Database.BeginTransactionAsync(isolation, cancellationToken);
        if (!await storage.Set<CoursePoint>().AnyAsync(point => point.Id == id, cancellationToken))
            return Results.NotFound();
        var associations = new TimingAssociationService(storage);
        var history = await associations.GetHistoryAsync(id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(history);
    }

    private static Task<Guid?> PointFacility(FacilityDbContext storage, Guid id, CancellationToken cancellationToken) => storage.Set<CourseSegment>()
        .Where(segment => segment.Start.Id == id || segment.End.Id == id).Select(segment => (Guid?)segment.Revision.CourseLayout.FacilityId)
        .Distinct().SingleOrDefaultAsync(cancellationToken);

    private static async Task<CourseLayoutDTO> LayoutAtAsync(FacilityDbContext storage, Guid layoutId, DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var layout = await CourseConfigurationService.LayoutsWithHistory(storage).AsNoTracking()
            .SingleAsync(candidate => candidate.Id == layoutId, cancellationToken);
        return (await CourseLayoutProjection.ToDtosAsync(storage, [layout], at, cancellationToken)).Single();
    }

    private static ProblemHttpResult Invalid(string message) => TypedResults.Problem(detail: message, statusCode: 400);
    private static ProblemHttpResult Conflict(string message) => TypedResults.Problem(detail: message, statusCode: 409);

    private static bool IsAssociationConstraintFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is Npgsql.PostgresException postgres &&
                postgres.SqlState is Npgsql.PostgresErrorCodes.ExclusionViolation or
                    Npgsql.PostgresErrorCodes.CheckViolation or Npgsql.PostgresErrorCodes.ForeignKeyViolation or
                    Npgsql.PostgresErrorCodes.DeadlockDetected or Npgsql.PostgresErrorCodes.SerializationFailure)
                return true;
        }
        return false;
    }
}