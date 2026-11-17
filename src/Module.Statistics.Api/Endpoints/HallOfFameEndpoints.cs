using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Endpoints;

internal static class HallOfFameEndpoints
{
    internal static void MapHallOfFameEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/hall-of-fame", Get)
            .AllowAnonymous()
            .WithName("GetHallOfFame")
            .WithSummary("Get ten all-time public personal bests or lap totals; quantities use SI units")
            .ProducesProblem(400).ProducesProblem(409);

    private static async Task<Results<Ok<HallOfFameDTO>, ValidationProblem, ProblemHttpResult>> Get(
        HttpContext http, StatisticsDbContext storage, ILoggerFactory loggerFactory, CancellationToken cancellationToken,
        string? category = null, string? facilityId = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        var duplicates = http.Request.Query.Where(pair => pair.Value.Count != 1)
            .ToDictionary(pair => pair.Key, _ => new[] { "Supply each parameter exactly once." });
        if (duplicates.Count != 0) return TypedResults.ValidationProblem(duplicates);
        var values = http.Request.Query.ToDictionary(pair => pair.Key, pair => (string?)pair.Value[0]);
        if (!HallOfFameQuery.TryParse(values, out var request, out var errors))
            return TypedResults.ValidationProblem(errors);

        var name = HallOfFameQuery.ItemName(request.Category);
        var items = await storage.Set<StatisticsItem>().AsNoTracking()
            .Where(item => item.Name == name).Take(2).ToListAsync(cancellationToken);
        if (items.Count == 0) return TypedResults.Ok(new HallOfFameDTO(false, [], []));
        if (items.Count != 1 || request.Category == "laps" &&
            (!float.IsFinite(items[0].Distance) || items[0].Distance <= 0))
        {
            loggerFactory.CreateLogger("HallOfFame").LogWarning(
                "Hall of Fame category {Category} has ambiguous definitions or an invalid lap distance.", request.Category);
            return TypedResults.Problem("The statistics definition must be corrected before this leaderboard can be loaded.", statusCode: 409);
        }

        var itemId = items[0].Id;
        var entries = storage.Set<StatisticsEntry>().AsNoTracking().Where(entry =>
            entry.StatisticsItemId == itemId && entry.IsValid &&
            entry.StatsProfile != null && entry.StatsProfile.IsPublic);
        if (request.FacilityId.HasValue)
            entries = entries.Where(entry => entry.FacilityId == request.FacilityId.Value);

        if (request.Category == "laps")
        {
            var distance = (double)items[0].Distance;
            var laps = await entries.GroupBy(entry => new { entry.StatsProfileUserId, entry.StatsProfile!.ProfileName })
                .Select(group => new { group.Key.StatsProfileUserId, group.Key.ProfileName, Count = group.LongCount() })
                .OrderByDescending(row => row.Count).ThenBy(row => row.StatsProfileUserId)
                .Take(HallOfFameQuery.Limit)
                .Select(row => new HallOfFameLapsDTO(row.ProfileName, row.Count, row.Count * distance))
                .ToListAsync(cancellationToken);
            return TypedResults.Ok(new HallOfFameDTO(true, [], laps));
        }

        // A correlated anti-join keeps the winning entry's facility/speed together
        // and applies the top-ten limit in SQL, before materializing any results.
        var winners = entries.Where(entry => !entries.Any(other =>
            other.StatsProfileUserId == entry.StatsProfileUserId &&
            (other.Duration < entry.Duration ||
             other.Duration == entry.Duration && other.TimeEnd < entry.TimeEnd ||
             other.Duration == entry.Duration && other.TimeEnd == entry.TimeEnd && other.Id.CompareTo(entry.Id) < 0)));
        var times = await winners.OrderBy(entry => entry.Duration)
            .ThenBy(entry => entry.TimeEnd).ThenBy(entry => entry.Id)
            .Take(HallOfFameQuery.Limit)
            .Select(entry => new HallOfFameTimeDTO(entry.StatsProfile!.ProfileName,
                entry.Duration, entry.Speed, entry.FacilityId))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new HallOfFameDTO(true, times, []));
    }
}
