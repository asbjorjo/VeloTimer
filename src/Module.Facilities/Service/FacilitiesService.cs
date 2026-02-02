using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Service;

internal class FacilitiesService(FacilityDbContext storage)
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

    internal async Task<double> DistanceBetweenCoursePoints(CoursePoint Start, CoursePoint End)
    {
        using var activity = Instrumentation.Source.StartActivity("DistanceBetweenCoursePoints");

        activity?.SetTag("StartCoursePointId", Start.Id);
        activity?.SetTag("EndCoursePointId", End.Id);

        double distance = 0;
        var segments = await storage.Set<CourseLayout>()
            .Include(l => l.Segments)
                .ThenInclude(s => s.Start)
            .Include(l => l.Segments)
                .ThenInclude(s => s.End)
            .Where(l => l.Segments.Select(s => s.Start.Id).Contains(Start.Id) || l.Segments.Select(s => s.End.Id).Contains(End.Id))
            .SelectMany(l => l.Segments)
            .OrderBy(s => s.Order)
            .ToListAsync();

        CourseSegment start = segments.Single(s => s.Start == Start);
        CourseSegment end = Start == End ? start : segments.Single(s => s.End == End);

        activity?.SetTag("StartSegmentId", start.Id);
        activity?.SetTag("EndSegmentId", end.Id);

        if (Start == End)
        {
            distance = segments.Sum(s => s.Length);
            activity?.SetTag("Distance", distance);
            return distance;
        }


        if (start.Order < end.Order)
        {
            distance = segments.Where(s => s.Order >= start.Order && s.Order <= end.Order).Sum(s => s.Length);
        }
        else
        {
            distance = segments.Where(s => s.Order <= start.Order && s.Order >= end.Order).Sum(s => s.Length);
        }

        activity?.SetTag("Distance", distance);

        return distance;
    }
    internal async Task<double> DistanceBetweenTimingPoints(Guid Start, Guid End)
    {
        using var activity = Instrumentation.Source.StartActivity("DistanceBetweenTimingPoints");

        try 
        {
            activity?.SetTag("StartTimingPointId", Start);
            activity?.SetTag("EndTimingPointId", End);

            CoursePoint StartPoint = await storage.Set<CoursePoint>()
                .SingleAsync(p => p.TimingPoint == Start);
            CoursePoint EndPoint = Start == End ? StartPoint : await storage.Set<CoursePoint>()
                .SingleAsync(p => p.TimingPoint == End);

            return await DistanceBetweenCoursePoints(StartPoint, EndPoint);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    internal async Task<CoursePoint> FindCoursePointByTimingPointId(Guid TimingPoint)
    {
        using var activity = Instrumentation.Source.StartActivity("FindCoursePointByTimingPointId");
        activity?.SetTag("TimingPointId", TimingPoint);
        try
        {
            var timingPoint = await storage.Set<CoursePoint>()
                .SingleAsync(p => p.TimingPoint == TimingPoint);
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
