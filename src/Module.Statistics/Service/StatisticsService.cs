using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service;

/// <summary>
/// Read helpers over Statistics definitions and stored entries. Entries are derived only by the
/// recompute worker (<c>StatisticsRecomputeService</c>); see docs/replay-safe-statistics-plan.md.
/// </summary>
public class StatisticsService(
    StatisticsDbContext storage,
    HybridCache cache)
{
    /// <summary>
    /// One grouped SQL query for a bounded set of persisted Multi windows (including uncommitted new
    /// entries in this context's transaction). Counts every matching parent, not a guessed latest subset.
    /// Only aggregate rows are materialized; missing/empty windows are invalid.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, bool>> ReadMultiContributorValidityAsync(
        StatisticsDbContext storage, IEnumerable<Guid> entryIds, CancellationToken cancellationToken = default)
    {
        var ids = entryIds.Distinct().ToArray();
        var result = ids.ToDictionary(id => id, _ => false);
        if (ids.Length == 0) return result;

        var counts = await (
            from multi in storage.Set<StatisticsEntry>().AsNoTracking()
            where ids.Contains(multi.Id)
            join config in storage.Set<MultiStatisticsItemConfig>() on multi.StatisticsItemConfigId equals config.Id
            join parent in storage.Set<StatisticsEntry>() on config.ParentConfigId equals parent.StatisticsItemConfigId
            where parent.StatisticsItemId == config.ParentConfig.StatisticsItemId
                && parent.TransponderId == multi.TransponderId
                && parent.FacilityId == multi.FacilityId
                && parent.TimeStart >= multi.TimeStart && parent.TimeEnd <= multi.TimeEnd
            group parent by new { multi.Id, config.Repetitions } into contributors
            select new
            {
                contributors.Key.Id,
                contributors.Key.Repetitions,
                Count = contributors.LongCount(),
                InvalidCount = contributors.LongCount(p => !p.IsValid)
            }).ToListAsync(cancellationToken);

        foreach (var count in counts)
            result[count.Id] = count.Repetitions > 0 && count.Count == count.Repetitions && count.InvalidCount == 0;
        return result;
    }

    /// <summary>
    /// Current effective bounds per configuration, read from storage (never from cached definitions).
    /// Call after locking the owning items.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, StatisticsDurationBounds>> ReadCurrentBoundsAsync(IEnumerable<Guid> configIds,
        CancellationToken cancellationToken = default)
    {
        var ids = configIds.Distinct().ToList();
        var rows = await storage.Set<StatisticsItemConfig>()
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new
            {
                c.Id,
                LocalMin = c.MinDuration,
                LocalMax = c.MaxDuration,
                GlobalMin = c.StatisticsItem.MinDuration,
                GlobalMax = c.StatisticsItem.MaxDuration
            })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Id,
            r => StatisticsDurationBounds.Effective(r.GlobalMin, r.GlobalMax, r.LocalMin, r.LocalMax));
    }

    public async Task<IEnumerable<MultiStatisticsItemConfig>> GetMultiStatisticsItemConfigs(Guid parentConfigId, CancellationToken cancellationToken = default)
    {
        var itemQuery = storage.Set<MultiStatisticsItemConfig>()
            .Where(s => s.ParentConfigId == parentConfigId)
            .Include(s => s.StatisticsItem)
            .OrderByDescending(s => s.Repetitions);

        return await cache.GetOrCreateAsync(
            StatisticsCacheKeys.MultiConfigurationsByParent(parentConfigId),
            async cancel => await itemQuery.ToListAsync(cancellationToken: cancel),
            options: StatisticsCacheKeys.NewEntryOptions(),
            tags: [StatisticsCacheTags.ConfigurationDefinitions],
            cancellationToken: cancellationToken
            );
    }

    public async Task<IEnumerable<SimpleStatisticsItemConfig>> GetStatisticsItemConfigByEndCoursePoint(Guid coursePointId, CancellationToken cancellationToken = default)
    {
        var itemQuery = storage.Set<SimpleStatisticsItemConfig>()
            .Where(s => s.CoursePointEnd == coursePointId)
            .Include(s => s.StatisticsItem);
        return await cache.GetOrCreateAsync(
            StatisticsCacheKeys.ConfigurationsByEndCoursePoint(coursePointId),
            async cancel => await itemQuery.ToListAsync(cancellationToken: cancel),
            options: StatisticsCacheKeys.NewEntryOptions(),
            tags: [StatisticsCacheTags.ConfigurationDefinitions],
            cancellationToken: cancellationToken
            );
    }
}
