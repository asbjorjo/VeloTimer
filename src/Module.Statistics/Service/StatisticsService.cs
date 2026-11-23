using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using VeloTime.Module.Statistics.Interface.Data;
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
    /// Replays return the original persisted entry for at-least-once publication retry;
    /// they never reassess checkpoints or insert another row. The caller must serialize by transponder.
    /// </summary>
    public async Task<IEnumerable<StatisticsEntry>> ProcessCompletedAsync(Guid transponderId, DateTime startTime, DateTime endTime, Guid startPoint, Guid endPoint, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("ProcessCompletedAsync");
        activity?.SetTag("TransponderId", transponderId);
        IEnumerable<StatisticsEntry> entries = new List<StatisticsEntry>();
        startTime = Persisted(startTime);
        endTime = Persisted(endTime);

        var (statsItems, dependentItems) = await LockCompletionDefinitionsAsync(endPoint, cancellationToken);
        entries = await ReadReplayEntriesAsync<SimpleStatisticsItemConfig>(
            config => CompletionEntryId(config, transponderId, startTime, endTime, startPoint, endPoint), cancellationToken);

        if (statsItems.Any())
        {
            // SampleComplete carries resolved course points, not the source sample ID. Never silently
            // merge distinct Timing edges that resolve to the same completion contract.
            var sources = await storage.Set<Sample>().AsNoTracking()
                .Where(s => s.TransponderId == transponderId && s.TimeStart == startTime && s.TimeEnd == endTime
                    && s.CoursePointStartId == startPoint && s.CoursePointEndId == endPoint)
                .Select(s => new { s.TimingPointStartId, s.TimingPointEndId, s.FacilityId })
                .Distinct().Take(2).ToListAsync(cancellationToken);
            if (sources.Count > 1)
                throw new InvalidOperationException("SampleComplete identifies multiple persisted source edges; a source sample ID is required.");
            var bounds = await ReadCurrentBoundsAsync(statsItems.Select(c => c.Id), cancellationToken);
            foreach (var statsItem in statsItems)
            {
                var entryId = CompletionEntryId(statsItem.Id, transponderId, startTime, endTime, startPoint, endPoint);
                if (entries.Any(e => e.Id == entryId)) continue;
                if (statsItem.StatisticsItem.DistanceMode is not StatisticsDistanceMode.Fixed
                    and not StatisticsDistanceMode.CourseLayout)
                    throw new InvalidOperationException($"Statistics item {statsItem.StatisticsItemId} has an unsupported distance mode.");
                // Each item has its own start, independent of the completing segment's start.
                // Include adjacent earlier segments and prefer the persisted current segment when
                // it covers the entire item, rather than reaching back to a previous lap.
                if (statsItem.RequiredCoursePointIds.Length > 0
                    && statsItem.StatisticsItem.DistanceMode != StatisticsDistanceMode.CourseLayout)
                {
                    var attempt = await new RequiredCoursePointAttemptReader(storage).ReadAsync(statsItem,
                        transponderId, Persisted(startTime), Persisted(endTime), startPoint, cancellationToken);
                    if (attempt is null) continue;
                    if (attempt.ExistingEntry is not null)
                    {
                        entries = entries.Append(attempt.ExistingEntry);
                        continue;
                    }
                    var requiredEntry = CreateSimpleEntry(statsItem, transponderId, Persisted(attempt.TimeStart),
                        Persisted(endTime), attempt.FacilityId, attempt.Satisfied, Bounds(bounds, statsItem.Id), entryId);
                    entries = entries.Append(requiredEntry);
                    continue;
                }
                if (statsItem.StatisticsItem.DistanceMode == StatisticsDistanceMode.CourseLayout)
                {
                    if (await storage.Set<MultiStatisticsItemConfig>().AsNoTracking()
                            .AnyAsync(item => item.ParentConfigId == statsItem.Id, cancellationToken))
                        throw new InvalidOperationException("Multi statistics are not supported for course-layout distance items.");
                    if (statsItem.CoursePointStart != statsItem.CoursePointEnd)
                        throw new InvalidOperationException($"Course-layout item {statsItem.StatisticsItemId} must use a simple finish-to-finish configuration.");
                    var attempt = await new CourseLayoutLapAttemptReader(storage).ReadAsync(statsItem,
                        transponderId, Persisted(startTime), Persisted(endTime), startPoint, cancellationToken);
                    if (attempt.ExistingEntry is not null)
                    {
                        entries = entries.Append(attempt.ExistingEntry);
                        continue;
                    }
                    if (Persisted(endTime) <= Persisted(attempt.TimeStart))
                        throw new InvalidOperationException("Course-layout lap duration must be positive to calculate a persisted speed.");
                    var lapEntry = CreateSimpleEntry(statsItem, transponderId, Persisted(attempt.TimeStart),
                        Persisted(endTime), attempt.FacilityId, attempt.RequiredCoursePointsSatisfied,
                        Bounds(bounds, statsItem.Id), entryId, attempt.DistanceMetres);
                    entries = entries.Append(lapEntry);
                    continue;
                }
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
                    var entry = CreateSimpleEntry(statsItem, transponderId, timeStart, timeEnd,
                        start.FacilityId, true, Bounds(bounds, statsItem.Id), entryId);
                    entries = entries.Append(entry);
                }
            }
            var newEntries = entries.Where(e => storage.Entry(e).State == EntityState.Detached).ToList();
            await storage.AddRangeAsync(newEntries, cancellationToken);
            await storage.SaveChangesAsync(cancellationToken);
            if (newEntries.Count > 0)
                await scheduler.ScheduleDependentMultiRevalidationAsync(dependentItems, cancellationToken);
        }
        activity?.SetTag("EntriesCreated", entries.Count());

        return entries;
    }

    /// <summary>
    /// Discovery is only a lock-scope hint. Reload the entire endpoint selection, including item
    /// navigation/requirements, without cache or tracking after locking. A definition may have been
    /// added, deleted, moved or retargeted while we waited; dependencies may have moved too.
    /// Roll back only our lock attempt before retrying so we never extend/upgrade an existing scope
    /// out of UUID order. The caller transaction uses READ COMMITTED and stays intact, including
    /// its transponder lock; all entry writes occur only after this scope is verified.
    /// </summary>
    private async Task<(List<SimpleStatisticsItemConfig> Configs, IReadOnlyList<Guid> Dependents)>
        LockCompletionDefinitionsAsync(Guid endPoint, CancellationToken cancellationToken)
    {
        async Task<List<SimpleStatisticsItemConfig>> ReadAsync() =>
            await storage.Set<SimpleStatisticsItemConfig>().AsNoTracking()
                .Where(c => c.CoursePointEnd == endPoint).Include(c => c.StatisticsItem)
                .ToListAsync(cancellationToken);

        var configs = await ReadAsync();
        var dependents = await scheduler.GetDependentMultiItemIdsByParentConfigAsync(configs.Select(c => c.Id), cancellationToken);
        var transaction = storage.Database.CurrentTransaction;
        const string savepoint = "statistics_completion_scope";
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (transaction is not null) await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            var mode = dependents.Count > 0 ? StatisticsItemLockMode.Exclusive : StatisticsItemLockMode.Shared;
            var locked = await scheduler.LockItemsAsync(configs.Select(c => c.StatisticsItemId).Concat(dependents), mode, cancellationToken);
            var current = await ReadAsync();
            var currentDependents = await scheduler.GetDependentMultiItemIdsByParentConfigAsync(current.Select(c => c.Id), cancellationToken);
            var currentMode = currentDependents.Count > 0 ? StatisticsItemLockMode.Exclusive : StatisticsItemLockMode.Shared;
            if (mode == currentMode && locked.ToHashSet().SetEquals(current.Select(c => c.StatisticsItemId).Concat(currentDependents)))
            {
                if (transaction is not null) await transaction.ReleaseSavepointAsync(savepoint, cancellationToken);
                return (current, currentDependents);
            }
            if (transaction is not null)
            {
                await transaction.RollbackToSavepointAsync(savepoint, cancellationToken);
                await transaction.ReleaseSavepointAsync(savepoint, cancellationToken);
            }
            configs = current;
            dependents = currentDependents;
        }
    }

    private static StatisticsEntry CreateSimpleEntry(SimpleStatisticsItemConfig config, Guid transponderId,
        DateTime start, DateTime end, Guid facilityId, bool checkpointsSatisfied, StatisticsDurationBounds bounds,
        Guid entryId, double? distanceMetres = null)
    {
        var duration = end - start;
        var entry = new StatisticsEntry
        {
            Id = entryId,
            TransponderId = transponderId, TimeStart = start, TimeEnd = end,
            StatisticsItemId = config.StatisticsItem.Id, StatisticsItemConfigId = config.Id,
            Duration = duration, Speed = (distanceMetres ?? config.StatisticsItem.Distance) / duration.TotalSeconds,
            DistanceMetres = distanceMetres, FacilityId = facilityId, RequiredCoursePointsSatisfied = checkpointsSatisfied
        };
        StatisticsValidityEvaluator.Apply(entry, StatisticsValidityEvaluator.Evaluate(duration,
            contributorsValid: true, bounds, checkpointsSatisfied));
        return entry;
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
    /// Replays return existing windows without changing their endpoints or contributors, even after
    /// late parent arrivals or configuration edits. Identity is the full triggering EntryCreated tuple,
    /// not just the selected window's finish/facility. The caller must serialize by transponder.
    /// </summary>
    public async Task<IEnumerable<StatisticsEntry>> ProcessEntryAsync(Guid transponderId, Guid statisticsItemId, Guid configId, DateTime startTime, DateTime endTime, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("ProcessEntryAsync");
        IEnumerable<StatisticsEntry> entries = new List<StatisticsEntry>();
        startTime = Persisted(startTime);
        endTime = Persisted(endTime);

        var multiItems = await LockMultiDefinitionsAsync(configId, cancellationToken);
        entries = await ReadReplayEntriesAsync<MultiStatisticsItemConfig>(
            config => ParentEntryId(config, transponderId, statisticsItemId, configId, startTime, endTime), cancellationToken);

        if (multiItems.Any())
        {
            // EntryCreated has no entry ID. Equal finishes are legitimate, but two persisted parents
            // with the entire same wire identity cannot be distinguished safely by this consumer.
            var parents = await storage.Set<StatisticsEntry>().AsNoTracking().Where(e =>
                    e.TransponderId == transponderId && e.StatisticsItemConfigId == configId
                    && e.TimeStart == startTime && e.TimeEnd == endTime)
                .Select(e => e.StatisticsItemId).Take(2).ToListAsync(cancellationToken);
            if (parents.Any(item => item != statisticsItemId))
                throw new InvalidOperationException("EntryCreated conflicts with its persisted parent statistics item.");
            if (parents.Count > 1)
                throw new InvalidOperationException("EntryCreated identifies multiple persisted parent entries; an entry ID is required.");
            var parentDistanceMode = await storage.Set<SimpleStatisticsItemConfig>().AsNoTracking()
                .Where(c => c.Id == configId).Select(c => (StatisticsDistanceMode?)c.StatisticsItem.DistanceMode)
                .SingleOrDefaultAsync(cancellationToken);
            if (parentDistanceMode == StatisticsDistanceMode.CourseLayout
                || multiItems.Any(item => item.StatisticsItem.DistanceMode == StatisticsDistanceMode.CourseLayout))
                throw new InvalidOperationException("Multi statistics are not supported for course-layout distance items.");
            var bounds = await ReadCurrentBoundsAsync(multiItems.Select(m => m.Id), cancellationToken);
            var newEntries = new List<StatisticsEntry>();
            var maxRepetitions = multiItems.Max(m => m.Repetitions);
            var allEntries = await storage.Set<StatisticsEntry>()
                .AsNoTracking()
                .Where(s => s.TransponderId == transponderId && s.StatisticsItemConfigId == configId && s.TimeEnd <= endTime)
                .OrderByDescending(s => s.TimeEnd)
                .ThenByDescending(s => s.Id)
                .Take(maxRepetitions)
                .Select(s => new { s.TimeStart, s.TimeEnd, s.FacilityId })
                .ToListAsync(cancellationToken);

            foreach (var multiItem in multiItems)
            {
                var entryId = ParentEntryId(multiItem.Id, transponderId, statisticsItemId, configId, startTime, endTime);
                if (entries.Any(e => e.Id == entryId)) continue;
                if (multiItem.Repetitions > allEntries.Count) continue;
                var relevantEntries = allEntries.Take(multiItem.Repetitions).ToList();
                // Contributor endpoints come from storage; normalization is idempotent and keeps the Multi
                // duration equal to the persisted endpoint difference.
                var timeStart = Persisted(relevantEntries.Last().TimeStart);
                var timeEnd = Persisted(relevantEntries.First().TimeEnd);
                var duration = timeEnd - timeStart;
                StatisticsEntry entry = new()
                {
                    Id = entryId,
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
                newEntries.Add(entry);
            }

            // Insert inside the caller's transaction so one bounded, set-based query can classify all
            // new windows by the very same rule as historical revalidation. No provisional row is
            // committed or published before classification succeeds.
            await storage.AddRangeAsync(newEntries, cancellationToken);
            await storage.SaveChangesAsync(cancellationToken);
            var contributorsValid = await ReadMultiContributorValidityAsync(storage, newEntries.Select(e => e.Id), cancellationToken);
            foreach (var entry in newEntries)
                StatisticsValidityEvaluator.Apply(entry, StatisticsValidityEvaluator.Evaluate(entry.Duration,
                    contributorsValid[entry.Id], Bounds(bounds, entry.StatisticsItemConfigId)));
            await storage.SaveChangesAsync(cancellationToken);
        }

        return entries;
    }

    // Enumerate the (small) definition key set, not entry history. A configuration can have moved
    // to another endpoint/parent/owner since commit; live routing must not hide an unpublished
    // original result on retry. Only exact source-derived primary keys match. Deleted entries
    // cannot be recovered here; durable publication requires a worker-owned transactional outbox.
    private async Task<List<StatisticsEntry>> ReadReplayEntriesAsync<TConfig>(Func<Guid, Guid> sourceEntryId,
        CancellationToken cancellationToken) where TConfig : StatisticsItemConfig
    {
        var configs = await storage.Set<TConfig>().AsNoTracking().Select(c => c.Id).ToListAsync(cancellationToken);
        var ids = configs.Select(sourceEntryId).ToArray();
        return await storage.Set<StatisticsEntry>().Where(e => ids.Contains(e.Id)).ToListAsync(cancellationToken);
    }

    // Definition caches are for reads, not creation decisions. Match the completion lock protocol:
    // rediscover after waiting, and release a stale scope before acquiring another in UUID order.
    private async Task<List<MultiStatisticsItemConfig>> LockMultiDefinitionsAsync(Guid parentConfigId,
        CancellationToken cancellationToken)
    {
        async Task<List<MultiStatisticsItemConfig>> ReadAsync() =>
            await storage.Set<MultiStatisticsItemConfig>().AsNoTracking()
                .Where(c => c.ParentConfigId == parentConfigId).Include(c => c.StatisticsItem)
                .OrderByDescending(c => c.Repetitions).ToListAsync(cancellationToken);

        var configs = await ReadAsync();
        var transaction = storage.Database.CurrentTransaction;
        const string savepoint = "statistics_parent_scope";
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (transaction is not null) await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            var locked = await scheduler.LockItemsAsync(configs.Select(c => c.StatisticsItemId),
                StatisticsItemLockMode.Shared, cancellationToken);
            var current = await ReadAsync();
            if (locked.ToHashSet().SetEquals(current.Select(c => c.StatisticsItemId)))
            {
                if (transaction is not null) await transaction.ReleaseSavepointAsync(savepoint, cancellationToken);
                return current;
            }
            if (transaction is not null)
            {
                await transaction.RollbackToSavepointAsync(savepoint, cancellationToken);
                await transaction.ReleaseSavepointAsync(savepoint, cancellationToken);
            }
            configs = current;
        }
    }

    // Versioned, domain-separated source identities in the existing primary key: no schema change.
    // Do NOT derive IDs from mutable distance, requirements, ownership, selected starts/windows or
    // validity. UUID-sized SHA-256 digests have the same collision assumption as generated UUIDs.
    // Normalize timestamps BEFORE hashing, exactly as PostgreSQL persists them.
    private static Guid CompletionEntryId(Guid config, Guid rider, DateTime start, DateTime end, Guid from, Guid to) =>
        SourceEntryId(FormattableString.Invariant($"statistics-completion-v1|{config:D}|{rider:D}|{start.Ticks}|{end.Ticks}|{from:D}|{to:D}"));

    private static Guid ParentEntryId(Guid config, Guid rider, Guid parentItem, Guid parentConfig, DateTime start, DateTime end) =>
        SourceEntryId(FormattableString.Invariant($"statistics-parent-v1|{config:D}|{rider:D}|{parentItem:D}|{parentConfig:D}|{start.Ticks}|{end.Ticks}"));

    private static Guid SourceEntryId(string source) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(source)).AsSpan(0, 16));

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
