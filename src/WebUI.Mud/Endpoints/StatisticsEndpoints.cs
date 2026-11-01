using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.WebUI.Mud.Endpoints;

internal static class StatisticsEndpoints
{
    internal static void MapStatisticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/statistics/public/discovery", GetPublicDiscovery).AllowAnonymous();
        endpoints.MapGet("/statistics/public/results", GetPublicResults).AllowAnonymous();

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

    private static async Task<IResult> GetPublicDiscovery(HttpContext http, IStatisticsService statistics,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (http.Request.Query.Count != 0)
            return Results.Problem(detail: "Discovery does not accept query parameters.", statusCode: 400);
        return await PublicResponse(http, async () =>
            Results.Ok(await statistics.GetPublicDiscoveryAsync(cancellationToken)));
    }

    private static async Task<IResult> GetPublicResults(HttpContext http, IStatisticsService statistics,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var duplicates = http.Request.Query.Where(pair => pair.Value.Count != 1)
            .ToDictionary(pair => pair.Key, _ => new[] { "Supply this query parameter exactly once." });
        if (duplicates.Count != 0) return Results.ValidationProblem(duplicates);
        var values = http.Request.Query.ToDictionary(pair => pair.Key, pair => (string?)pair.Value[0]);
        if (!PublicStatisticsQuery.TryParse(values, out var request, out var errors))
            return Results.ValidationProblem(errors);
        return await PublicResponse(http, async () =>
            Results.Ok(await statistics.GetPublicResultsAsync(request!, cancellationToken)));
    }

    private static async Task<IResult> PublicResponse(HttpContext http, Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (HttpRequestException exception)
        {
            // Public dependency messages are untrusted, even for validation/outage statuses.
            // Local query validation above keeps its safe, actionable details.
            var status = exception.StatusCode is { } upstream ? (int)upstream : 502;
            status = status is 400 or 401 or 403 or 404 or 503 ? status : 502;
            http.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("WebUI.PublicStatistics")
                .LogWarning("Public statistics request failed for {Path}, status {Status}", http.Request.Path, status);
            return Results.Problem(statusCode: status, detail: status switch
            {
                400 => "Select an available configuration and valid public statistics filters.",
                503 => "Public statistics is temporarily unavailable.",
                _ => "Public statistics could not be loaded."
            });
        }
    }
}