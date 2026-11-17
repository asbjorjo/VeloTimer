using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Endpoints;

internal static class AdminStatisticsEntryEndpoints
{
    private static readonly HashSet<string> QueryKeys =
        ["facilityId", "statisticsItemId", "statisticsItemConfigId", "transponderId", "isValid", "cursor", "direction", "pageSize"];

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

    // Cursor uses the invariant UTC round-trip timestamp|UUID encoding used by public Statistics paging.
    // Raw single-valued query parsing ensures malformed and duplicate values are explicitly rejected.
    private static async Task<Results<Ok<AdminStatisticsEntriesPageDTO>, ValidationProblem>> List(
        HttpContext http, StatisticsDbContext storage, CancellationToken cancellationToken,
        Guid? facilityId = null, Guid? statisticsItemId = null, Guid? statisticsItemConfigId = null,
        Guid? transponderId = null, bool? isValid = null, string? cursor = null,
        string? direction = null, int pageSize = 50)
    {
        http.Response.Headers.CacheControl = "no-store";
        var errors = new Dictionary<string, string[]>();
        var values = new Dictionary<string, string?>();
        foreach (var parameter in http.Request.Query)
        {
            if (!QueryKeys.Contains(parameter.Key))
                errors[parameter.Key] = ["Unsupported Statistics entry query parameter."];
            if (parameter.Value.Count != 1)
                errors[parameter.Key] = ["Supply this query parameter exactly once."];
            else
                values[parameter.Key] = parameter.Value[0];
        }
        if (errors.Count != 0) return TypedResults.ValidationProblem(errors);

        string? Value(string key) => values.GetValueOrDefault(key);
        Guid? ParseId(string key)
        {
            if (!values.ContainsKey(key)) return null;
            if (Guid.TryParseExact(Value(key), "D", out var id) && id != Guid.Empty) return id;
            errors[key] = ["A nonempty ID in UUID D format is required."];
            return null;
        }

        _ = ParseId("facilityId");
        _ = ParseId("statisticsItemId");
        _ = ParseId("statisticsItemConfigId");
        _ = ParseId("transponderId");
        var facility = facilityId;
        var item = statisticsItemId;
        var config = statisticsItemConfigId;
        var transponder = transponderId;
        var valid = isValid;

        AdminStatisticsEntryCursorDTO? parsedCursor = null;
        if (values.ContainsKey("cursor"))
        {
            if (PublicStatisticsQuery.TryParseCursor(Value("cursor"), out var publicCursor))
                parsedCursor = new(publicCursor!.TimeEnd, publicCursor.Id);
            else errors["cursor"] = ["Use a UTC round-trip timestamp, |, and an entry UUID."];
        }
        var parsedDirection = Value("direction") switch
        {
            "next" => AdminStatisticsEntriesPageDirection.Next,
            "previous" => AdminStatisticsEntriesPageDirection.Previous,
            _ => (AdminStatisticsEntriesPageDirection?)null
        };
        if (values.ContainsKey("direction") && parsedDirection is null)
            errors["direction"] = ["Direction must be next or previous."];
        if ((parsedCursor is null) != (parsedDirection is null))
            errors["cursor"] = ["Cursor and direction must be supplied together."];

        var requestedPageSize = pageSize;
        if (requestedPageSize is < 1 or > 100)
            errors["pageSize"] = ["Page size must be an integer from 1 through 100."];

        if (errors.Count != 0) return TypedResults.ValidationProblem(errors);
        var request = new GetAdminStatisticsEntriesRequest(facility, item, config, transponder, valid,
            parsedCursor, parsedDirection, requestedPageSize);
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

        var previousDirection = request.Direction == AdminStatisticsEntriesPageDirection.Previous;
        var pageQuery = request.Cursor is { } anchor ? Boundary(filtered, anchor, previousDirection) : filtered;
        var ordered = previousDirection
            ? pageQuery.OrderBy(entry => entry.TimeEnd).ThenBy(entry => entry.Id)
            : pageQuery.OrderByDescending(entry => entry.TimeEnd).ThenByDescending(entry => entry.Id);
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
            if (request.Cursor is not { } emptyAnchor) return new([], null, null);
            var hasPrevious = await Boundary(filtered, emptyAnchor, true).AnyAsync(cancellationToken);
            var hasNext = await Boundary(filtered, emptyAnchor, false).AnyAsync(cancellationToken);
            return new([], hasPrevious ? emptyAnchor : null, hasNext ? emptyAnchor : null);
        }

        var first = Cursor(rows[0]);
        var last = Cursor(rows[^1]);
        var previous = previousDirection && lookahead || await Boundary(filtered, first, true).AnyAsync(cancellationToken);
        var next = !previousDirection && lookahead || await Boundary(filtered, last, false).AnyAsync(cancellationToken);
        return new(rows.Select(ToDto).ToList(), previous ? first : null, next ? last : null);
    }

    private static IQueryable<StatisticsEntry> Boundary(IQueryable<StatisticsEntry> query,
        AdminStatisticsEntryCursorDTO cursor, bool greater)
    {
        var timeEnd = cursor.TimeEnd.UtcDateTime;
        return greater
            ? query.Where(entry => EF.Functions.GreaterThan(
                ValueTuple.Create(entry.TimeEnd, entry.Id), ValueTuple.Create(timeEnd, cursor.Id)))
            : query.Where(entry => EF.Functions.LessThan(
                ValueTuple.Create(entry.TimeEnd, entry.Id), ValueTuple.Create(timeEnd, cursor.Id)));
    }

    private static AdminStatisticsEntryCursorDTO Cursor(EntryPageRow row) => new(Utc(row.TimeEnd), row.Id);

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
        StatisticsDbContext storage, StatisticsValidityScheduler scheduler, CancellationToken cancellationToken)
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
                    .Select(entry => new { entry.StatisticsItemId, entry.StatisticsItemConfigId })
                    .SingleOrDefaultAsync(cancellationToken);
                if (target is null) return TypedResults.NotFound();

                // A Multi classification depends on its parent Simple item. Lock both owners and all
                // dependent Multi owners together, in UUID order, before evaluating or writing validity.
                var parentItemId = await storage.Set<MultiStatisticsItemConfig>().AsNoTracking()
                    .Where(config => config.Id == target.StatisticsItemConfigId)
                    .Select(config => (Guid?)config.ParentConfig.StatisticsItemId)
                    .SingleOrDefaultAsync(cancellationToken);
                var lockItems = parentItemId.HasValue
                    ? new[] { target.StatisticsItemId, parentItemId.Value }
                    : [target.StatisticsItemId];
                await scheduler.LockThresholdScopeAsync(lockItems, true, cancellationToken);

                var entry = await storage.Set<StatisticsEntry>()
                    .Include(value => value.StatisticsItem)
                    .Include(value => value.StatisticsItemConfig)
                    .Include(value => value.StatsProfile)
                    .SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
                if (entry is null) return TypedResults.NotFound();
                if (entry.StatisticsItemId != target.StatisticsItemId || entry.StatisticsItemConfigId != target.StatisticsItemConfigId)
                    throw new InvalidOperationException($"Statistics entry {id} changed identity while acquiring its item lock.");

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
                    var reason = StatisticsValidityEvaluator.Evaluate(entry.Duration, contributorsValid,
                        StatisticsDurationBounds.Effective(entry.StatisticsItem, entry.StatisticsItemConfig),
                        entry.RequiredCoursePointsSatisfied);
                    StatisticsValidityEvaluator.Apply(entry, reason, allowManualInvalidationOverride: true);
                }

                entry.ManualInvalidationChangedByUserId = actorId;
                entry.ManualInvalidationChangedAtUtc = DateTimeOffset.UtcNow;
                entry.ManualInvalidationNote = request.Note;
                await storage.SaveChangesAsync(cancellationToken);

                if (wasValid != entry.IsValid && entry.StatisticsItemConfig is SimpleStatisticsItemConfig)
                {
                    var dependents = await scheduler.GetDependentMultiItemIdsByParentConfigAsync(
                        [entry.StatisticsItemConfigId], cancellationToken);
                    await scheduler.ScheduleDependentMultiRevalidationAsync(dependents, cancellationToken);
                }

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
