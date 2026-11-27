using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Service.Recompute;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Endpoints;

internal static class StatisticsDefinitionEndpoints
{
    internal static void MapStatisticsDefinitionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var definitions = endpoints.MapGroup("/admin").RequireAuthorization(AdministrationAuthorization.StatisticsPolicy);
        definitions.MapGet("/items", ListItems).WithName("ListStatisticsItems");
        definitions.MapPost("/items", CreateItem).WithName("CreateStatisticsItem");
        definitions.MapPut("/items/{id:guid}", UpdateItem).WithName("UpdateStatisticsItem");
        definitions.MapDelete("/items/{id:guid}", DeleteItem).WithName("DeleteStatisticsItem");
        definitions.MapGet("/configurations", ListConfigurations).WithName("ListStatisticsConfigurations");
        definitions.MapPost("/configurations", CreateConfiguration).WithName("CreateStatisticsConfiguration");
        definitions.MapPut("/configurations/{id:guid}", UpdateConfiguration).WithName("UpdateStatisticsConfiguration");
        definitions.MapDelete("/configurations/{id:guid}", DeleteConfiguration).WithName("DeleteStatisticsConfiguration");
        definitions.MapGet("/configurations/{id:guid}/versions", ListConfigurationVersions).WithName("ListStatisticsConfigurationVersions");
    }

    private static async Task<Ok<List<StatisticsItemDTO>>> ListItems(StatisticsDbContext storage, CancellationToken cancellationToken) =>
        TypedResults.Ok(await storage.Set<StatisticsItem>().AsNoTracking().OrderBy(item => item.Name)
            .Select(item => new StatisticsItemDTO(item.Id, item.Name, item.Description, item.Distance, item.MinDuration, item.MaxDuration) { DistanceMode = item.DistanceMode }).ToListAsync(cancellationToken));

    private static string? ValidateItem(SaveStatisticsItemRequest request, StatisticsDistanceMode mode) =>
        string.IsNullOrWhiteSpace(request.Name) ? "Name is required." :
        mode is not (StatisticsDistanceMode.Fixed or StatisticsDistanceMode.CourseLayout) ? "Unknown distance mode." :
        mode == StatisticsDistanceMode.Fixed && (!float.IsFinite(request.Distance) || request.Distance <= 0) ? "Distance must be a finite positive number of metres." :
        mode == StatisticsDistanceMode.CourseLayout && request.Distance != 0 ? "CourseLayout distance must be zero." :
        ValidateBounds(request.MinDuration, request.MaxDuration);

    private static string? ValidateBounds(TimeSpan? minimum, TimeSpan? maximum)
    {
        foreach (var duration in new[] { minimum, maximum })
        {
            if (duration is null) continue;
            if (StatisticsValidityEvaluator.Evaluate(duration.Value, true, StatisticsDurationBounds.Unbounded) is not null)
                return "Duration bounds must be positive.";
            if (duration.Value.Ticks % 10 != 0) return "Duration bounds must use whole microseconds.";
        }
        return new StatisticsDurationBounds(minimum, maximum).IsDisjoint
            ? "Minimum duration cannot exceed maximum duration." : null;
    }

    private static async Task<Results<Created<StatisticsItemDTO>, ProblemHttpResult>> CreateItem(
        SaveStatisticsItemRequest request, StatisticsDbContext storage, CancellationToken cancellationToken)
        => await StatisticsDefinitionTransaction.RunAsync<Results<Created<StatisticsItemDTO>, ProblemHttpResult>>(storage, async () =>
    {
        if (ValidateItem(request, request.DistanceMode ?? StatisticsDistanceMode.Fixed) is { } error)
        {
            return Invalid(error);
        }

        var item = new StatisticsItem
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description ?? "",
            Distance = request.Distance,
            DistanceMode = request.DistanceMode ?? StatisticsDistanceMode.Fixed,
            MinDuration = request.MinDuration,
            MaxDuration = request.MaxDuration
        };
        storage.Add(item);
        await storage.SaveChangesAsync(cancellationToken);
        await storage.Database.CurrentTransaction!.CommitAsync(cancellationToken);
        return TypedResults.Created($"/api/statistics/admin/items/{item.Id}", ToDto(item));
    }, cancellationToken);

    private static async Task<Results<Ok<StatisticsItemDTO>, NotFound, ProblemHttpResult>> UpdateItem(
        Guid id, SaveStatisticsItemRequest request, StatisticsDbContext storage, HybridCache cache, ICacheInvalidationOperations invalidation,
        ILogger<StatisticsDbContext> logger, CancellationToken cancellationToken)
    {
        var cacheKeys = Array.Empty<string>();
        var result = await StatisticsDefinitionTransaction.RunAsync<Results<Ok<StatisticsItemDTO>, NotFound, ProblemHttpResult>>(storage, async () =>
        {
            await LockItemAsync(storage, id, cancellationToken);
            var item = await storage.Set<StatisticsItem>().FindAsync([id], cancellationToken);
            if (item is null) return TypedResults.NotFound();
            var mode = request.DistanceMode ?? item.DistanceMode;
            if (ValidateItem(request, mode) is { } error) return Invalid(error);
            var configs = await storage.Set<StatisticsItemConfig>()
                .Where(config => config.StatisticsItemId == id).ToListAsync(cancellationToken);
            if (configs.Any(config => StatisticsDurationBounds.Effective(request.MinDuration, request.MaxDuration,
                config.MinDuration, config.MaxDuration).IsDisjoint))
                return Invalid("Global bounds are disjoint from an existing configuration's effective range.");
            if (mode == StatisticsDistanceMode.CourseLayout)
            {
                if (configs.Any(config => config is not SimpleStatisticsItemConfig) ||
                    configs.OfType<SimpleStatisticsItemConfig>().Any(config => config.CoursePointStart == Guid.Empty || config.CoursePointStart != config.CoursePointEnd) ||
                    configs.OfType<SimpleStatisticsItemConfig>().GroupBy(config => config.CoursePointStart).Any(group => group.Count() > 1))
                    return Invalid("CourseLayout requires unique Simple loop configurations.");
                var simpleIds = configs.OfType<SimpleStatisticsItemConfig>().Select(config => config.Id).ToArray();
                if (await storage.Set<MultiStatisticsItemConfig>().AnyAsync(config => simpleIds.Contains(config.ParentConfigId), cancellationToken))
                    return Conflict("CourseLayout configurations cannot have Multi dependents.");
            }

            // Result-affecting values are dated (plan §4.2, decision §6.1): the edit applies from its effective
            // instant, and the recompute worker rebuilds the results from there.
            var before = StatisticsConfigVersioning.Detach(item);
            item.MinDuration = request.MinDuration;
            item.MaxDuration = request.MaxDuration;
            item.Name = request.Name.Trim();
            item.Description = request.Description ?? "";
            item.Distance = request.Distance;
            item.DistanceMode = mode;
            await StatisticsConfigVersioning.ApplyItemFromAsync(storage, before, item, EffectiveFrom(request.EffectiveFromUtc),
                DateTime.UtcNow, cancellationToken);
            cacheKeys = configs.SelectMany(CacheKeysFor).ToArray();
            await storage.SaveChangesAsync(cancellationToken);
            await storage.Database.CurrentTransaction!.CommitAsync(cancellationToken);
            return TypedResults.Ok(ToDto(item));
        }, cancellationToken);
        if (result.Result is not Ok<StatisticsItemDTO>) return result;
        await StatisticsCacheInvalidation.InvalidateAsync(cache, logger, cacheKeys, [StatisticsCacheTags.ConfigurationDefinitions], invalidation);
        return result;
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteItem(
        Guid id, StatisticsDbContext storage, CancellationToken cancellationToken)
    {
        var item = await storage.Set<StatisticsItem>().FindAsync([id], cancellationToken);
        if (item is null) return TypedResults.NotFound();
        var configs = await storage.Set<StatisticsItemConfig>().CountAsync(config => config.StatisticsItemId == id, cancellationToken);
        var results = await storage.Set<StatisticsEntry>().CountAsync(entry => entry.StatisticsItemId == id, cancellationToken);
        if (configs > 0 || results > 0) return Conflict($"This item is referenced by {configs} configuration(s) and {results} result(s).");
        storage.Remove(item);
        await storage.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<StatisticsConfigurationDTO>>> ListConfigurations(
        StatisticsDbContext storage, Guid? itemId, CancellationToken cancellationToken)
    {
        var query = storage.Set<StatisticsItemConfig>().AsNoTracking();
        if (itemId.HasValue) query = query.Where(config => config.StatisticsItemId == itemId);
        return TypedResults.Ok((await query.OrderBy(config => config.Id).ToListAsync(cancellationToken)).Select(ToDto).ToList());
    }

    private static async Task<string?> ValidateConfiguration(SaveStatisticsConfigurationRequest request,
        StatisticsDbContext storage, ICoursePointReferences points, ICourseLayoutReferences layouts, CancellationToken cancellationToken, Guid? configId = null)
    {
        if (ValidateBounds(request.MinDuration, request.MaxDuration) is { } error) return error;
        var item = await storage.Set<StatisticsItem>().FindAsync([request.StatisticsItemId], cancellationToken);
        if (item is null) return "Select an existing statistics item.";
        if (StatisticsDurationBounds.Effective(item.MinDuration, item.MaxDuration, request.MinDuration, request.MaxDuration).IsDisjoint)
            return "Configuration bounds are disjoint from the global effective range.";
        if (request.Type == StatisticsConfigurationType.Simple)
        {
            if (request.CoursePointStart.GetValueOrDefault() == Guid.Empty ||
                request.CoursePointEnd.GetValueOrDefault() == Guid.Empty) return "Start and end coursepoints are required.";
            if (request.ParentConfigId is not null || request.Repetitions is not null) return "Simple configurations cannot have a parent or repetitions.";
            if (item.DistanceMode == StatisticsDistanceMode.CourseLayout)
            {
                if (request.CoursePointStart != request.CoursePointEnd) return "CourseLayout requires a Simple loop with identical start and end.";
                if (await storage.Set<SimpleStatisticsItemConfig>().AnyAsync(config => config.StatisticsItemId == item.Id && config.Id != configId && config.CoursePointStart == request.CoursePointStart && config.CoursePointEnd == request.CoursePointEnd, cancellationToken))
                    return "A CourseLayout loop configuration already exists for this item and coursepoint.";
                if (configId is { } id && await storage.Set<MultiStatisticsItemConfig>().AnyAsync(config => config.ParentConfigId == id, cancellationToken))
                    return "CourseLayout configurations cannot have Multi dependents.";
            }
            var startFacility = await points.GetFacilityAsync(request.CoursePointStart!.Value, cancellationToken);
            var endFacility = request.CoursePointStart == request.CoursePointEnd ? startFacility : await points.GetFacilityAsync(request.CoursePointEnd!.Value, cancellationToken);
            if (startFacility is null || endFacility is null || startFacility != endFacility) return "Both coursepoints must exist in the same facility.";
            if (request.RequiredCoursePoints is { } requirements)
            {
                if (StatisticsRequiredPointsValidation.ValidateShape(requirements, request.CoursePointStart.Value, request.CoursePointEnd.Value) is { } shapeError)
                    return shapeError;
                if (requirements.CoursePointIds!.Count > 0)
                {
                    var layout = await layouts.GetLayoutAsync(requirements.CourseLayoutId!.Value, cancellationToken);
                    if (layout is null) return "Select an existing layout for required coursepoints.";
                    if (StatisticsRequiredPointsValidation.ValidatePath(layout, startFacility.Value, request.CoursePointStart.Value,
                        request.CoursePointEnd.Value, requirements.CoursePointIds) is { } pathError) return pathError;
                    // Optional observations may be any point along this itinerary, not only a
                    // configured checkpoint. Facilities selects layouts by both endpoints and
                    // rejects anything other than one; probing each point against itself detects
                    // any competing layout. Since ValidatePath checked full-layout continuity,
                    // every pair of itinerary points then resolves in this same unique layout.
                    var itineraryPoints = StatisticsRequiredPointsValidation.GetItineraryPoints(layout,
                        request.CoursePointStart.Value, request.CoursePointEnd.Value);
                    foreach (var pointId in itineraryPoints)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!await points.CanResolveDistanceAsync(pointId, pointId, cancellationToken))
                            return "Facilities cannot resolve every possible sample point on the required path. Choose an unambiguous layout.";
                    }
                    foreach (var pointId in requirements.CoursePointIds)
                        if (await points.GetFacilityAsync(pointId, cancellationToken) != startFacility)
                            return "All required coursepoints must exist in the endpoints' facility.";
                }
            }
        }
        else if (request.Type == StatisticsConfigurationType.Multi)
        {
            if (item.DistanceMode == StatisticsDistanceMode.CourseLayout) return "CourseLayout items cannot have Multi configurations.";
            if (request.RequiredCoursePoints is not null) return "Multi configurations inherit requirements from their Simple parent.";
            if (request.CoursePointStart is not null || request.CoursePointEnd is not null) return "Multi configurations inherit their coursepoints from their parent.";
            if (request.Repetitions is null or <= 0) return "Repetitions must be a positive integer.";
            var parent = await storage.Set<SimpleStatisticsItemConfig>().Include(config => config.StatisticsItem).SingleOrDefaultAsync(config => config.Id == request.ParentConfigId, cancellationToken);
            if (parent is null) return "Select an existing Simple configuration as the parent.";
            if (parent.StatisticsItem.DistanceMode == StatisticsDistanceMode.CourseLayout) return "Multi configurations cannot depend on CourseLayout items.";
        }
        else return "Unknown configuration type.";
        return null;
    }

    private static async Task<Results<Created<StatisticsConfigurationDTO>, ProblemHttpResult>> CreateConfiguration(
        SaveStatisticsConfigurationRequest request, StatisticsDbContext storage, ICoursePointReferences points, ICourseLayoutReferences layouts, HybridCache cache,
        ICacheInvalidationOperations invalidation, ILogger<StatisticsDbContext> logger, CancellationToken cancellationToken)
    {
        var cacheKeys = Array.Empty<string>();
        var result = await StatisticsDefinitionTransaction.RunAsync<Results<Created<StatisticsConfigurationDTO>, ProblemHttpResult>>(storage, async () =>
        {
            await LockItemAsync(storage, request.StatisticsItemId, cancellationToken);
            if (await ValidateConfiguration(request, storage, points, layouts, cancellationToken) is { } error) return Invalid(error);
            var item = await storage.Set<StatisticsItem>().SingleAsync(value => value.Id == request.StatisticsItemId, cancellationToken);
            StatisticsItemConfig config = request.Type == StatisticsConfigurationType.Simple
                ? new SimpleStatisticsItemConfig { StatisticsItem = null!, CoursePointStart = request.CoursePointStart!.Value, CoursePointEnd = request.CoursePointEnd!.Value,
                    RequiredCourseLayoutId = request.RequiredCoursePoints?.CourseLayoutId,
                    RequiredCoursePointIds = request.RequiredCoursePoints?.CoursePointIds?.ToArray() ?? [] }
                : new MultiStatisticsItemConfig { StatisticsItem = null!, ParentConfig = null!, ParentConfigId = request.ParentConfigId!.Value, Repetitions = request.Repetitions!.Value };
            config.Id = Guid.NewGuid();
            config.StatisticsItemId = request.StatisticsItemId;
            config.MinDuration = request.MinDuration;
            config.MaxDuration = request.MaxDuration;
            cacheKeys = CacheKeysFor(config).ToArray();
            storage.Add(config);
            // A new definition counts every recorded passing from ValidFromUtc (all of them when omitted).
            StatisticsConfigVersioning.Create(storage, item, config, request.ValidFromUtc, DateTime.UtcNow);
            await storage.SaveChangesAsync(cancellationToken);
            await storage.Database.CurrentTransaction!.CommitAsync(cancellationToken);
            return TypedResults.Created($"/api/statistics/admin/configurations/{config.Id}", ToDto(config));
        }, cancellationToken);
        if (result.Result is not Created<StatisticsConfigurationDTO>) return result;
        await StatisticsCacheInvalidation.InvalidateAsync(cache, logger, cacheKeys, [StatisticsCacheTags.ConfigurationDefinitions], invalidation);
        return result;
    }

    private static async Task<Results<Ok<StatisticsConfigurationDTO>, NotFound, ProblemHttpResult>> UpdateConfiguration(
        Guid id, SaveStatisticsConfigurationRequest request, StatisticsDbContext storage, ICoursePointReferences points, ICourseLayoutReferences layouts, HybridCache cache,
        ICacheInvalidationOperations invalidation, ILogger<StatisticsDbContext> logger, CancellationToken cancellationToken)
    {
        var cacheKeys = Array.Empty<string>();
        var result = await StatisticsDefinitionTransaction.RunAsync<Results<Ok<StatisticsConfigurationDTO>, NotFound, ProblemHttpResult>>(storage, async () =>
        {
            var existingItemId = await storage.Set<StatisticsItemConfig>().AsNoTracking()
                .Where(config => config.Id == id).Select(config => (Guid?)config.StatisticsItemId).SingleOrDefaultAsync(cancellationToken);
            if (existingItemId is null) return TypedResults.NotFound();
            await LockItemAsync(storage, existingItemId.Value, cancellationToken);
            var config = await storage.Set<StatisticsItemConfig>().Include(value => value.StatisticsItem)
                .SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
            if (config is null) return TypedResults.NotFound();
            if (config.StatisticsItemId != existingItemId)
                return Conflict("The configuration moved to another item. Retry with current state.");
            if (ToDto(config).Type != request.Type) return Invalid("Configuration type cannot be changed.");
            // Results are identified by their configuration and item, so moving one is a new definition.
            if (request.StatisticsItemId != config.StatisticsItemId)
                return Conflict("A configuration cannot move to another item. Create a new configuration instead.");
            // Resolve preserve semantics before validating against proposed endpoints.
            // Keep the original request unchanged: transaction retries must preserve the newly loaded snapshot,
            // not turn a prior attempt's preserved value into an explicit replacement.
            var proposedRequest = config is SimpleStatisticsItemConfig existingSimple && request.RequiredCoursePoints is null
                ? request with { RequiredCoursePoints = new(existingSimple.RequiredCourseLayoutId, existingSimple.RequiredCoursePointIds.ToArray()) }
                : request;
            if (await ValidateConfiguration(proposedRequest, storage, points, layouts, cancellationToken, id) is { } error) return Invalid(error);

            // Every value is dated (plan §4.2): the edit applies from its effective instant and the recompute
            // worker rebuilds results from there, so recorded results no longer block structural edits.
            var before = StatisticsConfigVersioning.Snapshot(config.StatisticsItem, config);
            var oldCacheKeys = CacheKeysFor(config).ToArray();
            config.MinDuration = request.MinDuration;
            config.MaxDuration = request.MaxDuration;
            if (config is SimpleStatisticsItemConfig simple)
            {
                simple.CoursePointStart = request.CoursePointStart!.Value;
                simple.CoursePointEnd = request.CoursePointEnd!.Value;
                simple.RequiredCourseLayoutId = proposedRequest.RequiredCoursePoints?.CourseLayoutId;
                simple.RequiredCoursePointIds = proposedRequest.RequiredCoursePoints?.CoursePointIds?.ToArray() ?? [];
            }
            else if (config is MultiStatisticsItemConfig multi)
            {
                multi.ParentConfigId = request.ParentConfigId!.Value;
                multi.Repetitions = request.Repetitions!.Value;
            }
            cacheKeys = oldCacheKeys.Concat(CacheKeysFor(config)).ToArray();
            await StatisticsConfigVersioning.ApplyFromAsync(storage, before, StatisticsConfigVersioning.Snapshot(config.StatisticsItem, config),
                EffectiveFrom(request.EffectiveFromUtc), DateTime.UtcNow, cancellationToken);
            await storage.SaveChangesAsync(cancellationToken);
            await storage.Database.CurrentTransaction!.CommitAsync(cancellationToken);
            return TypedResults.Ok(ToDto(config));
        }, cancellationToken);
        if (result.Result is not Ok<StatisticsConfigurationDTO>) return result;
        await StatisticsCacheInvalidation.InvalidateAsync(cache, logger,
            cacheKeys, [StatisticsCacheTags.ConfigurationDefinitions], invalidation);
        return result;
    }

    private static async Task<Ok<List<StatisticsConfigurationVersionDTO>>> ListConfigurationVersions(Guid id,
        StatisticsDbContext storage, CancellationToken cancellationToken) =>
        TypedResults.Ok((await storage.Set<StatisticsConfigVersion>().AsNoTracking().Where(version => version.StatisticsItemConfigId == id)
                .OrderBy(version => version.ValidFromUtc ?? DateTime.MinValue).ToListAsync(cancellationToken))
            .Select(version => new StatisticsConfigurationVersionDTO(version.Id, version.StatisticsItemConfigId, version.StatisticsItemId,
                Utc(version.ValidFromUtc), Utc(version.ValidToUtc), version.Distance, version.DistanceMode, version.ItemMinDuration,
                version.ItemMaxDuration, version.MinDuration, version.MaxDuration,
                version.CoursePointStart == Guid.Empty ? null : version.CoursePointStart,
                version.CoursePointEnd == Guid.Empty ? null : version.CoursePointEnd,
                version.RequiredCoursePointIds.Length == 0 ? null : new(version.RequiredCourseLayoutId, version.RequiredCoursePointIds.ToArray()),
                version.ParentConfigId, version.Repetitions))
            .ToList());

    /// <summary>Serializes definition edits of one item (and its configurations' version histories).</summary>
    private static Task LockItemAsync(StatisticsDbContext storage, Guid itemId, CancellationToken cancellationToken) =>
        StatisticsLocks.AdvisoryLockAsync(storage, $"statistics-item:{itemId:D}", cancellationToken);

    /// <summary>An edit without an effective instant applies from now (decision §6.1: never silently to history).</summary>
    private static DateTime EffectiveFrom(DateTime? requested) =>
        requested is { } value ? DateTime.SpecifyKind(value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value, DateTimeKind.Utc) : DateTime.UtcNow;

    private static DateTime? Utc(DateTime? value) => value is { } time ? DateTime.SpecifyKind(time, DateTimeKind.Utc) : null;

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteConfiguration(
        Guid id, StatisticsDbContext storage, HybridCache cache, ICacheInvalidationOperations invalidation,
        ILogger<StatisticsDbContext> logger, CancellationToken cancellationToken)
    {
        var config = await storage.Set<StatisticsItemConfig>().FindAsync([id], cancellationToken);
        if (config is null) return TypedResults.NotFound();
        var children = await storage.Set<MultiStatisticsItemConfig>().CountAsync(child => child.ParentConfigId == id, cancellationToken);
        var results = await storage.Set<StatisticsEntry>().CountAsync(entry => entry.StatisticsItemConfigId == id, cancellationToken);
        if (children > 0 || results > 0) return Conflict($"This configuration is referenced by {children} child configuration(s) and {results} result(s).");
        var cacheKeys = CacheKeysFor(config).ToArray();
        storage.Remove(config);
        await storage.SaveChangesAsync(cancellationToken);
        await StatisticsCacheInvalidation.InvalidateAsync(cache, logger, cacheKeys, [StatisticsCacheTags.ConfigurationDefinitions], invalidation);
        return TypedResults.NoContent();
    }

    private static IEnumerable<string> CacheKeysFor(StatisticsItemConfig config)
    {
        if (config is SimpleStatisticsItemConfig simple)
        {
            return new SimpleStatisticsCacheIdentity(simple.Id, simple.CoursePointEnd).Keys;
        }
        else if (config is MultiStatisticsItemConfig multi)
        {
            return [new MultiStatisticsCacheIdentity(multi.ParentConfigId).Key];
        }

        throw new InvalidOperationException("Unknown statistics configuration type.");
    }

    private static StatisticsItemDTO ToDto(StatisticsItem item) => new(item.Id, item.Name, item.Description, item.Distance, item.MinDuration, item.MaxDuration) { DistanceMode = item.DistanceMode };
    private static StatisticsConfigurationDTO ToDto(StatisticsItemConfig config) => config switch
    {
        SimpleStatisticsItemConfig simple => new(simple.Id, simple.StatisticsItemId, StatisticsConfigurationType.Simple, simple.CoursePointStart, simple.CoursePointEnd, null, null, simple.MinDuration, simple.MaxDuration)
        { RequiredCoursePoints = simple.RequiredCoursePointIds.Length == 0 ? null : new(simple.RequiredCourseLayoutId, simple.RequiredCoursePointIds.ToArray()) },
        MultiStatisticsItemConfig multi => new(multi.Id, multi.StatisticsItemId, StatisticsConfigurationType.Multi, null, null, multi.ParentConfigId, multi.Repetitions, multi.MinDuration, multi.MaxDuration),
        _ => throw new InvalidOperationException("Unknown configuration type.")
    };
    private static ProblemHttpResult Invalid(string detail) => TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest);
    private static ProblemHttpResult Conflict(string detail) => TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status409Conflict);
}