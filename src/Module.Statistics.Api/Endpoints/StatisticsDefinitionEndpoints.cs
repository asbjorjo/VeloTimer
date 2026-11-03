using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
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
    }

    private static async Task<Ok<List<StatisticsItemDTO>>> ListItems(StatisticsDbContext storage, CancellationToken cancellationToken) =>
        TypedResults.Ok(await storage.Set<StatisticsItem>().AsNoTracking().OrderBy(item => item.Name)
            .Select(item => new StatisticsItemDTO(item.Id, item.Name, item.Description, item.Distance, item.MinDuration, item.MaxDuration)).ToListAsync(cancellationToken));

    private static string? ValidateItem(SaveStatisticsItemRequest request) =>
        string.IsNullOrWhiteSpace(request.Name) ? "Name is required." :
        !float.IsFinite(request.Distance) || request.Distance <= 0 ? "Distance must be a finite positive number of metres." :
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
        SaveStatisticsItemRequest request, StatisticsDbContext storage, StatisticsValidityScheduler scheduler, CancellationToken cancellationToken)
        => await StatisticsDefinitionTransaction.RunAsync<Results<Created<StatisticsItemDTO>, ProblemHttpResult>>(storage, async () =>
    {
        if (ValidateItem(request) is { } error)
        {
            return Invalid(error);
        }

        var item = new StatisticsItem
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description ?? "",
            Distance = request.Distance,
            MinDuration = request.MinDuration,
            MaxDuration = request.MaxDuration
        };
        storage.Add(item);
        await storage.SaveChangesAsync(cancellationToken);
        await scheduler.LockThresholdScopeAsync(item.Id, true, cancellationToken);
        await storage.Database.CurrentTransaction!.CommitAsync(cancellationToken);
        return TypedResults.Created($"/api/statistics/admin/items/{item.Id}", ToDto(item));
    }, cancellationToken);

    private static async Task<Results<Ok<StatisticsItemDTO>, NotFound, ProblemHttpResult>> UpdateItem(
        Guid id, SaveStatisticsItemRequest request, StatisticsDbContext storage, HybridCache cache, ICacheInvalidationOperations invalidation,
        ILogger<StatisticsDbContext> logger, StatisticsValidityScheduler scheduler, IServiceProvider services, CancellationToken cancellationToken)
    {
        var cacheKeys = Array.Empty<string>();
        var result = await StatisticsDefinitionTransaction.RunAsync<Results<Ok<StatisticsItemDTO>, NotFound, ProblemHttpResult>>(storage, async () =>
        {
            await scheduler.LockThresholdScopeAsync(id, true, cancellationToken);
            var item = await storage.Set<StatisticsItem>().FindAsync([id], cancellationToken);
            if (item is null) return TypedResults.NotFound();
            if (ValidateItem(request) is { } error) return Invalid(error);
            var configs = await storage.Set<StatisticsItemConfig>()
                .Where(config => config.StatisticsItemId == id).ToListAsync(cancellationToken);
            if (configs.Any(config => StatisticsDurationBounds.Effective(request.MinDuration, request.MaxDuration,
                config.MinDuration, config.MaxDuration).IsDisjoint))
                return Invalid("Global bounds are disjoint from an existing configuration's effective range.");
            var configIds = configs.Select(config => config.Id).ToArray();
            if (item.Distance != request.Distance && await storage.Set<StatisticsEntry>()
                .AnyAsync(entry => entry.StatisticsItemId == id || configIds.Contains(entry.StatisticsItemConfigId), cancellationToken))
            {
                return Conflict("Recorded results depend on this distance. Create a new definition instead.");
            }

            var thresholdsChanged = item.MinDuration != request.MinDuration || item.MaxDuration != request.MaxDuration;
            item.MinDuration = request.MinDuration;
            item.MaxDuration = request.MaxDuration;
            item.Name = request.Name.Trim();
            item.Description = request.Description ?? "";
            item.Distance = request.Distance;
            cacheKeys = configs.SelectMany(CacheKeysFor).ToArray();
            await storage.SaveChangesAsync(cancellationToken);
            if (thresholdsChanged)
            {
                await scheduler.ScheduleThresholdChangeAsync(id, true, cancellationToken);
                // Resolve on this request scope only for real changes. No-op/metadata writes need no producer.
                await services.GetRequiredService<IStatisticsThresholdEnqueue>().EnqueueAsync(new(Guid.NewGuid(), id, null), cancellationToken);
            }
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
        StatisticsDbContext storage, ICoursePointReferences points, CancellationToken cancellationToken)
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
            var startFacility = await points.GetFacilityAsync(request.CoursePointStart!.Value, cancellationToken);
            var endFacility = request.CoursePointStart == request.CoursePointEnd ? startFacility : await points.GetFacilityAsync(request.CoursePointEnd!.Value, cancellationToken);
            if (startFacility is null || endFacility is null || startFacility != endFacility) return "Both coursepoints must exist in the same facility.";
        }
        else if (request.Type == StatisticsConfigurationType.Multi)
        {
            if (request.CoursePointStart is not null || request.CoursePointEnd is not null) return "Multi configurations inherit their coursepoints from their parent.";
            if (request.Repetitions is null or <= 0) return "Repetitions must be a positive integer.";
            if (!await storage.Set<SimpleStatisticsItemConfig>().AnyAsync(config => config.Id == request.ParentConfigId, cancellationToken)) return "Select an existing Simple configuration as the parent.";
        }
        else return "Unknown configuration type.";
        return null;
    }

    private static async Task<Results<Created<StatisticsConfigurationDTO>, ProblemHttpResult>> CreateConfiguration(
        SaveStatisticsConfigurationRequest request, StatisticsDbContext storage, ICoursePointReferences points, HybridCache cache,
        ICacheInvalidationOperations invalidation,
        ILogger<StatisticsDbContext> logger, StatisticsValidityScheduler scheduler, IServiceProvider services, CancellationToken cancellationToken)
    {
        var cacheKeys = Array.Empty<string>();
        var result = await StatisticsDefinitionTransaction.RunAsync<Results<Created<StatisticsConfigurationDTO>, ProblemHttpResult>>(storage, async () =>
        {
            if (!await LockConfigurationScope(request, null, storage, scheduler, cancellationToken))
                return Conflict("The parent configuration moved to another item. Retry with current state.");
            if (await ValidateConfiguration(request, storage, points, cancellationToken) is { } error) return Invalid(error);
            StatisticsItemConfig config = request.Type == StatisticsConfigurationType.Simple
                ? new SimpleStatisticsItemConfig { StatisticsItem = null!, CoursePointStart = request.CoursePointStart!.Value, CoursePointEnd = request.CoursePointEnd!.Value }
                : new MultiStatisticsItemConfig { StatisticsItem = null!, ParentConfig = null!, ParentConfigId = request.ParentConfigId!.Value, Repetitions = request.Repetitions!.Value };
            config.Id = Guid.NewGuid();
            config.StatisticsItemId = request.StatisticsItemId;
            config.MinDuration = request.MinDuration;
            config.MaxDuration = request.MaxDuration;
            cacheKeys = CacheKeysFor(config).ToArray();
            storage.Add(config);
            await storage.SaveChangesAsync(cancellationToken);
            if (config.MinDuration is not null || config.MaxDuration is not null)
            {
                await scheduler.ScheduleThresholdChangeAsync(config.StatisticsItemId, true, cancellationToken);
                await services.GetRequiredService<IStatisticsThresholdEnqueue>().EnqueueAsync(new(Guid.NewGuid(), config.StatisticsItemId, config.Id), cancellationToken);
            }
            await storage.Database.CurrentTransaction!.CommitAsync(cancellationToken);
            return TypedResults.Created($"/api/statistics/admin/configurations/{config.Id}", ToDto(config));
        }, cancellationToken);
        if (result.Result is not Created<StatisticsConfigurationDTO>) return result;
        await StatisticsCacheInvalidation.InvalidateAsync(cache, logger, cacheKeys, [StatisticsCacheTags.ConfigurationDefinitions], invalidation);
        return result;
    }

    private static async Task<Results<Ok<StatisticsConfigurationDTO>, NotFound, ProblemHttpResult>> UpdateConfiguration(
        Guid id, SaveStatisticsConfigurationRequest request, StatisticsDbContext storage, ICoursePointReferences points, HybridCache cache,
        ICacheInvalidationOperations invalidation,
        ILogger<StatisticsDbContext> logger, StatisticsValidityScheduler scheduler, IServiceProvider services, CancellationToken cancellationToken)
    {
        var cacheKeys = Array.Empty<string>();
        var result = await StatisticsDefinitionTransaction.RunAsync<Results<Ok<StatisticsConfigurationDTO>, NotFound, ProblemHttpResult>>(storage, async () =>
        {
            var existingItemId = await storage.Set<StatisticsItemConfig>().AsNoTracking()
                .Where(config => config.Id == id).Select(config => (Guid?)config.StatisticsItemId).SingleOrDefaultAsync(cancellationToken);
            if (existingItemId is null) return TypedResults.NotFound();
            if (!await LockConfigurationScope(request, existingItemId, storage, scheduler, cancellationToken))
                return Conflict("The parent configuration moved to another item. Retry with current state.");
            var config = await storage.Set<StatisticsItemConfig>().FindAsync([id], cancellationToken);
            if (config is null) return TypedResults.NotFound();
            if (config.StatisticsItemId != existingItemId)
                return Conflict("The configuration moved to another item. Retry with current state.");
            if (ToDto(config).Type != request.Type) return Invalid("Configuration type cannot be changed.");
            if (await ValidateConfiguration(request, storage, points, cancellationToken) is { } error) return Invalid(error);
            var proposed = new StatisticsConfigurationDTO(id, request.StatisticsItemId, request.Type, request.CoursePointStart, request.CoursePointEnd, request.ParentConfigId, request.Repetitions);
            var childIds = storage.Set<MultiStatisticsItemConfig>().Where(child => child.ParentConfigId == id).Select(child => child.Id);
            var structural = ToDto(config) with { MinDuration = null, MaxDuration = null };
            if (structural != proposed && await storage.Set<StatisticsEntry>()
                .AnyAsync(entry => entry.StatisticsItemConfigId == id || childIds.Contains(entry.StatisticsItemConfigId), cancellationToken))
            {
                return Conflict("Recorded results depend on this configuration. Create a new definition instead.");
            }

            var oldCacheKeys = CacheKeysFor(config).ToArray();
            var thresholdsChanged = config.MinDuration != request.MinDuration || config.MaxDuration != request.MaxDuration;
            config.MinDuration = request.MinDuration;
            config.MaxDuration = request.MaxDuration;
            config.StatisticsItemId = request.StatisticsItemId;
            if (config is SimpleStatisticsItemConfig simple)
            {
                simple.CoursePointStart = request.CoursePointStart!.Value;
                simple.CoursePointEnd = request.CoursePointEnd!.Value;
            }
            else if (config is MultiStatisticsItemConfig multi)
            {
                multi.ParentConfigId = request.ParentConfigId!.Value;
                multi.Repetitions = request.Repetitions!.Value;
            }
            cacheKeys = oldCacheKeys.Concat(CacheKeysFor(config)).ToArray();
            await storage.SaveChangesAsync(cancellationToken);
            if (thresholdsChanged)
            {
                await scheduler.ScheduleThresholdChangeAsync(config.StatisticsItemId, true, cancellationToken);
                await services.GetRequiredService<IStatisticsThresholdEnqueue>().EnqueueAsync(new(Guid.NewGuid(), config.StatisticsItemId, config.Id), cancellationToken);
            }
            await storage.Database.CurrentTransaction!.CommitAsync(cancellationToken);
            return TypedResults.Ok(ToDto(config));
        }, cancellationToken);
        if (result.Result is not Ok<StatisticsConfigurationDTO>) return result;
        await StatisticsCacheInvalidation.InvalidateAsync(cache, logger,
            cacheKeys, [StatisticsCacheTags.ConfigurationDefinitions], invalidation);
        return result;
    }

    private static async Task<bool> LockConfigurationScope(SaveStatisticsConfigurationRequest request, Guid? previousItemId,
        StatisticsDbContext storage, StatisticsValidityScheduler scheduler, CancellationToken cancellationToken)
    {
        var itemIds = new List<Guid> { request.StatisticsItemId };
        if (previousItemId is { } previous) itemIds.Add(previous);
        // Lock a Multi's parent item too: inserting/changing dependencies must serialize with parent saves.
        if (request.ParentConfigId is { } parentId)
        {
            var parentItemId = await storage.Set<SimpleStatisticsItemConfig>().AsNoTracking()
                .Where(parent => parent.Id == parentId).Select(parent => (Guid?)parent.StatisticsItemId)
                .SingleOrDefaultAsync(cancellationToken);
            if (parentItemId is { } parent) itemIds.Add(parent);
        }
        foreach (var itemId in itemIds.Distinct().Order())
            await scheduler.LockThresholdScopeAsync(itemId, true, cancellationToken);
        if (request.ParentConfigId is { } currentParentId)
        {
            var currentParentItemId = await storage.Set<SimpleStatisticsItemConfig>().AsNoTracking()
                .Where(parent => parent.Id == currentParentId).Select(parent => (Guid?)parent.StatisticsItemId)
                .SingleOrDefaultAsync(cancellationToken);
            if (currentParentItemId is { } current && !itemIds.Contains(current)) return false;
        }
        return true;
    }

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

    private static StatisticsItemDTO ToDto(StatisticsItem item) => new(item.Id, item.Name, item.Description, item.Distance, item.MinDuration, item.MaxDuration);
    private static StatisticsConfigurationDTO ToDto(StatisticsItemConfig config) => config switch
    {
        SimpleStatisticsItemConfig simple => new(simple.Id, simple.StatisticsItemId, StatisticsConfigurationType.Simple, simple.CoursePointStart, simple.CoursePointEnd, null, null, simple.MinDuration, simple.MaxDuration),
        MultiStatisticsItemConfig multi => new(multi.Id, multi.StatisticsItemId, StatisticsConfigurationType.Multi, null, null, multi.ParentConfigId, multi.Repetitions, multi.MinDuration, multi.MaxDuration),
        _ => throw new InvalidOperationException("Unknown configuration type.")
    };
    private static ProblemHttpResult Invalid(string detail) => TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest);
    private static ProblemHttpResult Conflict(string detail) => TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status409Conflict);
}