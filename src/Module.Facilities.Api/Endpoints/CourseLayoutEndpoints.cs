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
        var layout = await CourseConfigurationService.LayoutsWithHistory(storage).AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (layout == null)
        {
            return TypedResults.NotFound();
        }
        var instant = at.HasValue ? TimingAssociationService.Normalize(at.Value) : DateTimeOffset.UtcNow;
        return TypedResults.Ok((await CourseLayoutProjection.ToDtosAsync(storage, [layout], instant, cancellationToken)).Single());
    }
}
