using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using VeloTime.Module.Common;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Service;

public class FacilitiesService(FacilityDbContext storage, HybridCache cache, ILogger<FacilitiesService>? logger = null,
    ICacheInvalidationOperations? evictionOperations = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<FacilitiesService>.Instance;

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
                .Select(s => s.CourseLayout.Facility)
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

    public async Task<IEnumerable<CourseSegment>> GetSegmentsBetweenCoursePoints(CoursePoint start, CoursePoint end, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("GetSegmentsBetweenCoursePoints");

        return await cache.GetOrCreateAsync(
            FacilitiesCache.SegmentsBetweenCoursePoints(start.Id, end.Id),
            async cancel => await storage.Set<CourseLayout>()
                .Include(l => l.Segments)
                    .ThenInclude(s => s.Start)
                .Include(l => l.Segments)
                    .ThenInclude(s => s.End)
                .Where(l => l.Segments.Select(s => s.Start).Contains(start) || l.Segments.Select(s => s.End).Contains(end))
                .SelectMany(l => l.Segments)
                .OrderBy(s => s.Order)
                .ToListAsync(),
            options: FacilitiesCache.NewEntryOptions(),
            tags: [FacilitiesCache.RelationshipsTag], cancellationToken: cancellationToken);
    }

    public async Task<double> CalculateDistanceBetweenCoursePoints(CoursePoint Start, CoursePoint End, CancellationToken cancellationToken = default)
        => (await CalculateCoursePointDistance(Start, End, cancellationToken)).Distance;

    private async Task<CoursePointDistanceResolution> CalculateCoursePointDistance(
        CoursePoint Start, CoursePoint End, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("CalculateDistanceBetweenCoursePoints");
        var layouts = await storage.Set<CourseLayout>()
            .Where(layout => layout.Segments.Any(segment => segment.Start.Id == Start.Id || segment.End.Id == Start.Id) &&
                             layout.Segments.Any(segment => segment.Start.Id == End.Id || segment.End.Id == End.Id))
            .Include(layout => layout.Segments).ThenInclude(segment => segment.Start)
            .Include(layout => layout.Segments).ThenInclude(segment => segment.End)
            .ToListAsync(cancellationToken);
        if (layouts.Count != 1)
            throw new IncompatibleCoursePathException("The coursepoints do not identify one compatible course layout.");

        var segments = layouts[0].Segments.OrderBy(segment => segment.Order).ToList();
        if (segments.Count == 0 || segments.Zip(segments.Skip(1))
                .Any(pair => pair.First.End.Id != pair.Second.Start.Id))
            throw new IncompatibleCoursePathException("The course layout does not contain one continuous path.");
        var points = segments.Take(1).Select(segment => segment.Start)
            .Concat(segments.Select(segment => segment.End))
            .ToList();
        var startIndex = points.FindIndex(point => point.Id == Start.Id);
        var endIndex = points.FindIndex(point => point.Id == End.Id);
        if (startIndex < 0 || endIndex < 0)
            throw new IncompatibleCoursePathException("The coursepoints do not identify a continuous path.");
        var closed = segments[^1].End.Id == segments[0].Start.Id;
        double distance;
        if (startIndex == endIndex)
            distance = segments.Sum(segment => segment.Length);
        else if (closed)
        {
            // Closed courses are directed: continue forward through the last segment and
            // wrap to the first, rather than taking the reverse linear distance.
            var count = (endIndex - startIndex + segments.Count) % segments.Count;
            distance = segments.Skip(startIndex).Concat(segments.Take(startIndex))
                .Take(count).Sum(segment => segment.Length);
        }
        else
        {
            // Open courses retain their existing symmetric linear-path behavior.
            distance = segments.Skip(Math.Min(startIndex, endIndex))
                .Take(Math.Abs(endIndex - startIndex))
                .Sum(segment => segment.Length);
        }

        var loopDistance = GetValidClosedLoopDistance(segments, points, closed);
        if (layouts[0].Id == Guid.Empty) loopDistance = null;
        Guid? layoutId = loopDistance.HasValue ? layouts[0].Id : null;
        return new CoursePointDistanceResolution(distance, layoutId, loopDistance);
    }

    public async Task<double> DistanceBetweenCoursePoints(CoursePoint Start, CoursePoint End, CancellationToken cancellationToken = default)
        => (await CoursePointDistanceBetweenCoursePoints(Start, End, cancellationToken)).Distance;

    /// <summary>
    /// Resolves endpoint distance and optional full-loop evidence from the same course geometry read.
    /// </summary>
    public async Task<CoursePointDistanceResolution> CoursePointDistanceBetweenCoursePoints(
        CoursePoint Start, CoursePoint End, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("DistanceBetweenCoursePoints");

        activity?.SetTag("StartCoursePointId", Start.Id);
        activity?.SetTag("EndCoursePointId", End.Id);

        var resolution = await cache.GetOrCreateAsync(
            FacilitiesCache.DistanceBetweenCoursePoints(Start.Id, End.Id),
            async cancel => await CalculateCoursePointDistance(Start, End, cancel),
            options: FacilitiesCache.NewEntryOptions(),
            tags: [FacilitiesCache.RelationshipsTag], cancellationToken: cancellationToken);

        activity?.SetTag("Distance", resolution.Distance);

        return resolution;
    }

    private static double? GetValidClosedLoopDistance(
        IReadOnlyList<CourseSegment> segments, IReadOnlyList<CoursePoint> points, bool closed)
    {
        if (!closed || segments.Any(segment => !double.IsFinite(segment.Length) || segment.Length <= 0) ||
            segments.Select(segment => segment.Order).Distinct().Count() != segments.Count ||
            points.Take(points.Count - 1).Select(point => point.Id).Distinct().Count() != segments.Count)
            return null;

        var distance = segments.Sum(segment => segment.Length);
        return double.IsFinite(distance) && distance > 0 ? distance : null;
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

            return await DistanceBetweenCoursePoints(StartPoint, EndPoint, cancellationToken);
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
        return await DistanceBetweenCoursePoints(startPoint, endPoint, cancellationToken);
    }

    public async Task<CoursePoint> FindCoursePointByTimingPointId(Guid TimingPoint, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await FindCoursePointByTimingPointIdAtAsync(TimingPoint, now, cancellationToken);
    }

    private async Task<CoursePoint> FindCoursePointByTimingPointIdAtAsync(
        Guid timingPointId, DateTimeOffset at, CancellationToken cancellationToken) =>
        await storage.Set<CoursePointTimingAssociation>()
            .Where(association => association.TimingPointId == timingPointId &&
                (!association.ValidFromUtc.HasValue || association.ValidFromUtc <= at) &&
                (!association.ValidToUtc.HasValue || at < association.ValidToUtc))
            .Select(association => association.CoursePoint)
            .SingleAsync(cancellationToken);
}

public sealed record CoursePointDistanceResolution(
    double Distance,
    Guid? CourseLayoutId,
    double? ClosedLoopDistanceMetres);

public sealed class IncompatibleCoursePathException(string message) : Exception(message);
