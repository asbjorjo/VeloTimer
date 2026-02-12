using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using System.Diagnostics;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Service;

internal class FacilitiesService(FacilityDbContext storage, HybridCache cache)
{
    internal async Task<CourseLayout> CreateCourseLayout(CourseLayout Layout)
    {
        using var activity = Instrumentation.Source.StartActivity("CreateCourseLayout");
        storage.Set<CourseLayout>().Add(Layout);
        await storage.SaveChangesAsync();
        activity?.SetTag("CourseLayoutId", Layout.Id);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return Layout;
    }

    internal async Task<Facility?> FacilityByCoursePoint(Guid CoursePointId, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("FacilityByCoursePoint");
        activity?.SetTag("CoursePointId", CoursePointId);
        var facility = await cache.GetOrCreateAsync(
            $"FacilityByCoursePoint_{CoursePointId}",
            async cancel => await storage.Set<CourseSegment>()
                .Where(s => s.Start.Id == CoursePointId || s.End.Id == CoursePointId)
                .Select(s => s.CourseLayout.Facility)
                .Distinct()
                .SingleOrDefaultAsync(cancellationToken: cancel),
            cancellationToken: cancellationToken);
        activity?.SetTag("FacilityId", facility?.Id);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return facility;
    }

    internal async Task<CoursePoint?> GetCoursePointById(Guid CoursePointId, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("GetCoursePointById");
        activity?.SetTag("CoursePointId", CoursePointId);
        var coursePoint = await cache.GetOrCreateAsync(
            $"CoursePointById_{CoursePointId}",
            async cancel => await storage.Set<CoursePoint>()
                .SingleOrDefaultAsync(p => p.Id == CoursePointId, cancellationToken: cancel),
            cancellationToken: cancellationToken);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return coursePoint;
    }

    internal async Task<IEnumerable<CourseSegment>> GetSegmentsBetweenCoursePoints(CoursePoint start, CoursePoint end, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("GetSegmentsBetweenCoursePoints");

        return await cache.GetOrCreateAsync(
            $"SegmentsBetweenCoursePoints_{start.Id}_{end.Id}",
            async cancel => await storage.Set<CourseLayout>()
                .Include(l => l.Segments)                
                    .ThenInclude(s => s.Start)
                .Include(l => l.Segments)
                    .ThenInclude(s => s.End)
                .Where(l => l.Segments.Select(s => s.Start).Contains(start) || l.Segments.Select(s => s.End).Contains(end))
                .SelectMany(l => l.Segments)
                .OrderBy(s => s.Order)
                .ToListAsync(),
            cancellationToken: cancellationToken);
    }

    internal async Task<double> CalculateDistanceBetweenCoursePoints(CoursePoint Start, CoursePoint End, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("CalculateDistanceBetweenCoursePoints");
        var segments = await GetSegmentsBetweenCoursePoints(Start, End, cancellationToken);

        CourseSegment start = segments.Single(s => s.Start == Start);
        CourseSegment end = Start == End ? start : segments.Single(s => s.End == End);

        activity?.SetTag("StartSegmentId", start.Id);
        activity?.SetTag("EndSegmentId", end.Id);

        if (Start == End)
        {
            return segments.Sum(s => s.Length);
        }


        if (start.Order < end.Order)
        {
            return segments.Where(s => s.Order >= start.Order && s.Order <= end.Order).Sum(s => s.Length);
        }
        else
        {
            return segments.Where(s => s.Order <= start.Order && s.Order >= end.Order).Sum(s => s.Length);
        }
    }

    internal async Task<double> DistanceBetweenCoursePoints(CoursePoint Start, CoursePoint End, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("DistanceBetweenCoursePoints");

        activity?.SetTag("StartCoursePointId", Start.Id);
        activity?.SetTag("EndCoursePointId", End.Id);

        var distance = await cache.GetOrCreateAsync(
            $"DistanceBetweenCoursePoints_{Start.Id}_{End.Id}",
            async cancel => await CalculateDistanceBetweenCoursePoints(Start, End, cancel),
            cancellationToken: cancellationToken);

        activity?.SetTag("Distance", distance);

        return distance;
    }
    internal async Task<double> DistanceBetweenTimingPoints(Guid Start, Guid End, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("DistanceBetweenTimingPoints");

        try 
        {
            activity?.SetTag("StartTimingPointId", Start);
            activity?.SetTag("EndTimingPointId", End);

            CoursePoint StartPoint = await FindCoursePointByTimingPointId(Start, cancellationToken);
            CoursePoint EndPoint = await FindCoursePointByTimingPointId(End, cancellationToken);

            return await DistanceBetweenCoursePoints(StartPoint, EndPoint, cancellationToken);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    internal async Task<CoursePoint> FindCoursePointByTimingPointId(Guid TimingPoint, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("FindCoursePointByTimingPointId");
        activity?.SetTag("TimingPointId", TimingPoint);
        try
        {
            var timingPoint = await cache.GetOrCreateAsync(
                $"CoursePointByTimingPoint_{TimingPoint}",
                async cancel => await storage.Set<CoursePoint>()
                    .SingleAsync(p => p.TimingPoint == TimingPoint, cancellationToken: cancel),
                cancellationToken: cancellationToken);
            activity?.SetTag("CoursePointId", timingPoint.Id);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return timingPoint;
        }
        catch (InvalidOperationException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
