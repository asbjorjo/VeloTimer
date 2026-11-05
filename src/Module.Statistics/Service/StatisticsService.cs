using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service;

public class StatisticsService(
    StatisticsDbContext storage,
    HybridCache cache)
{
    // Always bound to this service's context so locks, classification and inserts share one transaction.
    private readonly StatisticsValidityScheduler scheduler = new(storage);

    /// <summary>
    /// Creates Simple entries for a completed sample. Must run inside the caller's StatisticsDbContext
    /// transaction (StatisticsOwnershipService) on PostgreSQL. Owning and dependent items are locked
    /// before live bounds are read. Insertions schedule dependent Multi revalidation atomically, including
    /// windows created before a late Simple entry arrived. Invalid measurements are retained and published.
    /// </summary>
    public async Task<IEnumerable<StatisticsEntry>> ProcessCompletedAsync(Guid transponderId, DateTime startTime, DateTime endTime, Guid startPoint, Guid endPoint, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("ProcessCompletedAsync");
        activity?.SetTag("TransponderId", transponderId);
        IEnumerable<StatisticsEntry> entries = new List<StatisticsEntry>();

        var statsItems = (await GetStatisticsItemConfigByEndCoursePoint(endPoint, cancellationToken)).ToList();

        if (statsItems.Any())
        {
            var dependentItems = await scheduler.GetDependentMultiItemIdsByParentConfigAsync(
                statsItems.Select(c => c.Id), cancellationToken);
            // Take the complete scope in UUID order, exclusively when scheduling will write it.
            // Do not upgrade individual shared locks after inserting (concurrent writers could deadlock).
            await scheduler.LockItemsAsync(statsItems.Select(c => c.StatisticsItemId).Concat(dependentItems),
                dependentItems.Count > 0 ? StatisticsItemLockMode.Exclusive : StatisticsItemLockMode.Shared, cancellationToken);
            var bounds = await ReadCurrentBoundsAsync(statsItems.Select(c => c.Id), cancellationToken);
            foreach (var statsItem in statsItems)
            {
                // Each item has its own start, independent of the completing segment's start.
                // Include adjacent earlier segments and prefer the persisted current segment when
                // it covers the entire item, rather than reaching back to a previous lap.
                var start = await storage.Set<Sample>()
                    .Where(s =>
                        s.TransponderId == transponderId
                        && s.CoursePointStartId == statsItem.CoursePointStart
                        && (s.TimeEnd <= startTime
                            || (s.TimeStart == startTime && s.TimeEnd == endTime
                                && s.CoursePointStartId == startPoint && s.CoursePointEndId == endPoint)))
                    .OrderByDescending(s => s.TimeEnd)
                    .FirstOrDefaultAsync(cancellationToken);
                if (start != null)
                {
                    // Derive duration, speed and validity from the persisted (microsecond) endpoints so the
                    // stored classification matches what readers and revalidation observe.
                    var timeStart = Persisted(start.TimeStart);
                    var timeEnd = Persisted(endTime);
                    var duration = timeEnd - timeStart;
                    StatisticsEntry entry = new()
                    {
                        TransponderId = transponderId,
                        TimeStart = timeStart,
                        TimeEnd = timeEnd,
                        StatisticsItemId = statsItem.StatisticsItem.Id,
                        Duration = duration,
                        // Raw SI speed is retained even when nonfinite (zero duration); validity excludes it.
                        Speed = statsItem.StatisticsItem.Distance / duration.TotalSeconds,
                        StatisticsItemConfigId = statsItem.Id,
                        FacilityId = start.FacilityId,
                    };
                    StatisticsValidityEvaluator.Apply(entry,
                        StatisticsValidityEvaluator.Evaluate(duration, contributorsValid: true, Bounds(bounds, statsItem.Id)));
                    entries = entries.Append(entry);
                }
            }
            await storage.AddRangeAsync(entries, cancellationToken);
            await storage.SaveChangesAsync(cancellationToken);
            if (entries.Any())
                await scheduler.ScheduleDependentMultiRevalidationAsync(dependentItems, cancellationToken);
        }
        activity?.SetTag("EntriesCreated", entries.Count());

        return entries;
    }

    /// <summary>
    /// Creates Multi entries for a newly created parent (Simple) entry. The window is fixed and
    /// deterministic: the newest parent entries of this transponder/configuration ending at or before
    /// the triggering entry's end, ordered by TimeEnd then Id (both descending). Invalid parents are
    /// never skipped. This selection only defines the new Multi's endpoints and facility; contributors
    /// are all current parent Simple entries of the same transponder/facility fully contained in that
    /// window. Exactly Repetitions entries must match and all must be valid. The Multi items are
    /// share-locked before reading live bounds and contributors (requires the caller's transaction on
    /// PostgreSQL). Later parent insertions/validity changes durably reschedule these items.
    /// </summary>
    public async Task<IEnumerable<StatisticsEntry>> ProcessEntryAsync(Guid transponderId, Guid statisticsItemId, Guid configId, DateTime startTime, DateTime endTime, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("ProcessEntryAsync");
        IEnumerable<StatisticsEntry> entries = new List<StatisticsEntry>();

        var multiItems = (await GetMultiStatisticsItemConfigs(configId, cancellationToken: cancellationToken)).ToList();

        if (multiItems.Any())
        {
            await scheduler.LockItemsAsync(multiItems.Select(m => m.StatisticsItemId), StatisticsItemLockMode.Shared, cancellationToken);
            var bounds = await ReadCurrentBoundsAsync(multiItems.Select(m => m.Id), cancellationToken);
            var maxRepetitions = multiItems.Max(m => m.Repetitions);
            var allEntries = await storage.Set<StatisticsEntry>()
                .AsNoTracking()
                .Where(s => s.TransponderId == transponderId && s.StatisticsItemConfigId == configId && s.TimeEnd <= endTime)
                .OrderByDescending(s => s.TimeEnd)
                .ThenByDescending(s => s.Id)
                .Take(maxRepetitions)
                .Select(s => new { s.TimeStart, s.TimeEnd, s.FacilityId })
                .ToListAsync(cancellationToken);

            foreach (var multiItem in multiItems.Where(i => i.Repetitions <= allEntries.Count))
            {
                var relevantEntries = allEntries.Take(multiItem.Repetitions).ToList();
                // Contributor endpoints come from storage; normalization is idempotent and keeps the Multi
                // duration equal to the persisted endpoint difference.
                var timeStart = Persisted(relevantEntries.Last().TimeStart);
                var timeEnd = Persisted(relevantEntries.First().TimeEnd);
                var duration = timeEnd - timeStart;
                StatisticsEntry entry = new()
                {
                    TransponderId = transponderId,
                    TimeStart = timeStart,
                    TimeEnd = timeEnd,
                    StatisticsItemId = multiItem.StatisticsItem.Id,
                    Duration = duration,
                    Speed = multiItem.StatisticsItem.Distance / duration.TotalSeconds,
                    StatisticsItemConfigId = multiItem.Id,
                    FacilityId = relevantEntries.First().FacilityId,
                };
                entries = entries.Append(entry);
            }

            // Insert inside the caller's transaction so one bounded, set-based query can classify all
            // new windows by the very same rule as historical revalidation. No provisional row is
            // committed or published before classification succeeds.
            await storage.AddRangeAsync(entries, cancellationToken);
            await storage.SaveChangesAsync(cancellationToken);
            var contributorsValid = await ReadMultiContributorValidityAsync(storage, entries.Select(e => e.Id), cancellationToken);
            foreach (var entry in entries)
                StatisticsValidityEvaluator.Apply(entry, StatisticsValidityEvaluator.Evaluate(entry.Duration,
                    contributorsValid[entry.Id], Bounds(bounds, entry.StatisticsItemConfigId)));
            await storage.SaveChangesAsync(cancellationToken);
        }

        return entries;
    }

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

    /// <summary>
    /// The timestamp exactly as the configured store persists it. Differences of normalized endpoints are
    /// whole microseconds on PostgreSQL, so the stored interval equals the classified duration.
    /// </summary>
    private DateTime Persisted(DateTime value) =>
        StatisticsStoragePrecision.UsesMicrosecondPrecision(storage.Database) ? StatisticsStoragePrecision.Timestamp(value) : value;

    private static StatisticsDurationBounds Bounds(IReadOnlyDictionary<Guid, StatisticsDurationBounds> bounds, Guid configId) =>
        bounds.TryGetValue(configId, out var value)
            ? value
            : throw new InvalidOperationException($"Statistics configuration {configId} no longer exists; cannot classify a new entry.");

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
