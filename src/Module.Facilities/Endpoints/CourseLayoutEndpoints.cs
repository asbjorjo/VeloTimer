using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Endpoints;

internal static class CourseLayoutEndpoints
{
    internal static void MapCourseLayoutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var coursePoints = endpoints.MapGroup("layouts");

        coursePoints.MapGet("{id}", GetCourseLayoutById);
    }

    async static Task<Results<Ok<CourseLayoutDTO>, NotFound>> GetCourseLayoutById(Guid id, FacilityDbContext storage)
    {
        var layout = await storage.Set<CourseLayout>()
            .Include(l => l.Segments)
            .ThenInclude(s => s.Start)
            .Include(l => l.Segments)
            .ThenInclude(s => s.End)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (layout == null)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Ok(new CourseLayoutDTO
        {
            Id = layout.Id,
            FacilityId = layout.FacilityId,
            Segments = layout.Segments.Select(s => new SegmentDTO
            {
                Id = s.Id,
                Order = s.Order,
                Length = s.Length,
                Start = new CoursePointDTO
                {
                    Id = s.Start.Id,
                    Name = s.Start.Name ?? string.Empty,
                    TimingPointId = s.Start.TimingPoint
                },
                End = new CoursePointDTO
                {
                    Id = s.End.Id,
                    Name = s.End.Name ?? string.Empty,
                    TimingPointId = s.End.TimingPoint
                }
            }).ToList()
        });
    }
}