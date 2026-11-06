using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Service;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Api.Endpoints;

internal static class CourseLayoutEndpoints
{
    internal static void MapCourseLayoutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var coursePoints = endpoints.MapGroup("layouts");

        coursePoints.MapGet("{id}", GetCourseLayoutById);
    }

    async static Task<Results<Ok<CourseLayoutDTO>, NotFound>> GetCourseLayoutById(
        Guid id, [FromQuery] DateTimeOffset? at, FacilityDbContext storage,
        CancellationToken cancellationToken)
    {
        var layout = await storage.Set<CourseLayout>()
            .Include(l => l.Segments)
            .ThenInclude(s => s.Start)
            .Include(l => l.Segments)
            .ThenInclude(s => s.End)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (layout == null)
        {
            return TypedResults.NotFound();
        }
        var instant = at.HasValue ? TimingAssociationService.Normalize(at.Value) : DateTimeOffset.UtcNow;
        var associations = new TimingAssociationService(storage);
        var mappings = await associations.ResolveManyAsync(
            layout.Segments.SelectMany(segment => new[] { segment.Start.Id, segment.End.Id }),
            instant, cancellationToken);
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
                    TimingPointId = mappings.GetValueOrDefault(s.Start.Id)
                },
                End = new CoursePointDTO
                {
                    Id = s.End.Id,
                    Name = s.End.Name ?? string.Empty,
                    TimingPointId = mappings.GetValueOrDefault(s.End.Id)
                }
            }).ToList()
        });
    }
}