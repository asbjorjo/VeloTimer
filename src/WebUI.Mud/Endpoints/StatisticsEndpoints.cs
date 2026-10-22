using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.WebUI.Mud.Endpoints;

internal static class StatisticsEndpoints
{
    internal static void MapStatisticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
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