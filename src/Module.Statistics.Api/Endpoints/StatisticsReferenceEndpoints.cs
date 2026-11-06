using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Endpoints;

internal static class StatisticsReferenceEndpoints
{
    internal static void MapStatisticsReferenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var references = endpoints.MapGroup("/references").RequireAuthorization(AdministrationAuthorization.ReferencePolicy);
        references.MapGet("/facilities/{id:guid}/dependencies", async (Guid id, StatisticsDbContext storage, CancellationToken cancellationToken) =>
        {
            var count = await storage.Set<Sample>().CountAsync(sample => sample.FacilityId == id, cancellationToken) +
                await storage.Set<StatisticsEntry>().CountAsync(entry => entry.FacilityId == id, cancellationToken);
            return Results.Ok(new DependencySummary(count, $"This facility has {count} recorded statistics sample(s) or result(s)."));
        }).Produces<DependencySummary>();
        references.MapGet("/coursepoints/{id:guid}/dependencies", async (Guid id, StatisticsDbContext storage, CancellationToken cancellationToken) =>
        {
            var configs = await storage.Set<SimpleStatisticsItemConfig>().CountAsync(config => config.CoursePointStart == id || config.CoursePointEnd == id, cancellationToken);
            var samples = await storage.Set<Sample>().CountAsync(sample => sample.CoursePointStartId == id || sample.CoursePointEndId == id, cancellationToken);
            return Results.Ok(new DependencySummary(configs + samples, $"This coursepoint is used by {configs} statistics configuration(s) and {samples} recorded sample(s). Create a new layout instead of changing recorded course geometry."));
        }).Produces<DependencySummary>();
        references.MapGet("/transponders/{id:guid}/dependencies", async (Guid id, StatisticsDbContext storage, CancellationToken cancellationToken) =>
        {
            var count = await storage.Set<Sample>().CountAsync(sample => sample.TransponderId == id, cancellationToken) +
                await storage.Set<StatisticsEntry>().CountAsync(entry => entry.TransponderId == id, cancellationToken);
            return Results.Ok(new DependencySummary(count, $"This transponder has {count} recorded statistics sample(s) or result(s)."));
        }).Produces<DependencySummary>();
    }
}