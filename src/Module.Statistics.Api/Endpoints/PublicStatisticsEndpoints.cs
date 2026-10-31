using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Endpoints;

internal static class PublicStatisticsEndpoints
{
    internal static void MapPublicStatisticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/public").AllowAnonymous();
        group.MapGet("/discovery", GetDiscovery).WithName("GetPublicStatisticsDiscovery")
            .ProducesProblem(400).ProducesProblem(502).ProducesProblem(503);
        group.MapGet("/results", GetResults).WithName("GetPublicStatisticsResults")
            .ProducesProblem(400).ProducesProblem(502).ProducesProblem(503);
    }

    private static async Task<Results<Ok<PublicStatisticsDiscoveryDTO>, ProblemHttpResult>> GetDiscovery(
        HttpContext http, PublicStatisticsDiscovery discovery, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (http.Request.Query.Count != 0)
            return TypedResults.Problem("Discovery does not accept query parameters.", statusCode: 400);
        try
        {
            return TypedResults.Ok(await discovery.GetAsync(cancellationToken));
        }
        catch (Exception exception) when (IsDependencyFailure(exception, cancellationToken))
        {
            return DependencyProblem(exception, loggerFactory);
        }
    }

    // Explicit wire parameters expose the shared query contract in generated OpenAPI/NSwag.
    // Parsing uses raw single-valued query strings so malformed/duplicate values produce 400.
    private static async Task<Results<Ok<PublicStatisticsResultsPageDTO>, ValidationProblem, ProblemHttpResult>> GetResults(
        HttpContext http, StatisticsDbContext storage, PublicStatisticsDiscovery discovery,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken,
        string? facilityId = null, string? itemId = null, string? configurationId = null,
        string? name = null, string? minTime = null, string? maxTime = null,
        string? minSpeed = null, string? maxSpeed = null, string? cursor = null, string? direction = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        var duplicates = http.Request.Query.Where(pair => pair.Value.Count != 1)
            .ToDictionary(pair => pair.Key, _ => new[] { "Supply this query parameter exactly once." });
        if (duplicates.Count != 0) return TypedResults.ValidationProblem(duplicates);
        var queryValues = http.Request.Query.ToDictionary(pair => pair.Key, pair => (string?)pair.Value[0]);
        if (!PublicStatisticsQuery.TryParse(queryValues, out var request, out var errors))
            return TypedResults.ValidationProblem(errors);

        try
        {
            if (!await discovery.IsSelectionAvailableAsync(request!, cancellationToken))
                return TypedResults.Problem("Select an available configuration matching the item and facility.", statusCode: 400);
        }
        catch (Exception exception) when (IsDependencyFailure(exception, cancellationToken))
        {
            return DependencyProblem(exception, loggerFactory);
        }

        return TypedResults.Ok(await QueryPage(storage, request!, cancellationToken));
    }

    internal static async Task<PublicStatisticsResultsPageDTO> QueryPage(
        StatisticsDbContext storage, GetPublicStatisticsResultsRequest request, CancellationToken cancellationToken)
    {
        var filtered = storage.Set<StatisticsEntry>().AsNoTracking()
            .Where(entry => entry.StatsProfile != null && entry.StatsProfile.IsPublic &&
                entry.FacilityId == request.FacilityId && entry.StatisticsItemId == request.StatisticsItemId &&
                entry.StatisticsItemConfigId == request.StatisticsItemConfigId);
        if (request.Name is not null)
        {
            var pattern = "%" + request.Name.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            filtered = filtered.Where(entry => EF.Functions.ILike(entry.StatsProfile!.ProfileName, pattern, "\\"));
        }
        if (request.MinDuration is { } minDuration) filtered = filtered.Where(entry => entry.Duration >= minDuration);
        if (request.MaxDuration is { } maxDuration) filtered = filtered.Where(entry => entry.Duration <= maxDuration);
        if (request.MinSpeedKmh is { } minSpeed)
        {
            filtered = filtered.Where(entry => entry.Speed * 3.6d >= minSpeed);
        }
        if (request.MaxSpeedKmh is { } maxSpeed)
        {
            filtered = filtered.Where(entry => entry.Speed * 3.6d <= maxSpeed);
        }

        var previousDirection = request.Direction == PublicStatisticsPageDirection.Previous;
        var pageQuery = request.Cursor is { } anchor ? Boundary(filtered, anchor, previousDirection) : filtered;
        var ordered = previousDirection
            ? pageQuery.OrderBy(entry => entry.TimeEnd).ThenBy(entry => entry.Id)
            : pageQuery.OrderByDescending(entry => entry.TimeEnd).ThenByDescending(entry => entry.Id);
        var rows = await ordered.Take(PublicStatisticsQuery.FetchSize)
            .Select(entry => new PublicStatisticsResultDTO(entry.Id, entry.StatsProfile!.ProfileName,
                entry.StatisticsItemId, entry.StatisticsItemConfigId, entry.FacilityId,
                entry.Duration, entry.Speed * 3.6d, new DateTimeOffset(entry.TimeEnd)))
            .ToListAsync(cancellationToken);
        var lookahead = rows.Count > PublicStatisticsQuery.PageSize;
        if (lookahead) rows.RemoveAt(PublicStatisticsQuery.PageSize);
        if (previousDirection) rows.Reverse();

        if (rows.Count == 0)
        {
            if (request.Cursor is not { } emptyAnchor) return new(rows, null, null);
            var hasPrevious = await Boundary(filtered, emptyAnchor, true).AnyAsync(cancellationToken);
            var hasNext = await Boundary(filtered, emptyAnchor, false).AnyAsync(cancellationToken);
            return new(rows, hasPrevious ? emptyAnchor : null, hasNext ? emptyAnchor : null);
        }
        var first = new PublicStatisticsCursorDTO(rows[0].TimeEnd, rows[0].Id);
        var last = new PublicStatisticsCursorDTO(rows[^1].TimeEnd, rows[^1].Id);
        var previous = previousDirection && lookahead || await Boundary(filtered, first, true).AnyAsync(cancellationToken);
        var next = !previousDirection && lookahead || await Boundary(filtered, last, false).AnyAsync(cancellationToken);
        return new(rows, previous ? first : null, next ? last : null);
    }

    private static IQueryable<StatisticsEntry> Boundary(IQueryable<StatisticsEntry> query,
        PublicStatisticsCursorDTO cursor, bool greater)
    {
        var timestamp = cursor.TimeEnd.UtcDateTime;
        var id = cursor.Id;
        return greater
            ? query.Where(entry => EF.Functions.GreaterThan(
                ValueTuple.Create(entry.TimeEnd, entry.Id), ValueTuple.Create(timestamp, id)))
            : query.Where(entry => EF.Functions.LessThan(
                ValueTuple.Create(entry.TimeEnd, entry.Id), ValueTuple.Create(timestamp, id)));
    }

    private static bool IsDependencyFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException ||
        exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    private static ProblemHttpResult DependencyProblem(Exception exception, ILoggerFactory loggerFactory)
    {
        loggerFactory.CreateLogger("PublicStatistics").LogWarning(exception, "Public statistics facility resolution failed.");
        var unavailable = exception is OperationCanceledException ||
            exception is HttpRequestException { StatusCode: HttpStatusCode.ServiceUnavailable };
        return TypedResults.Problem("Statistics configuration discovery could not resolve Facilities.",
            statusCode: unavailable ? 503 : 502);
    }
}
