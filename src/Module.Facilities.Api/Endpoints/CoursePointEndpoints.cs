using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Service;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Api.Endpoints;

internal static class CoursePointEndpoints
{
    internal static void MapCoursePointEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var coursePoints = endpoints.MapGroup("coursepoint");

        // These projections depend on effective time. Bypass both mapping and output caches so a
        // scheduled boundary cannot leave a stale current mapping behind.
        coursePoints.MapGet("{id}", GetCoursePointById);
        coursePoints.MapGet("{id}/facility", GetFacilityByCoursePointId).CacheOutput(FacilitiesOutputCache.PolicyName);
        coursePoints.MapGet("{start}/distance/{end}", DistanceBetweenCoursePoints).CacheOutput(FacilitiesOutputCache.PolicyName);

        var timingPoints = endpoints.MapGroup("timingpoint");
        timingPoints.MapGet("{id}", GetCoursePointByTimingPoint);
        timingPoints.MapGet("{start:guid}/distance/{end:guid}", DistanceBetweenTimingPoints);
    }

    static async Task<Results<Ok<FacilityDTO>, NotFound>> GetFacilityByCoursePointId(
        Guid id, FacilitiesService facilities, FacilityDbContext storage, CancellationToken cancellationToken)
    {
        if (!await storage.Set<CoursePoint>().AnyAsync(point => point.Id == id, cancellationToken))
            return TypedResults.NotFound();
        var facility = await facilities.FacilityByCoursePoint(id, cancellationToken);
        return facility is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new FacilityDTO
            {
                Id = facility.Id,
                Name = facility.Name,
                Layouts = facility.Layouts.Select(layout => layout.Id).ToList()
            });
    }

    static async Task<Results<Ok<CoursePointDTO>, NotFound>> GetCoursePointById(
        Guid id, [FromQuery] DateTimeOffset? at, FacilityDbContext storage,
        CancellationToken cancellationToken)
    {
        var point = await storage.Set<CoursePoint>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (point is null) return TypedResults.NotFound();
        var instant = at.HasValue ? TimingAssociationService.Normalize(at.Value) : DateTimeOffset.UtcNow;
        var associations = new TimingAssociationService(storage);
        var timingPointId = await associations.ResolveTimingPointIdAsync(id, instant, cancellationToken);
        return TypedResults.Ok(new CoursePointDTO
        {
            Id = point.Id,
            TimingPointId = timingPointId,
            Name = point.Name ?? string.Empty
        });
    }

    static async Task<Results<Ok<double>, NotFound, ProblemHttpResult>> DistanceBetweenCoursePoints(
        Guid start, Guid end, FacilitiesService facilities, FacilityDbContext storage, CancellationToken cancellationToken)
    {
        var startpoint = await storage.Set<CoursePoint>().AsNoTracking().SingleOrDefaultAsync(point => point.Id == start, cancellationToken);
        var endpoint = start == end
            ? startpoint
            : await storage.Set<CoursePoint>().AsNoTracking().SingleOrDefaultAsync(point => point.Id == end, cancellationToken);
        if (startpoint is null || endpoint is null) return TypedResults.NotFound();
        try
        {
            return TypedResults.Ok(await facilities.DistanceBetweenCoursePoints(startpoint, endpoint, cancellationToken));
        }
        catch (IncompatibleCoursePathException exception)
        {
            return TypedResults.Problem(detail: exception.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    static async Task<IResult> DistanceBetweenTimingPoints(
        Guid start, Guid end, [FromQuery] DateTimeOffset? startAt, [FromQuery] DateTimeOffset? endAt,
        FacilitiesService facilities, FacilityDbContext storage, CancellationToken cancellationToken)
    {
        // Both omitted bounds use exactly one captured now. Supplying only one bound does not
        // affect the independently defaulted other endpoint.
        var now = DateTimeOffset.UtcNow;
        var startInstant = startAt.HasValue ? TimingAssociationService.Normalize(startAt.Value) : now;
        var endInstant = endAt.HasValue ? TimingAssociationService.Normalize(endAt.Value) : now;
        var associations = new TimingAssociationService(storage);
        try
        {
            var startpoint = await associations.FindCoursePointByTimingPointIdAsync(start, startInstant, cancellationToken);
            var endpoint = start == end && startInstant == endInstant
                ? startpoint
                : await associations.FindCoursePointByTimingPointIdAsync(end, endInstant, cancellationToken);
            if (startpoint is null || endpoint is null) return TypedResults.NotFound();
            var distance = await facilities.DistanceBetweenCoursePoints(startpoint, endpoint, cancellationToken);
            var facility = await facilities.FacilityByCoursePoint(startpoint.Id, cancellationToken);
            return TypedResults.Ok(new CoursePointDistance(
                startpoint.Id, endpoint.Id, distance, facility?.Id ?? Guid.Empty));
        }
        catch (IncompatibleCoursePathException exception)
        {
            return TypedResults.Problem(detail: exception.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    static async Task<Results<Ok<CoursePointDTO>, NotFound>> GetCoursePointByTimingPoint(
        Guid id, [FromQuery] DateTimeOffset? at, FacilityDbContext storage, CancellationToken cancellationToken)
    {
        var instant = at.HasValue ? TimingAssociationService.Normalize(at.Value) : DateTimeOffset.UtcNow;
        var associations = new TimingAssociationService(storage);
        var point = await associations.FindCoursePointByTimingPointIdAsync(id, instant, cancellationToken);
        return point is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new CoursePointDTO
            {
                Id = point.Id,
                TimingPointId = id,
                Name = point.Name ?? string.Empty
            });
    }
}
