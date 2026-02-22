using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Endpoints;

internal static class FacilityEndpoints
{
    internal static void MapFacilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var facilities = endpoints.MapGroup("facility");

        facilities.MapGet("", ListFacilities);
        facilities.MapGet("{id}", GetFacilityById);
        facilities.MapGet("{id}/layouts", GetLayoutsForFacility);
    }

    private static async Task<Results<Ok<List<CourseLayoutDTO>>, NotFound>> GetLayoutsForFacility(Guid id, FacilityDbContext storage)
    {
        var layouts = storage.Set<CourseLayout>()
            .Where(cl => cl.FacilityId == id);

        if (!layouts.Any())
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(
            await layouts
                .Select(cl => new CourseLayoutDTO
                {
                    Id = cl.Id,
                    FacilityId = cl.FacilityId,
                    Segments = cl.Segments.Select(s => new SegmentDTO
                    {
                        Id = s.Id,
                        Order = s.Order,
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
                        },
                        Length = s.Length
                    }).ToList()
                }).ToListAsync());
    }

    async static Task<Ok<List<FacilityDTO>>> ListFacilities(FacilityDbContext storage)
    {
        return TypedResults.Ok(
            await storage.Set<Facility>()
                        .Select(f => new FacilityDTO {
                            Id = f.Id,
                            Name = f.Name,
                            Layouts = f.Layouts.Select(l => l.Id).ToList()
                        }).ToListAsync());
    }

    async static Task<Results<Ok<FacilityDTO>, NotFound>> GetFacilityById(Guid id, FacilityDbContext storage)
    {
        var facility = await storage.Set<Facility>()
            .Include(f => f.Layouts)
            .FirstOrDefaultAsync(f => f.Id == id);
        if (facility == null)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Ok(new FacilityDTO
        {
            Id = facility.Id,
            Name = facility.Name,
            Layouts = facility.Layouts.Select(l => l.Id).ToList()
        });
    }
}
