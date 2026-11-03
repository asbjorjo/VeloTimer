using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Common;
using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;
using FacilitiesApiException = VeloTime.Module.Facilities.Client.ApiException;

namespace VeloTime.Module.Statistics.Api.Endpoints;

internal static class HallOfFameEndpoints
{
    internal static void MapHallOfFameEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/statistics/hall-of-fame", GetPublicHallOfFame)
            .AllowAnonymous()
            .WithTags("Statistics")
            .WithName("GetHallOfFame")
            .WithSummary("Get configured all-time public Hall of Fame rankings")
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        var settings = endpoints.MapGroup("/api/admin/statistics/hall-of-fame")
            .RequireAuthorization(AdministrationAuthorization.StatisticsPolicy);
        settings.MapGet("", GetSettings)
            .WithName("GetHallOfFameSettings")
            .ProducesProblem(StatusCodes.Status500InternalServerError);
        settings.MapPut("", ReplaceSettings)
            .WithName("ReplaceHallOfFameSettings")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Results<Ok<HallOfFameDTO>, ProblemHttpResult>> GetPublicHallOfFame(
        HttpContext http,
        HallOfFameSettingsStore settingsStore,
        HallOfFameService hallOfFame,
        StatisticsDbContext storage,
        IFacilitiesClient facilities,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var logger = loggerFactory.CreateLogger("HallOfFame");
        var settings = await settingsStore.GetAsync(cancellationToken);
        if (GetSettingsErrors(settings).Count != 0)
        {
            logger.LogError("Persisted Hall of Fame settings are structurally invalid at version {Version}.", settings.Version);
            return TypedResults.Problem(
                detail: "Persisted Hall of Fame settings are invalid. An administrator must review the settings.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var enabledSections = settings.Sections.Where(section => section.IsEnabled).ToArray();
        if (enabledSections.Length == 0)
            return TypedResults.Ok(new HallOfFameDTO([]));

        var itemIds = enabledSections.Select(section => section.StatisticsItemId).Distinct().ToArray();
        var items = await storage.Set<StatisticsItem>().AsNoTracking()
            .Where(item => itemIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Name })
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        if (items.Count != itemIds.Length)
        {
            logger.LogError(
                "Persisted Hall of Fame settings at version {Version} reference missing statistics items.",
                settings.Version);
            return TypedResults.Problem(
                detail: "Persisted Hall of Fame settings reference a missing statistics item. An administrator must review the settings.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        try
        {
            var queried = new List<(HallOfFameSectionSettingsDTO Settings, IReadOnlyList<HallOfFameProfileResult> Rows)>();
            foreach (var section in enabledSections)
            {
                var rows = await hallOfFame.GetRankingAsync(
                    section.StatisticsItemId,
                    section.RankingMode == HallOfFameRankingMode.MostPassings,
                    section.RowCount,
                    cancellationToken);
                queried.Add((section, rows));
            }

            var needsFacilities = queried.Any(section =>
                section.Settings.Columns.Contains(HallOfFameColumn.FacilityOfBestResult) &&
                section.Rows.Count > 0);
            var facilityNames = needsFacilities
                ? await GetFacilityNamesAsync(
                    facilities,
                    queried.Where(section => section.Settings.Columns.Contains(HallOfFameColumn.FacilityOfBestResult))
                        .SelectMany(section => section.Rows)
                        .Select(row => row.BestFacilityId)
                        .Distinct()
                        .ToArray(),
                    cancellationToken)
                : new Dictionary<Guid, string>();

            var response = queried.Select(section => new HallOfFameSectionDTO(
                section.Settings.StatisticsItemId,
                items[section.Settings.StatisticsItemId],
                section.Settings.RankingMode,
                section.Settings.RowCount,
                section.Settings.Columns.ToArray(),
                section.Rows.Select(row => MapEntry(row, section.Settings.Columns, facilityNames)).ToArray()))
                .ToArray();
            return TypedResults.Ok(new HallOfFameDTO(response));
        }
        catch (Exception exception) when (IsFacilityFailure(exception, cancellationToken))
        {
            var unavailable = exception is OperationCanceledException ||
                exception is FacilitiesApiException { StatusCode: StatusCodes.Status503ServiceUnavailable } ||
                exception is HttpRequestException { StatusCode: HttpStatusCode.ServiceUnavailable };
            logger.LogWarning(exception, "Hall of Fame facility name resolution failed.");
            return TypedResults.Problem(
                detail: unavailable
                    ? "Facility names are temporarily unavailable. Please retry."
                    : "Facility names could not be loaded.",
                statusCode: unavailable
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IReadOnlyDictionary<Guid, string>> GetFacilityNamesAsync(
        IFacilitiesClient facilities,
        IReadOnlyCollection<Guid> requiredIds,
        CancellationToken cancellationToken)
    {
        var all = await facilities.FacilityAllAsync(cancellationToken);
        if (all is null)
            throw new InvalidDataException("Facilities returned an empty facility list.");

        var byId = new Dictionary<Guid, string>();
        foreach (var facility in all)
        {
            if (facility is null || facility.Id == Guid.Empty || string.IsNullOrWhiteSpace(facility.Name) ||
                !byId.TryAdd(facility.Id, facility.Name))
                throw new InvalidDataException("Facilities returned an invalid facility list.");
        }

        if (requiredIds.Any(id => !byId.ContainsKey(id)))
            throw new InvalidDataException("A Hall of Fame result references an unavailable facility.");
        return byId;
    }

    private static HallOfFameEntryDTO MapEntry(
        HallOfFameProfileResult row,
        IReadOnlyList<HallOfFameColumn> columns,
        IReadOnlyDictionary<Guid, string> facilityNames)
    {
        bool Includes(HallOfFameColumn column) => columns.Contains(column);
        double? speed = Includes(HallOfFameColumn.SpeedOfBestResult) ? row.BestSpeed : null;
        if (speed.HasValue && !double.IsFinite(speed.Value))
            throw new InvalidDataException("A Hall of Fame result has a non-finite speed.");

        return new HallOfFameEntryDTO(
            checked((int)row.Rank),
            row.ProfileName,
            Includes(HallOfFameColumn.BestTime) ? row.BestDuration : null,
            speed,
            Includes(HallOfFameColumn.PassingCount) ? row.PassingCount : null,
            Includes(HallOfFameColumn.FacilityOfBestResult) ? facilityNames[row.BestFacilityId] : null,
            Includes(HallOfFameColumn.DateOfBestResult)
                ? new DateTimeOffset(DateTime.SpecifyKind(row.BestTimeEnd, DateTimeKind.Utc))
                : null);
    }

    private static async Task<Results<Ok<HallOfFameSettingsDTO>, ProblemHttpResult>> GetSettings(
        HttpContext http,
        HallOfFameSettingsStore settingsStore,
        ILogger<HallOfFameSettingsDTO> logger,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var settings = await settingsStore.GetAsync(cancellationToken);
        if (GetSettingsErrors(settings).Count == 0)
            return TypedResults.Ok(settings);

        logger.LogError("Persisted Hall of Fame settings are structurally invalid at version {Version}.", settings.Version);
        return TypedResults.Problem(
            detail: "Persisted Hall of Fame settings are invalid. An administrator must review the settings.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    private static async Task<Results<Ok<HallOfFameSettingsDTO>, ValidationProblem, ProblemHttpResult>> ReplaceSettings(
        HttpContext http,
        SaveHallOfFameSettingsRequest request,
        StatisticsDbContext storage,
        HallOfFameSettingsStore settingsStore,
        ILogger<HallOfFameSettingsDTO> logger,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var errors = HallOfFameQuery.Validate(request);
        if (errors.Count != 0)
            return TypedResults.ValidationProblem(errors);

        await using var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
        var current = await settingsStore.GetForUpdateAsync(cancellationToken);
        if (current.Version != request.ExpectedVersion)
            return SettingsConflict();

        var itemIds = request.Sections.Select(section => section.StatisticsItemId).Distinct().ToArray();
        var knownIds = await storage.Set<StatisticsItem>().AsNoTracking()
            .Where(item => itemIds.Contains(item.Id))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        var known = knownIds.ToHashSet();
        for (var index = 0; index < request.Sections.Count; index++)
        {
            if (!known.Contains(request.Sections[index].StatisticsItemId))
                errors[$"sections[{index}].statisticsItemId"] = ["Select an existing statistics item."];
        }

        if (errors.Count != 0)
            return TypedResults.ValidationProblem(errors);

        var saved = await settingsStore.TryReplaceAsync(request, cancellationToken);
        if (saved is null)
            return SettingsConflict();

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok(saved);
    }

    private static ProblemHttpResult SettingsConflict() =>
        TypedResults.Problem(
            detail: "Hall of Fame settings changed since they were loaded. Reload the latest settings before saving again.",
            statusCode: StatusCodes.Status409Conflict);

    private static Dictionary<string, string[]> GetSettingsErrors(HallOfFameSettingsDTO settings) =>
        HallOfFameQuery.Validate(new SaveHallOfFameSettingsRequest(settings.Version, settings.Sections));

    private static bool IsFacilityFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is FacilitiesApiException or HttpRequestException or JsonException or InvalidDataException ||
        exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;
}
