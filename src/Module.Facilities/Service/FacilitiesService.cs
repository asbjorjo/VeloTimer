using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using VeloTime.Module.Common;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using VeloTime.Module.Facilities.Interface.Course;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Service;

public class FacilitiesService(FacilityDbContext storage, HybridCache cache, ILogger<FacilitiesService>? logger = null,
    ICacheInvalidationOperations? evictionOperations = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<FacilitiesService>.Instance;

    /// <summary>Persists a new layout graph (seeding) and evicts relationship caches after the commit.</summary>
    public async Task<CourseLayout> CreateCourseLayout(CourseLayout Layout, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("CreateCourseLayout");
        storage.Set<CourseLayout>().Add(Layout);
        await storage.SaveChangesAsync(cancellationToken);
        await FacilitiesCache.InvalidateRelationshipsAsync(cache, _logger, evictionOperations);
        activity?.SetTag("CourseLayoutId", Layout.Id);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return Layout;
    }

    public async Task<Facility?> FacilityByCoursePoint(Guid CoursePointId, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("FacilityByCoursePoint");
        activity?.SetTag("CoursePointId", CoursePointId);
        var facility = await cache.GetOrCreateAsync(
            FacilitiesCache.FacilityByCoursePoint(CoursePointId),
            async cancel => await storage.Set<CourseSegment>()
                .Where(s => s.Start.Id == CoursePointId || s.End.Id == CoursePointId)
                .Select(s => s.Revision.CourseLayout.Facility)
                .Distinct()
                .SingleOrDefaultAsync(cancellationToken: cancel),
            options: FacilitiesCache.NewEntryOptions(),
            tags: [FacilitiesCache.RelationshipsTag], cancellationToken: cancellationToken);
        activity?.SetTag("FacilityId", facility?.Id);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return facility;
    }

    public async Task<CoursePoint?> GetCoursePointById(Guid CoursePointId, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("GetCoursePointById");
        activity?.SetTag("CoursePointId", CoursePointId);
        var coursePoint = await cache.GetOrCreateAsync(
            FacilitiesCache.CoursePointById(CoursePointId),
            async cancel => await storage.Set<CoursePoint>()
                .SingleOrDefaultAsync(p => p.Id == CoursePointId, cancellationToken: cancel),
            options: FacilitiesCache.NewEntryOptions(),
            tags: [FacilitiesCache.RelationshipsTag], cancellationToken: cancellationToken);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return coursePoint;
    }

    public async Task<double> CalculateDistanceBetweenCoursePoints(CoursePoint Start, CoursePoint End, CancellationToken cancellationToken = default)
        => (await CoursePointDistanceBetweenCoursePoints(Start, End, null, cancellationToken)).Distance;

    public async Task<double> DistanceBetweenCoursePoints(CoursePoint Start, CoursePoint End, CancellationToken cancellationToken = default)
        => (await CoursePointDistanceBetweenCoursePoints(Start, End, null, cancellationToken)).Distance;

    /// <summary>
    /// Resolves endpoint distance and optional full-loop evidence over the layouts in use at <paramref name="at"/>
    /// (default now), each with the geometry of its revision then, using the rules shared with Statistics
    /// (<see cref="CourseDistanceRules"/>). Not cached: the answer depends on the instant.
    /// </summary>
    public async Task<CoursePointDistanceResolution> CoursePointDistanceBetweenCoursePoints(
        CoursePoint Start, CoursePoint End, DateTimeOffset? at = null, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("DistanceBetweenCoursePoints");
        activity?.SetTag("StartCoursePointId", Start.Id);
        activity?.SetTag("EndCoursePointId", End.Id);
        var instant = at ?? DateTimeOffset.UtcNow;

        var facilityIds = await storage.Set<CourseSegment>()
            .Where(segment => segment.Start.Id == Start.Id || segment.End.Id == Start.Id)
            .Select(segment => segment.Revision.CourseLayout.FacilityId).Distinct().ToListAsync(cancellationToken);
        var layouts = await CourseConfigurationService.LayoutsWithHistory(storage).AsNoTracking()
            .Where(layout => facilityIds.Contains(layout.FacilityId)).ToListAsync(cancellationToken);
        var resolution = CourseDistanceRules.Resolve(CourseConfigurationService.GeometryAt(layouts, instant), Start.Id, End.Id, out var failure);
        if (resolution is null)
            throw new IncompatibleCoursePathException(failure == CourseDistanceFailure.Discontinuous
                ? "The course layout does not contain one continuous path."
                : "The coursepoints do not identify one compatible course layout.");

        activity?.SetTag("Distance", resolution.Distance);
        return new CoursePointDistanceResolution(resolution.Distance, resolution.CourseLayoutId, resolution.ClosedLoopDistanceMetres);
    }

    public async Task<double> DistanceBetweenTimingPoints(Guid Start, Guid End, DateTimeOffset startAt,
        DateTimeOffset endAt, TimingAssociationService associations, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("DistanceBetweenTimingPoints");

        try
        {
            activity?.SetTag("StartTimingPointId", Start);
            activity?.SetTag("EndTimingPointId", End);

            var (StartPoint, EndPoint) = await associations.ResolveDistanceEndpointsAsync(
                Start, End, startAt, endAt, cancellationToken);
            if (StartPoint is null || EndPoint is null)
                throw new KeyNotFoundException("A timing point has no coursepoint association at the requested instant.");

            return (await CoursePointDistanceBetweenCoursePoints(StartPoint, EndPoint, startAt, cancellationToken)).Distance;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public async Task<double> DistanceBetweenTimingPoints(Guid Start, Guid End, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var (startPoint, endPoint) = await new TimingAssociationService(storage).ResolveDistanceEndpointsAsync(
            Start, End, now, now, cancellationToken);
        // Preserve this legacy overload's SingleAsync missing-association error semantics.
        if (startPoint is null || endPoint is null)
            throw new InvalidOperationException("Sequence contains no elements.");
        return (await CoursePointDistanceBetweenCoursePoints(startPoint, endPoint, now, cancellationToken)).Distance;
    }

    public async Task<CoursePoint> FindCoursePointByTimingPointId(Guid TimingPoint, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await storage.Set<CoursePointTimingAssociation>()
            .Where(association => association.TimingPointId == TimingPoint &&
                (!association.ValidFromUtc.HasValue || association.ValidFromUtc <= now) &&
                (!association.ValidToUtc.HasValue || now < association.ValidToUtc))
            .Select(association => association.CoursePoint)
            .SingleAsync(cancellationToken);
    }

    /// <summary>Evicts the Facilities relationship cache after a committed configuration change.</summary>
    public Task InvalidateRelationshipsAsync() => FacilitiesCache.InvalidateRelationshipsAsync(cache, _logger, evictionOperations);
}

public sealed record CoursePointDistanceResolution(
    double Distance,
    Guid? CourseLayoutId,
    double? ClosedLoopDistanceMetres);

public sealed class IncompatibleCoursePathException(string message) : Exception(message);
