using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.WebUI.Mud.Endpoints;

internal static class StatisticsEndpoints
{
    internal static void MapStatisticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/statistics/best-results", async (IStatisticsService statistics, CancellationToken cancellationToken,
            Guid? facilityId = null, DateTimeOffset? from = null, DateTimeOffset? to = null) =>
        {
            if (facilityId == Guid.Empty || (from.HasValue && to.HasValue && from >= to))
                return Results.Problem(detail: "Provide a nonempty facility ID and a time range with from before to.", statusCode: 400);

            return Results.Ok(await statistics.GetBestResultsAsync(facilityId, from, to, cancellationToken));
        }).AllowAnonymous();

        endpoints.MapGet("/statistics/samples", async (IStatisticsService statistics, CancellationToken cancellationToken,
            DateTime? cursor = null, bool isNextPage = false, int pageSize = 10) =>
        {
            if (pageSize <= 0)
            {
                return Results.BadRequest();
            }

            return Results.Ok((await statistics.GetSamplesAsync(cursor, isNextPage, pageSize, cancellationToken)).ToArray());
        }).AllowAnonymous();
    }
}