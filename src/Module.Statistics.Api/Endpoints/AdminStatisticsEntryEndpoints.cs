using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Service.Recompute;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Endpoints;

internal static class AdminStatisticsEntryEndpoints
{
    internal static void MapAdminStatisticsEntryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var entries = endpoints.MapGroup("/admin/entries")
            .RequireAuthorization(AdministrationAuthorization.StatisticsPolicy)
            .WithTags(["StatisticsEntries"]);

        entries.MapGet("", List)
            .WithName("ListAdminStatisticsEntries")
            .WithSummary("List recorded Statistics entries with keyset pagination")
            .Produces<AdminStatisticsEntriesPageDTO>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
        entries.MapGet("{id:guid}/samples", GetSamples)
            .WithName("GetAdminStatisticsEntrySamples")
            .WithSummary("Get up to 500 Statistics samples contained in an entry window")
            .WithDescription("Uses the entry transponder and inclusive UTC bounds. Orders by TimeStart, TimeEnd, then Id; Truncated indicates additional matches. Quantities use SI units. Responses are no-store. A nonfinite stored distance returns 409.")
            .Produces<AdminStatisticsEntrySamplesDTO>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        entries.MapPut("{id:guid}/manual-invalidation", SetManualInvalidation)
            .WithName("SetStatisticsEntryManualInvalidation")
            .WithSummary("Mark a Statistics entry manually invalid or restore automatic validity")
            .Produces<AdminStatisticsEntryDTO>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Results<Ok<AdminStatisticsEntrySamplesDTO>, NotFound, ProblemHttpResult>> GetSamples(
        Guid id, HttpContext http, StatisticsDbContext storage, ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var entry = await storage.Set<StatisticsEntry>().AsNoTracking()
            .Where(value => value.Id == id)
            .Select(value => new { value.Id, value.TransponderId, value.TimeStart, value.TimeEnd })
            .SingleOrDefaultAsync(cancellationToken);
        if (entry is null) return TypedResults.NotFound();

        const int limit = 500;
        var samples = await storage.Set<Sample>().AsNoTracking()
            .Where(sample => sample.TransponderId == entry.TransponderId &&
                sample.TimeStart >= entry.TimeStart && sample.TimeEnd <= entry.TimeEnd)
            .OrderBy(sample => sample.TimeStart).ThenBy(sample => sample.TimeEnd).ThenBy(sample => sample.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var truncated = samples.Count > limit;
        if (truncated) samples.RemoveAt(limit);
        if (samples.Any(sample => !double.IsFinite(sample.Distance)))
        {
            loggerFactory.CreateLogger("StatisticsEntrySamples").LogWarning(
                "Statistics entry {EntryId} has a sample with nonfinite distance in its window.", id);
            return TypedResults.Problem(
                detail: "A sample has an invalid stored distance and must be corrected before these samples can be loaded.",
                statusCode: StatusCodes.Status409Conflict);
        }
        return TypedResults.Ok(new AdminStatisticsEntrySamplesDTO(entry.Id, entry.TransponderId,
            Utc(entry.TimeStart), Utc(entry.TimeEnd), samples.Select(sample => new AdminStatisticsSampleDTO(
                sample.Id, Utc(sample.TimeStart), Utc(sample.TimeEnd), sample.CoursePointStartId,
                sample.CoursePointEndId, sample.Distance, sample.Duration,
                double.IsFinite(sample.Speed) ? sample.Speed : null, sample.FacilityId)).ToList(), truncated));
    }

    // Raw single-valued query parsing ensures API and BFF reject the same malformed values.
    // Named parameters preserve OpenAPI/NSwag query discovery; the shared parser is authoritative.
    private static async Task<Results<Ok<AdminStatisticsEntriesPageDTO>, ValidationProblem>> List(
        HttpContext http, StatisticsDbContext storage, CancellationToken cancellationToken,
        Guid? facilityId = null, Guid? statisticsItemId = null, Guid? statisticsItemConfigId = null,
        Guid? transponderId = null, bool? isValid = null, string? cursor = null,
        string? direction = null, int pageSize = 50,
        string? minDuration = null, string? maxDuration = null,
        string? minSpeed = null, string? maxSpeed = null, string? sort = null, string? order = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        var duplicates = http.Request.Query.Where(pair => pair.Value.Count != 1)
            .ToDictionary(pair => pair.Key, _ => new[] { "Supply this query parameter exactly once." });
        if (duplicates.Count != 0) return TypedResults.ValidationProblem(duplicates);
        var values = http.Request.Query.ToDictionary(pair => pair.Key, pair => (string?)pair.Value[0]);
        if (!AdminStatisticsEntriesQuery.TryParse(values, out var request, out var errors))
            return TypedResults.ValidationProblem(errors);
        return TypedResults.Ok(await QueryPage(storage, request, cancellationToken));
    }

    private static async Task<AdminStatisticsEntriesPageDTO> QueryPage(
        StatisticsDbContext storage, GetAdminStatisticsEntriesRequest request, CancellationToken cancellationToken)
    {
        var filtered = storage.Set<StatisticsEntry>().AsNoTracking().AsQueryable();
        if (request.FacilityId is { } facilityId) filtered = filtered.Where(entry => entry.FacilityId == facilityId);
        if (request.StatisticsItemId is { } itemId) filtered = filtered.Where(entry => entry.StatisticsItemId == itemId);
        if (request.StatisticsItemConfigId is { } configId) filtered = filtered.Where(entry => entry.StatisticsItemConfigId == configId);
        if (request.TransponderId is { } transponderId) filtered = filtered.Where(entry => entry.TransponderId == transponderId);
        if (request.IsValid is { } isValid) filtered = filtered.Where(entry => entry.IsValid == isValid);

        if (request.MinDuration is { } minDuration) filtered = DurationBound(filtered, minDuration, minimum: true);
        if (request.MaxDuration is { } maxDuration) filtered = DurationBound(filtered, maxDuration, minimum: false);
        // PostgreSQL considers NaN greater than all other doubles, and NaN = NaN. Explicitly
        // exclude all non-finite rows whenever either speed bound is present.
        if (request.MinSpeedMetresPerSecond.HasValue || request.MaxSpeedMetresPerSecond.HasValue)
            filtered = filtered.Where(entry => entry.Speed > double.NegativeInfinity && entry.Speed < double.PositiveInfinity);
        if (request.MinSpeedMetresPerSecond is { } minSpeed) filtered = filtered.Where(entry => entry.Speed >= minSpeed);
        if (request.MaxSpeedMetresPerSecond is { } maxSpeed) filtered = filtered.Where(entry => entry.Speed <= maxSpeed);

        var previousDirection = request.Direction == AdminStatisticsEntriesPageDirection.Previous;
        var descending = request.Order == AdminStatisticsEntriesSortOrder.Descending;
        var pageQuery = request.Cursor is { } anchor ? Boundary(filtered, anchor, previousDirection == descending) : filtered;
        var ordered = Order(pageQuery, request.Sort, descending != previousDirection);
        var rows = await ordered.Take(request.PageSize + 1)
            .Select(entry => new EntryPageRow(entry.Id, entry.TimeStart, entry.TimeEnd,
                entry.StatisticsItemId, entry.StatisticsItem.Name, entry.StatisticsItemConfigId,
                entry.StatisticsItemConfig is MultiStatisticsItemConfig,
                entry.FacilityId, entry.TransponderId, entry.StatsProfileUserId,
                entry.StatsProfile == null ? null : entry.StatsProfile.ProfileName,
                entry.Duration, entry.Speed, entry.IsValid, entry.InvalidReason,
                entry.InvalidReason == StatisticsEntryInvalidReason.ManuallyInvalidated,
                entry.ManualInvalidationChangedByUserId, entry.ManualInvalidationChangedAtUtc,
                entry.ManualInvalidationNote))
            .ToListAsync(cancellationToken);
        var lookahead = rows.Count > request.PageSize;
        if (lookahead) rows.RemoveAt(request.PageSize);
        if (previousDirection) rows.Reverse();

        if (rows.Count == 0)
        {
            if (request.Cursor is not null)
                return await QueryPage(storage, request with { Cursor = null, Direction = null }, cancellationToken);
            return new([], null, null);
        }

        var first = Cursor(rows[0], request);
        var last = Cursor(rows[^1], request);
        var previous = previousDirection && lookahead || await Boundary(filtered, first, descending).AnyAsync(cancellationToken);
        var next = !previousDirection && lookahead || await Boundary(filtered, last, !descending).AnyAsync(cancellationToken);
        return new(rows.Select(ToDto).ToList(), previous ? first : null, next ? last : null);
    }

    private static IQueryable<StatisticsEntry> DurationBound(IQueryable<StatisticsEntry> query, TimeSpan bound, bool minimum)
    {
        // PostgreSQL intervals have microsecond precision. Round inward so binding a sub-
        // microsecond bound cannot truncate it and accidentally include an out-of-range row.
        var ticks = (minimum ? decimal.Ceiling(bound.Ticks / 10m) : decimal.Floor(bound.Ticks / 10m)) * 10;
        if (ticks > long.MaxValue || ticks < long.MinValue) return query.Where(_ => false);
        var aligned = TimeSpan.FromTicks((long)ticks);
        return minimum ? query.Where(entry => entry.Duration >= aligned) : query.Where(entry => entry.Duration <= aligned);
    }

    private static IOrderedQueryable<StatisticsEntry> Order(IQueryable<StatisticsEntry> query,
        AdminStatisticsEntriesSort sort, bool descending) => (sort, descending) switch
    {
        (AdminStatisticsEntriesSort.Duration, true) => query.OrderByDescending(entry => entry.Duration).ThenByDescending(entry => entry.Id),
        (AdminStatisticsEntriesSort.Duration, false) => query.OrderBy(entry => entry.Duration).ThenBy(entry => entry.Id),
        (AdminStatisticsEntriesSort.Speed, true) => query.OrderByDescending(entry => entry.Speed).ThenByDescending(entry => entry.Id),
        (AdminStatisticsEntriesSort.Speed, false) => query.OrderBy(entry => entry.Speed).ThenBy(entry => entry.Id),
        (_, true) => query.OrderByDescending(entry => entry.TimeEnd).ThenByDescending(entry => entry.Id),
        _ => query.OrderBy(entry => entry.TimeEnd).ThenBy(entry => entry.Id)
    };

    private static IQueryable<StatisticsEntry> Boundary(IQueryable<StatisticsEntry> query,
        AdminStatisticsEntryCursorDTO cursor, bool greater)
    {
        if (cursor.Sort == AdminStatisticsEntriesSort.Duration)
        {
            var duration = cursor.Duration!.Value;
            return greater
                ? query.Where(entry => EF.Functions.GreaterThan(
                    ValueTuple.Create(entry.Duration, entry.Id), ValueTuple.Create(duration, cursor.Id)))
                : query.Where(entry => EF.Functions.LessThan(
                    ValueTuple.Create(entry.Duration, entry.Id), ValueTuple.Create(duration, cursor.Id)));
        }
        if (cursor.Sort == AdminStatisticsEntriesSort.Speed)
        {
            var speed = double.Parse(cursor.Speed!, CultureInfo.InvariantCulture);
            // Row comparisons run in PostgreSQL, including its native NaN equality/ordering.
            return greater
                ? query.Where(entry => EF.Functions.GreaterThan(
                    ValueTuple.Create(entry.Speed, entry.Id), ValueTuple.Create(speed, cursor.Id)))
                : query.Where(entry => EF.Functions.LessThan(
                    ValueTuple.Create(entry.Speed, entry.Id), ValueTuple.Create(speed, cursor.Id)));
        }
        var timeEnd = cursor.TimeEnd.UtcDateTime;
        return greater
            ? query.Where(entry => EF.Functions.GreaterThan(
                ValueTuple.Create(entry.TimeEnd, entry.Id), ValueTuple.Create(timeEnd, cursor.Id)))
            : query.Where(entry => EF.Functions.LessThan(
                ValueTuple.Create(entry.TimeEnd, entry.Id), ValueTuple.Create(timeEnd, cursor.Id)));
    }

    private static AdminStatisticsEntryCursorDTO Cursor(EntryPageRow row, GetAdminStatisticsEntriesRequest request) =>
        new(request.Sort == AdminStatisticsEntriesSort.TimeEnd ? Utc(row.TimeEnd) : default, row.Id,
            request.Sort, request.Order,
            request.Sort == AdminStatisticsEntriesSort.Duration ? row.Duration : null,
            request.Sort == AdminStatisticsEntriesSort.Speed ? row.Speed.ToString("R", CultureInfo.InvariantCulture) : null);

    private static AdminStatisticsEntryDTO ToDto(EntryPageRow row) => new(
        row.Id, Utc(row.TimeStart), Utc(row.TimeEnd), row.StatisticsItemId, row.StatisticsItemName,
        row.StatisticsItemConfigId,
        row.IsMulti ? StatisticsConfigurationType.Multi : StatisticsConfigurationType.Simple,
        row.FacilityId, row.TransponderId, row.StatsProfileUserId, row.ProfileName, row.Duration,
        double.IsFinite(row.Speed) ? row.Speed : null, row.IsValid, row.InvalidReason,
        row.IsManuallyInvalidated, row.ManualInvalidationChangedByUserId,
        row.ManualInvalidationChangedAtUtc, row.ManualInvalidationNote);

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static async Task<Results<Ok<AdminStatisticsEntryDTO>, NotFound, ValidationProblem, ForbidHttpResult, ProblemHttpResult>> SetManualInvalidation(
        Guid id, StatisticsEntryManualInvalidationRequest request, ClaimsPrincipal user, HttpContext http,
        StatisticsDbContext storage, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (request is null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["A request body is required."] });
        if (request.Note is { Length: > StatisticsEntryManualInvalidationRequest.MaximumNoteLength })
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["note"] = [$"Note must be at most {StatisticsEntryManualInvalidationRequest.MaximumNoteLength} characters."]
            });
        if (!Guid.TryParse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId) || actorId == Guid.Empty)
            return TypedResults.Forbid();

        return await StatisticsDefinitionTransaction.RunAsync<Results<Ok<AdminStatisticsEntryDTO>, NotFound, ValidationProblem, ForbidHttpResult, ProblemHttpResult>>(
            storage, async () =>
            {
                var target = await storage.Set<StatisticsEntry>().AsNoTracking()
                    .Where(entry => entry.Id == id)
                    .Select(entry => new { entry.StatisticsItemId, entry.StatisticsItemConfigId, entry.TransponderId })
                    .SingleOrDefaultAsync(cancellationToken);
                if (target is null) return TypedResults.NotFound();

                // The recompute worker is the other writer of this transponder's entries; serialize with it.
                await StatisticsLocks.LockTransponderAsync(storage, target.TransponderId, cancellationToken);

                var entry = await storage.Set<StatisticsEntry>()
                    .Include(value => value.StatisticsItem)
                    .Include(value => value.StatisticsItemConfig)
                    .Include(value => value.StatsProfile)
                    .SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
                if (entry is null) return TypedResults.NotFound();
                if (entry.StatisticsItemId != target.StatisticsItemId || entry.StatisticsItemConfigId != target.StatisticsItemConfigId)
                    throw new InvalidOperationException($"Statistics entry {id} changed identity while acquiring its transponder lock.");

                if (!request.IsManuallyInvalidated &&
                    entry.InvalidReason != StatisticsEntryInvalidReason.ManuallyInvalidated)
                    return TypedResults.Problem(
                        detail: "Only a manually invalidated entry can be restored.",
                        statusCode: StatusCodes.Status409Conflict);

                var wasValid = entry.IsValid;
                if (request.IsManuallyInvalidated)
                {
                    entry.IsValid = false;
                    entry.InvalidReason = StatisticsEntryInvalidReason.ManuallyInvalidated;
                }
                else
                {
                    var contributorsValid = true;
                    if (entry.StatisticsItemConfig is MultiStatisticsItemConfig)
                    {
                        var contributorValidity = await StatisticsService.ReadMultiContributorValidityAsync(
                            storage, [entry.Id], cancellationToken);
                        contributorsValid = contributorValidity[entry.Id];
                    }
                    // Bounds of the configuration version the entry was derived with (dated limits, plan §6.1).
                    var version = await storage.Set<StatisticsConfigVersion>().AsNoTracking()
                        .SingleOrDefaultAsync(value => value.Id == entry.StatisticsConfigVersionId, cancellationToken);
                    var bounds = version?.Bounds() ?? StatisticsDurationBounds.Effective(entry.StatisticsItem, entry.StatisticsItemConfig);
                    var reason = StatisticsValidityEvaluator.Evaluate(entry.Duration, contributorsValid, bounds,
                        entry.RequiredCoursePointsSatisfied);
                    StatisticsValidityEvaluator.Apply(entry, reason, allowManualInvalidationOverride: true);
                }

                entry.ManualInvalidationChangedByUserId = actorId;
                entry.ManualInvalidationChangedAtUtc = DateTimeOffset.UtcNow;
                entry.ManualInvalidationNote = request.Note;
                // Multi entries built on this one re-derive their contributor validity in the recompute worker.
                if (wasValid != entry.IsValid && entry.StatisticsItemConfig is SimpleStatisticsItemConfig)
                    StatisticsRecomputeScheduler.Transponder(storage, entry.TransponderId, entry.TimeStart, entry.TimeEnd,
                        RecomputeReason.Manual, entry.Id);
                await storage.SaveChangesAsync(cancellationToken);

                await storage.Database.CurrentTransaction!.CommitAsync(cancellationToken);
                return TypedResults.Ok(ToDto(entry));
            }, cancellationToken);
    }

    private static AdminStatisticsEntryDTO ToDto(StatisticsEntry entry) => new(
        entry.Id, Utc(entry.TimeStart), Utc(entry.TimeEnd), entry.StatisticsItemId, entry.StatisticsItem.Name,
        entry.StatisticsItemConfigId,
        entry.StatisticsItemConfig is MultiStatisticsItemConfig ? StatisticsConfigurationType.Multi : StatisticsConfigurationType.Simple,
        entry.FacilityId, entry.TransponderId, entry.StatsProfileUserId, entry.StatsProfile?.ProfileName,
        entry.Duration, double.IsFinite(entry.Speed) ? entry.Speed : null, entry.IsValid, entry.InvalidReason,
        entry.InvalidReason == StatisticsEntryInvalidReason.ManuallyInvalidated,
        entry.ManualInvalidationChangedByUserId, entry.ManualInvalidationChangedAtUtc, entry.ManualInvalidationNote);

    private sealed record EntryPageRow(
        Guid Id, DateTime TimeStart, DateTime TimeEnd, Guid StatisticsItemId, string StatisticsItemName,
        Guid StatisticsItemConfigId, bool IsMulti, Guid FacilityId, Guid TransponderId,
        Guid? StatsProfileUserId, string? ProfileName, TimeSpan Duration, double Speed, bool IsValid,
        StatisticsEntryInvalidReason? InvalidReason, bool IsManuallyInvalidated,
        Guid? ManualInvalidationChangedByUserId, DateTimeOffset? ManualInvalidationChangedAtUtc,
        string? ManualInvalidationNote);
}
