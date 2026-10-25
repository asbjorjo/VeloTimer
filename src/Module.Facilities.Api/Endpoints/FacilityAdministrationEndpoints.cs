using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using VeloTime.Module.Common;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Api.Endpoints;

internal static class FacilityAdministrationEndpoints
{
    internal static void MapFacilityAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var layouts = endpoints.MapGroup("/facility/{facilityId:guid}/layouts")
            .RequireAuthorization(AdministrationAuthorization.FacilitiesPolicy).AddEndpointFilter<AdministrationDependencyFilter>();
        layouts.MapPost("", CreateLayout).WithName("CreateCourseLayout");
        layouts.MapPut("/{id:guid}", UpdateLayout).WithName("UpdateCourseLayout");
        layouts.MapDelete("/{id:guid}", DeleteLayout).WithName("DeleteCourseLayout");
        endpoints.MapPut("/coursepoint/{id:guid}/timing-link", SetTimingLink)
            .RequireAuthorization(AdministrationAuthorization.TimingLinkPolicy).AddEndpointFilter<AdministrationDependencyFilter>()
            .WithName("SetCoursePointTimingLink");

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
            var count = await storage.Set<CoursePoint>().CountAsync(point => point.TimingPoint == id, cancellationToken);
            return Results.Ok(new DependencySummary(count, $"This timing point is linked to {count} coursepoint(s). Unlink them first."));
        }).Produces<DependencySummary>();
    }

    private static async Task<Results<Created<CourseLayoutDTO>, NotFound, ProblemHttpResult>> CreateLayout(Guid facilityId, SaveCourseLayoutRequest request,
        FacilityDbContext storage, IAdministrationReferences references, HybridCache cache, IOutputCacheStore output, CancellationToken cancellationToken)
    {
        if (!await storage.Set<Facility>().AnyAsync(facility => facility.Id == facilityId, cancellationToken)) return TypedResults.NotFound();
        var layout = new CourseLayout { Id = Guid.NewGuid(), FacilityId = facilityId };
        if (await SaveLayout(layout, request, storage, references, true, cancellationToken) is { } error) return error;
        await Invalidate(cache, output, cancellationToken);
        return TypedResults.Created($"/api/facilities/layouts/{layout.Id}", ToDto(layout));
    }

    private static async Task<Results<Ok<CourseLayoutDTO>, NotFound, ProblemHttpResult>> UpdateLayout(Guid facilityId, Guid id, SaveCourseLayoutRequest request,
        FacilityDbContext storage, IAdministrationReferences references, HybridCache cache, IOutputCacheStore output, CancellationToken cancellationToken)
    {
        var layout = await Layouts(storage).SingleOrDefaultAsync(layout => layout.Id == id && layout.FacilityId == facilityId, cancellationToken);
        if (layout is null) return TypedResults.NotFound();
        if (await SaveLayout(layout, request, storage, references, false, cancellationToken) is { } error) return error;
        await Invalidate(cache, output, cancellationToken);
        return TypedResults.Ok(ToDto(layout));
    }

    private static async Task<ProblemHttpResult?> SaveLayout(CourseLayout layout, SaveCourseLayoutRequest request, FacilityDbContext storage,
        IAdministrationReferences references, bool creating, CancellationToken cancellationToken)
    {
        if (request.Points is null || request.Segments is null || request.Segments.Count == 0) return Invalid("A layout must have at least one segment.");
        if (request.Points.Count > 1000 || request.Segments.Count > 1000) return Invalid("A layout can contain at most 1000 points and segments.");
        if (request.Points.Any(point => point.Id == Guid.Empty || string.IsNullOrWhiteSpace(point.Name)) || request.Points.Select(point => point.Id).Distinct().Count() != request.Points.Count)
            return Invalid("Each coursepoint needs a unique ID and a name.");
        if (request.Segments.Any(segment => segment.Id == Guid.Empty || !double.IsFinite(segment.Length) || segment.Length <= 0) ||
            request.Segments.Select(segment => segment.Id).Distinct().Count() != request.Segments.Count)
            return Invalid("Each segment needs a unique ID and a positive finite length in metres.");
        var usedIds = request.Segments.SelectMany(segment => new[] { segment.StartId, segment.EndId }).ToHashSet();
        var pointIds = request.Points.Select(point => point.Id).ToArray();
        if (!usedIds.SetEquals(pointIds)) return Invalid("Every segment endpoint must refer to a point in this layout; remove unused points.");
        if (request.Segments.Zip(request.Segments.Skip(1)).Any(pair => pair.First.EndId != pair.Second.StartId))
            return Invalid("Segments must form a connected course in their displayed order.");
        if (request.Segments.Select(segment => segment.StartId).Distinct().Count() != request.Segments.Count ||
            request.Segments.Select(segment => segment.EndId).Distinct().Count() != request.Segments.Count)
            return Invalid("A coursepoint may only occur once along the course, except for closing a lap.");
        var segmentIds = request.Segments.Select(segment => segment.Id).ToArray();
        if (await storage.Set<CourseSegment>().AnyAsync(segment => segmentIds.Contains(segment.Id) && segment.CourseLayoutId != layout.Id, cancellationToken))
            return Invalid("A segment ID is already used by another layout.");
        if (await storage.Set<CourseSegment>().AnyAsync(segment => segment.CourseLayout.FacilityId != layout.FacilityId &&
            (pointIds.Contains(segment.Start.Id) || pointIds.Contains(segment.End.Id)), cancellationToken))
            return Invalid("Coursepoints cannot be shared across different facilities.");

        var oldPoints = layout.Segments.SelectMany(segment => new[] { segment.Start, segment.End }).DistinctBy(point => point.Id).ToList();
        var semanticChange = !creating && !layout.Segments.OrderBy(segment => segment.Order)
            .Select(segment => new SaveCourseSegmentRequest(segment.Id, segment.Start.Id, segment.End.Id, segment.Length)).SequenceEqual(request.Segments);
        if (semanticChange)
        {
            foreach (var point in oldPoints)
            {
                var dependencies = await references.GetDependenciesAsync("statistics", "coursepoints", point.Id, cancellationToken);
                if (dependencies.Count > 0) return Conflict(dependencies.Description);
            }
        }
        var removedPoints = oldPoints.Where(point => !usedIds.Contains(point.Id)).ToList();
        foreach (var point in removedPoints)
        {
            if (point.TimingPoint.HasValue && !await UsedElsewhere(storage, layout.Id, point.Id, cancellationToken))
                return Conflict($"Unlink coursepoint '{point.Name}' from its timing point before removing it.");
        }

        var points = await storage.Set<CoursePoint>().Where(point => pointIds.Contains(point.Id)).ToDictionaryAsync(point => point.Id, cancellationToken);
        foreach (var requested in request.Points)
        {
            if (!points.TryGetValue(requested.Id, out var point))
            {
                point = new CoursePoint { Id = requested.Id };
                points.Add(point.Id, point);
                storage.Add(point);
            }
            point.Name = requested.Name.Trim();
        }
        if (creating) storage.Add(layout);
        var existing = layout.Segments.ToDictionary(segment => segment.Id);
        foreach (var removed in layout.Segments.Where(segment => !segmentIds.Contains(segment.Id)).ToList())
        {
            layout.Segments.Remove(removed);
            storage.Remove(removed);
        }
        for (var index = 0; index < request.Segments.Count; index++)
        {
            var requested = request.Segments[index];
            if (!existing.TryGetValue(requested.Id, out var segment))
            {
                segment = new CourseSegment { Id = requested.Id, CourseLayoutId = layout.Id, CourseLayout = layout, Start = points[requested.StartId], End = points[requested.EndId] };
                layout.Segments.Add(segment);
                storage.Add(segment);
            }
            segment.Order = index + 1;
            segment.Start = points[requested.StartId];
            segment.End = points[requested.EndId];
            segment.Length = requested.Length;
        }
        foreach (var point in removedPoints)
            if (!await UsedElsewhere(storage, layout.Id, point.Id, cancellationToken)) storage.Remove(point);
        await storage.SaveChangesAsync(cancellationToken);
        return null;
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteLayout(Guid facilityId, Guid id, FacilityDbContext storage,
        IAdministrationReferences references, HybridCache cache, IOutputCacheStore output, CancellationToken cancellationToken)
    {
        var layout = await Layouts(storage).SingleOrDefaultAsync(layout => layout.Id == id && layout.FacilityId == facilityId, cancellationToken);
        if (layout is null) return TypedResults.NotFound();
        var points = layout.Segments.SelectMany(segment => new[] { segment.Start, segment.End }).DistinctBy(point => point.Id).ToList();
        var orphanPoints = new List<CoursePoint>();
        foreach (var point in points)
        {
            var dependencies = await references.GetDependenciesAsync("statistics", "coursepoints", point.Id, cancellationToken);
            if (dependencies.Count > 0) return Conflict(dependencies.Description);
            if (!await UsedElsewhere(storage, layout.Id, point.Id, cancellationToken))
            {
                if (point.TimingPoint.HasValue) return Conflict($"Unlink coursepoint '{point.Name}' from its timing point before deleting the layout.");
                orphanPoints.Add(point);
            }
        }
        storage.RemoveRange(layout.Segments);
        storage.Remove(layout);
        storage.RemoveRange(orphanPoints);
        await storage.SaveChangesAsync(cancellationToken);
        await Invalidate(cache, output, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<CoursePointDTO>, NotFound, ProblemHttpResult>> SetTimingLink(Guid id, SetCoursePointTimingLinkRequest request,
        FacilityDbContext storage, IAdministrationReferences references, HybridCache cache, IOutputCacheStore output, CancellationToken cancellationToken)
    {
        await using var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
        if (storage.Database.IsNpgsql())
            await storage.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(731905124)", cancellationToken);
        var point = await storage.Set<CoursePoint>().FindAsync([id], cancellationToken);
        if (point is null) return TypedResults.NotFound();
        if (request.TimingPointId == Guid.Empty) return Invalid("Select an existing timing point or clear the link.");
        if (request.TimingPointId is { } timingPointId)
        {
            var target = await references.GetAsync("timing", "timingpoints", timingPointId, cancellationToken);
            var facilityId = await PointFacility(storage, id, cancellationToken);
            if (target is null || !facilityId.HasValue || target.FacilityId != facilityId.Value)
                return Invalid("The timing point must belong to an installation in the same facility.");
            if (await storage.Set<CoursePoint>().AnyAsync(other => other.Id != id && other.TimingPoint == timingPointId, cancellationToken))
                return Conflict("This timing point is already linked to another coursepoint. Unlink it first.");
        }
        point.TimingPoint = request.TimingPointId;
        await storage.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await Invalidate(cache, output, cancellationToken);
        return TypedResults.Ok(new CoursePointDTO { Id = point.Id, Name = point.Name ?? "", TimingPointId = point.TimingPoint });
    }

    private static IQueryable<CourseLayout> Layouts(FacilityDbContext storage) => storage.Set<CourseLayout>()
        .Include(layout => layout.Segments).ThenInclude(segment => segment.Start)
        .Include(layout => layout.Segments).ThenInclude(segment => segment.End);
    private static Task<bool> UsedElsewhere(FacilityDbContext storage, Guid layoutId, Guid pointId, CancellationToken cancellationToken) => storage.Set<CourseSegment>()
        .AnyAsync(segment => segment.CourseLayoutId != layoutId && (segment.Start.Id == pointId || segment.End.Id == pointId), cancellationToken);
    private static Task<Guid?> PointFacility(FacilityDbContext storage, Guid id, CancellationToken cancellationToken) => storage.Set<CourseSegment>()
        .Where(segment => segment.Start.Id == id || segment.End.Id == id).Select(segment => (Guid?)segment.CourseLayout.FacilityId).Distinct().SingleOrDefaultAsync(cancellationToken);
    private static async Task Invalidate(HybridCache cache, IOutputCacheStore output, CancellationToken cancellationToken)
    {
        await cache.RemoveByTagAsync("Facilities.Relationships", cancellationToken);
        await output.EvictByTagAsync("Facilities.Relationships", cancellationToken);
    }
    private static CourseLayoutDTO ToDto(CourseLayout layout) => new()
    {
        Id = layout.Id, FacilityId = layout.FacilityId,
        Segments = layout.Segments.OrderBy(segment => segment.Order).Select(segment => new SegmentDTO
        {
            Id = segment.Id, Order = segment.Order, Length = segment.Length,
            Start = new CoursePointDTO { Id = segment.Start.Id, Name = segment.Start.Name ?? "", TimingPointId = segment.Start.TimingPoint },
            End = new CoursePointDTO { Id = segment.End.Id, Name = segment.End.Name ?? "", TimingPointId = segment.End.TimingPoint }
        }).ToList()
    };
    private static ProblemHttpResult Invalid(string message) => TypedResults.Problem(detail: message, statusCode: 400);
    private static ProblemHttpResult Conflict(string message) => TypedResults.Problem(detail: message, statusCode: 409);
}