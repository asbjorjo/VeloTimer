using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;
using VeloTime.Module.Common;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Logging;

namespace VeloTime.Module.Facilities.Api.Endpoints;

internal static class FacilityEndpoints
{
    internal static void MapFacilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var facilities = endpoints.MapGroup("facility");

        facilities.MapGet("", ListFacilities);
        facilities.MapGet("{id}", GetFacilityById);
        facilities.MapGet("{id}/layouts", GetLayoutsForFacility);
        facilities.MapPost("", CreateFacility).WithName("CreateFacility").RequireAuthorization(AdministrationAuthorization.FacilitiesPolicy);
        facilities.MapPut("{id:guid}", UpdateFacility).WithName("UpdateFacility").RequireAuthorization(AdministrationAuthorization.FacilitiesPolicy);
        facilities.MapDelete("{id:guid}", DeleteFacility).WithName("DeleteFacility").RequireAuthorization(AdministrationAuthorization.FacilitiesPolicy)
            .AddEndpointFilter<AdministrationDependencyFilter>();
    }

    private static async Task<Results<Ok<List<CourseLayoutDTO>>, NotFound>> GetLayoutsForFacility(Guid id, FacilityDbContext storage)
    {
        var layouts = storage.Set<CourseLayout>()
            .Where(cl => cl.FacilityId == id);

        if (!await storage.Set<Facility>().AnyAsync(facility => facility.Id == id))
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

    private static async Task<Results<Created<FacilityDTO>, ProblemHttpResult>> CreateFacility(SaveFacilityRequest request, FacilityDbContext storage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return TypedResults.Problem(detail: "Facility name is required.", statusCode: 400);
        var facility = new Facility { Id = Guid.NewGuid(), Name = request.Name.Trim() };
        storage.Add(facility);
        await storage.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/facilities/facility/{facility.Id}", new FacilityDTO { Id = facility.Id, Name = facility.Name, Layouts = [] });
    }

    private static async Task<Results<Ok<FacilityDTO>, NotFound, ProblemHttpResult>> UpdateFacility(Guid id, SaveFacilityRequest request,
        FacilityDbContext storage, HybridCache cache, IOutputCacheStore output, ICacheInvalidationOperations invalidation,
        ILogger<FacilityDbContext> logger, CancellationToken cancellationToken)
    {
        var facility = await storage.Set<Facility>().Include(facility => facility.Layouts).SingleOrDefaultAsync(facility => facility.Id == id, cancellationToken);
        if (facility is null) return TypedResults.NotFound();
        if (string.IsNullOrWhiteSpace(request.Name)) return TypedResults.Problem(detail: "Facility name is required.", statusCode: 400);
        facility.Name = request.Name.Trim();
        await storage.SaveChangesAsync(cancellationToken);
        await FacilitiesOutputCache.InvalidateRelationshipsWithOperationsAsync(cache, output, logger, invalidation);
        return TypedResults.Ok(new FacilityDTO { Id = facility.Id, Name = facility.Name, Layouts = facility.Layouts.Select(layout => layout.Id).ToList() });
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteFacility(Guid id, FacilityDbContext storage, IAdministrationReferences references, CancellationToken cancellationToken)
    {
        var facility = await storage.Set<Facility>().FindAsync([id], cancellationToken);
        if (facility is null) return TypedResults.NotFound();
        var layouts = await storage.Set<CourseLayout>().CountAsync(layout => layout.FacilityId == id, cancellationToken);
        if (layouts > 0) return TypedResults.Problem(detail: $"This facility still has {layouts} course layout(s).", statusCode: 409);
        foreach (var module in new[] { "timing", "statistics" })
        {
            var dependencies = await references.GetDependenciesAsync(module, "facilities", id, cancellationToken);
            if (dependencies.Count > 0) return TypedResults.Problem(detail: dependencies.Description, statusCode: 409);
        }
        storage.Remove(facility);
        await storage.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
