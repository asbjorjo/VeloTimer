using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Service;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Endpoints;

internal static class CoursePointEndpoints
{
    internal static void MapCoursePointEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var coursePoints = endpoints.MapGroup("coursepoint");
        
        coursePoints.MapGet("{id}", GetCoursePointById).CacheOutput();
        coursePoints.MapGet("{start}/distance/{end}", DistanceBetweenCoursePoints).CacheOutput();

        var timingPoints = endpoints.MapGroup("timingpoint");
        timingPoints.MapGet("{id}", GetCoursePointByTimingPoint).CacheOutput();
        timingPoints.MapGet("{start}/distance/{end}", DistanceBetweenTimingPoints).CacheOutput();
    }

    static async Task<Results<Ok<CoursePointDTO>, NotFound>> GetCoursePointById(Guid id, FacilityDbContext storage) =>
        await storage.Set<CoursePoint>()
            .Select(c => new CoursePointDTO { Id = c.Id, TimingPointId = c.TimingPoint, Name = c.Name ?? string.Empty })
            .SingleOrDefaultAsync(c => c.Id == id) is CoursePointDTO coursePoint
            ? TypedResults.Ok(coursePoint)
            : TypedResults.NotFound();

    static async Task<Ok<double>> DistanceBetweenCoursePoints(Guid start, Guid end, FacilitiesService facilities, FacilityDbContext storage)
    {
        var startpoint = await storage.Set<CoursePoint>().SingleAsync(c => c.Id == start);
        var endpoint = start == end ? startpoint : await storage.Set<CoursePoint>().SingleAsync(c => c.Id == end);
        return TypedResults.Ok(await facilities.DistanceBetweenCoursePoints(startpoint, endpoint));
    }

    static async Task<Results<Ok<CoursePointDistance>, NotFound>> DistanceBetweenTimingPoints(Guid start, Guid end, FacilitiesService facilities, FacilityDbContext storage)
    {
        var startpoint = await storage.Set<CoursePoint>().SingleOrDefaultAsync(c => c.TimingPoint == start);
        var endpoint = start == end ? startpoint : await storage.Set<CoursePoint>().SingleOrDefaultAsync(c => c.TimingPoint == end);

        if (startpoint == null || endpoint == null )
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new CoursePointDistance(
            startpoint.Id,
            endpoint.Id,
            await facilities.DistanceBetweenCoursePoints(startpoint, endpoint)
            )
        );
    }

    static async Task<Results<Ok<CoursePointDTO>, NotFound>> GetCoursePointByTimingPoint(Guid id, FacilityDbContext storage) =>
        await storage.Set<CoursePoint>()
            .Select(c => new CoursePointDTO { Id = c.Id, TimingPointId = c.TimingPoint, Name = c.Name ?? string.Empty })
            .SingleOrDefaultAsync(c => c.TimingPointId == id) is CoursePointDTO coursePoint
            ? TypedResults.Ok(coursePoint)
            : TypedResults.NotFound();
}
