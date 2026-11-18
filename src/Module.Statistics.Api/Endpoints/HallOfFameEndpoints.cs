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
            (items[0].DistanceMode is not (StatisticsDistanceMode.Fixed or StatisticsDistanceMode.CourseLayout) ||
             items[0].DistanceMode == StatisticsDistanceMode.Fixed && (!float.IsFinite(items[0].Distance) || items[0].Distance <= 0) ||
             items[0].DistanceMode == StatisticsDistanceMode.CourseLayout && items[0].Distance != 0))
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
            // Old writers can still insert unsnapshotted rows between the additive migration
            // and cutover. Only a Fixed definition has an authoritative fallback distance.
            var fixedMode = items[0].DistanceMode == StatisticsDistanceMode.Fixed;
            var distance = (double)items[0].Distance;
            if (await entries.AnyAsync(entry =>
                entry.DistanceMetres == null && !fixedMode ||
                entry.DistanceMetres != null && (entry.DistanceMetres <= 0 || entry.DistanceMetres > double.MaxValue), cancellationToken))
            {
                loggerFactory.CreateLogger("HallOfFame").LogWarning("Lap entries have missing or invalid distance snapshots.");
                return TypedResults.Problem("Lap distance snapshots require correction before this leaderboard can be loaded.", statusCode: 409);
            }
            // Repeat eligibility in the aggregate: a concurrent bad insert must never
            // turn the CourseLayout sentinel into a fallback distance.
            var eligible = entries.Where(entry => entry.DistanceMetres == null && fixedMode ||
                entry.DistanceMetres > 0 && entry.DistanceMetres <= double.MaxValue);
            var laps = await eligible.GroupBy(entry => new { entry.StatsProfileUserId, entry.StatsProfile!.ProfileName })
                .Select(group => new { group.Key.StatsProfileUserId, group.Key.ProfileName, Count = group.LongCount(),
                    Distance = group.Sum(entry => entry.DistanceMetres ?? distance) })
                .OrderByDescending(row => row.Count).ThenBy(row => row.StatsProfileUserId)
                .Take(HallOfFameQuery.Limit)
                .Select(row => new HallOfFameLapsDTO(row.ProfileName, row.Count, row.Distance))
                .ToListAsync(cancellationToken);
            return TypedResults.Ok(new HallOfFameDTO(true, [], laps));
        }

        // Selecting each public profile's first entry (ordered by duration, completion,
        // then entry id) lets EF Core emit a single windowed query, ROW_NUMBER() OVER
        // (PARTITION BY profile ORDER BY ...) filtered to row 1 and joined to the profile,
        // so each winning entry keeps its own facility/speed and the top-ten limit is
        // applied in SQL before materializing any results. The facility filter already
        // sits in `entries`, so the partition only sees entries at that facility.
        // EF Core 10 cannot compose ordering or projection after GroupBy(...).First(),
        // which is why the per-profile correlated Take(1) is used to express the window.
        if (request.Category == "lap" && !request.FacilityId.HasValue)
        {
            var speedEntries = entries.Where(entry => entry.Speed > 0 && entry.Speed <= double.MaxValue);
            var speedWinners = storage.Set<StatsProfile>().AsNoTracking()
                .Where(profile => profile.IsPublic)
                .SelectMany(profile => speedEntries.Where(entry => entry.StatsProfileUserId == profile.UserId)
                    .OrderByDescending(entry => entry.Speed).ThenBy(entry => entry.TimeEnd).ThenBy(entry => entry.Id)
                    .Take(1),
                    (profile, entry) => new { profile.ProfileName, entry.Id, entry.Duration, entry.TimeEnd, entry.Speed, entry.FacilityId });
            var fastest = await speedWinners.OrderByDescending(winner => winner.Speed)
                .ThenBy(winner => winner.TimeEnd).ThenBy(winner => winner.Id)
                .Take(HallOfFameQuery.Limit)
                .Select(winner => new HallOfFameTimeDTO(winner.ProfileName, winner.Duration, winner.Speed, winner.FacilityId))
                .ToListAsync(cancellationToken);
            return TypedResults.Ok(new HallOfFameDTO(true, fastest, []));
        }

        var winners = storage.Set<StatsProfile>().AsNoTracking()
            .Where(profile => profile.IsPublic)
            .SelectMany(profile => entries.Where(entry => entry.StatsProfileUserId == profile.UserId)
                    .OrderBy(entry => entry.Duration).ThenBy(entry => entry.TimeEnd).ThenBy(entry => entry.Id)
                    .Take(1),
                (profile, entry) => new
                {
                    profile.ProfileName, entry.Id, entry.Duration, entry.TimeEnd, entry.Speed, entry.FacilityId,
                });
        var times = await winners.OrderBy(winner => winner.Duration)
            .ThenBy(winner => winner.TimeEnd).ThenBy(winner => winner.Id)
            .Take(HallOfFameQuery.Limit)
            .Select(winner => new HallOfFameTimeDTO(winner.ProfileName,
                winner.Duration, winner.Speed, winner.FacilityId))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new HallOfFameDTO(true, times, []));
    }
}
