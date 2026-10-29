using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Endpoints;

internal static class BestStatisticsResultsEndpoints
{
    internal static void MapBestStatisticsResultsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/best-results", GetBestResults)
            .AllowAnonymous()
            .WithName("GetBestStatisticsResults")
            .WithSummary("Get the fastest public result per statistics item; speed is in km/h");
    }

    private static async Task<Results<Ok<IReadOnlyList<BestStatisticsResultDTO>>, ProblemHttpResult>> GetBestResults(
        StatisticsDbContext storage, CancellationToken cancellationToken,
        Guid? facilityId = null, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        if (facilityId == Guid.Empty || (from.HasValue && to.HasValue && from >= to))
            return TypedResults.Problem(detail: "Provide a nonempty facility ID and a time range with from before to.", statusCode: 400);

        var entries = storage.Set<StatisticsEntry>().AsNoTracking()
            .Where(entry => entry.StatsProfile != null && entry.StatsProfile.IsPublic);
        if (facilityId.HasValue)
            entries = entries.Where(entry => entry.FacilityId == facilityId.Value);
        if (from.HasValue)
        {
            var start = from.Value.UtcDateTime;
            entries = entries.Where(entry => entry.TimeEnd >= start);
        }
        if (to.HasValue)
        {
            var end = to.Value.UtcDateTime;
            entries = entries.Where(entry => entry.TimeEnd < end);
        }

        var winners = await entries.GroupBy(entry => entry.StatisticsItemId)
            .Select(group => group.OrderBy(entry => entry.Duration)
                .ThenBy(entry => entry.TimeEnd).ThenBy(entry => entry.Id)
                .Select(entry => new BestStatisticsResultDTO(
                    entry.Id, entry.StatisticsItemId, entry.StatisticsItem.Name,
                    entry.StatsProfile!.ProfileName, entry.FacilityId, entry.Duration, entry.Speed * 3.6))
                .First())
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<BestStatisticsResultDTO>>(winners
            .OrderBy(result => result.StatisticsItemName, StringComparer.Ordinal)
            .ThenBy(result => result.StatisticsItemId).ToList());
    }
}
