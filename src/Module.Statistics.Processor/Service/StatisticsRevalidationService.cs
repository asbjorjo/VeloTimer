using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service;

// Raw SQL only concatenates delimited identifiers resolved from the EF model and a numeric constant;
// all values are parameters.
#pragma warning disable EF1002, EF1003

public enum StatisticsRevalidationBatchOutcome
{
    /// <summary>The job is idle (completed earlier, or never needed); nothing changed.</summary>
    NotPending,
    /// <summary>A batch was committed and the cursor advanced within the current phase.</summary>
    Progressed,
    /// <summary>The Simple phase was exhausted; the Multi phase starts from the beginning next.</summary>
    SimplePhaseCompleted,
    /// <summary>All phases were exhausted and the job is now idle.</summary>
    Completed
}

public sealed record StatisticsRevalidationBatchResult(
    StatisticsRevalidationBatchOutcome Outcome,
    long Generation,
    int Scanned,
    int Reclassified,
    int DependentItemsScheduled,
    int UnprovenLegacyLineage,
    int InconsistentLineage);

/// <summary>
/// One bounded, short-transaction revalidation step for a single item job. PostgreSQL only: it uses
/// row locks and native uuid keyset ordering. Each call locks the job row, reads the current persisted
/// definitions, classifies at most one keyset page of entries (statistics_item_id = item AND id &gt; cursor
/// ORDER BY id LIMIT n), updates validity only, schedules dependent Multi items for changed parents,
/// and advances the checkpoint guarded by the job generation, all in one transaction.
/// </summary>
public sealed class StatisticsRevalidationService(
    StatisticsDbContext storage,
    StatisticsValidityScheduler scheduler,
    Metrics metrics,
    ILogger<StatisticsRevalidationService> logger)
{
    /// <summary>Maximum time to wait for an item or entry lock before treating the attempt as transient.</summary>
    public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
    private const int LoggedEntrySample = 5;

    /// <summary>Creates pending jobs for items without one; never resets existing jobs.</summary>
    public Task<int> BootstrapAsync(CancellationToken cancellationToken) =>
        scheduler.EnsureJobsForAllItemsAsync(cancellationToken);

    /// <summary>Next pending item after <paramref name="after"/> in uuid order, wrapping to the first.</summary>
    public async Task<Guid?> FindNextPendingItemAsync(Guid? after, CancellationToken cancellationToken)
    {
        RequireNpgsql();
        var names = Names();
        var sql = $"SELECT r.{names.JobItem} AS \"Value\" FROM {names.JobTable} AS r WHERE r.{names.JobPending} " +
            "{0} ORDER BY r." + names.JobItem + " LIMIT 1";
        if (after.HasValue)
        {
            var next = await storage.Database
                .SqlQueryRaw<Guid>(sql.Replace("{0}", $"AND r.{names.JobItem} > @after"), new NpgsqlParameter("after", after.Value))
                .ToListAsync(cancellationToken);
            if (next.Count > 0) return next[0];
        }
        var first = await storage.Database.SqlQueryRaw<Guid>(sql.Replace("{0}", "")).ToListAsync(cancellationToken);
        return first.Count > 0 ? first[0] : null;
    }

    public async Task<StatisticsRevalidationBatchResult> ProcessBatchAsync(Guid itemId, int batchSize, CancellationToken cancellationToken)
    {
        RequireNpgsql();
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        using var activity = Instrumentation.Source.StartActivity("Revalidation.ProcessBatch");
        activity?.SetTag("StatisticsItemId", itemId);
        storage.ChangeTracker.Clear();

        await using var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
        await storage.Database.ExecuteSqlRawAsync(
            $"SET LOCAL lock_timeout = '{(int)LockTimeout.TotalMilliseconds}ms'", cancellationToken);

        var job = await LockJobAsync(itemId, cancellationToken);
        if (job is null || !job.IsPending)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(StatisticsRevalidationBatchOutcome.NotPending, job?.Generation ?? 0, 0, 0, 0, 0, 0);
        }
        var phase = (StatisticsValidityRevalidationPhase)job.Phase;
        activity?.SetTag("Generation", job.Generation);
        activity?.SetTag("Phase", phase.ToString());

        // Current persisted definitions, read only after the job lock (never cached definitions).
        var item = await storage.Set<StatisticsItem>().AsNoTracking().SingleAsync(i => i.Id == itemId, cancellationToken);
        var configs = await storage.Set<StatisticsItemConfig>().AsNoTracking()
            .Where(c => c.StatisticsItemId == itemId).ToDictionaryAsync(c => c.Id, cancellationToken);
        var bounds = configs.Values.ToDictionary(c => c.Id, c => StatisticsDurationBounds.Effective(item, c));
        var phaseHasConfigs = configs.Values.Any(c => phase == StatisticsValidityRevalidationPhase.Simple
            ? c is SimpleStatisticsItemConfig
            : c is MultiStatisticsItemConfig);

        var rows = phaseHasConfigs ? await ReadPageAsync(itemId, job.LastEntryId, batchSize, cancellationToken) : [];
        var classified = new List<(EntryRow Entry, StatisticsEntryInvalidReason? Reason)>();
        var unproven = new List<Guid>();
        var inconsistent = new List<Guid>();
        foreach (var row in rows)
        {
            if (!configs.TryGetValue(row.StatisticsItemConfigId, out var config))
                throw new InvalidOperationException(
                    $"Statistics entry {row.Id} references configuration {row.StatisticsItemConfigId}, which does not belong to item {itemId}.");
            if (phase == StatisticsValidityRevalidationPhase.Simple && config is SimpleStatisticsItemConfig)
                classified.Add((row, StatisticsValidityEvaluator.Evaluate(row.Duration, true, bounds[config.Id])));
        }
        if (phase == StatisticsValidityRevalidationPhase.Multi)
        {
            var multiRows = rows.Where(r => configs[r.StatisticsItemConfigId] is MultiStatisticsItemConfig).ToList();
            var contributorsValid = await EvaluateContributorsAsync(multiRows, configs, unproven, inconsistent, cancellationToken);
            classified.AddRange(multiRows.Select(r => (r, StatisticsValidityEvaluator.Evaluate(
                r.Duration, contributorsValid[r.Id], bounds[r.StatisticsItemConfigId]))));
        }

        var changed = classified.Where(c => c.Entry.IsValid != (c.Reason is null) || c.Entry.InvalidReason != c.Reason).ToList();
        foreach (var group in changed.GroupBy(c => c.Reason))
        {
            var ids = group.Select(c => c.Entry.Id).ToList();
            var reason = group.Key;
            var valid = reason is null;
            // Validity only: measurements, ownership, lineage and creation events are untouched.
            var updated = await storage.Set<StatisticsEntry>().Where(e => ids.Contains(e.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsValid, valid).SetProperty(e => e.InvalidReason, reason), cancellationToken);
            if (updated != ids.Count)
                throw new InvalidOperationException($"Expected to reclassify {ids.Count} entries of item {itemId}, updated {updated}.");
        }

        var dependentsScheduled = 0;
        if (phase == StatisticsValidityRevalidationPhase.Simple && changed.Count > 0)
        {
            var dependents = (await scheduler.GetDependentMultiItemIdsByParentConfigAsync(
                    changed.Select(c => c.Entry.StatisticsItemConfigId), cancellationToken))
                .Where(id => id != itemId) // this job's own Multi phase runs after its Simple phase
                .ToList();
            dependentsScheduled = await scheduler.ScheduleDependentMultiRevalidationAsync(dependents, cancellationToken);
        }

        var exhausted = rows.Count < batchSize;
        var outcome = !exhausted ? StatisticsRevalidationBatchOutcome.Progressed
            : phase == StatisticsValidityRevalidationPhase.Simple ? StatisticsRevalidationBatchOutcome.SimplePhaseCompleted
            : StatisticsRevalidationBatchOutcome.Completed;
        var nextPhase = outcome == StatisticsRevalidationBatchOutcome.SimplePhaseCompleted ? StatisticsValidityRevalidationPhase.Multi : phase;
        Guid? nextCursor = exhausted ? null : rows[^1].Id;
        var stillPending = outcome != StatisticsRevalidationBatchOutcome.Completed;
        var expectedGeneration = job.Generation;
        var advanced = await storage.Set<StatisticsValidityRevalidation>()
            .Where(r => r.StatisticsItemId == itemId && r.Generation == expectedGeneration)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Phase, nextPhase)
                .SetProperty(r => r.LastEntryId, nextCursor)
                .SetProperty(r => r.IsPending, stillPending), cancellationToken);
        if (advanced != 1)
            throw new DbUpdateConcurrencyException(
                $"Revalidation job for item {itemId} no longer has generation {expectedGeneration}; discarding the batch.");

        await transaction.CommitAsync(cancellationToken);

        metrics.RevalidationBatchCommitted(rows.Count, changed.Count, dependentsScheduled, unproven.Count, inconsistent.Count);
        if (unproven.Count > 0)
            logger.LogWarning(
                "Classified {Count} legacy Multi entries of item {StatisticsItemId} as InvalidContributor: contributors without recorded lineage could not be proven (sample: {EntryIds})",
                unproven.Count, itemId, string.Join(", ", unproven.Take(LoggedEntrySample)));
        if (inconsistent.Count > 0)
            logger.LogWarning(
                "Classified {Count} Multi entries of item {StatisticsItemId} as InvalidContributor: recorded lineage is inconsistent with the configuration (sample: {EntryIds})",
                inconsistent.Count, itemId, string.Join(", ", inconsistent.Take(LoggedEntrySample)));
        logger.LogDebug(
            "Revalidation of item {StatisticsItemId} generation {Generation} phase {Phase}: scanned {Scanned}, reclassified {Reclassified}, dependents scheduled {Dependents}, outcome {Outcome}",
            itemId, expectedGeneration, phase, rows.Count, changed.Count, dependentsScheduled, outcome);
        return new(outcome, expectedGeneration, rows.Count, changed.Count, dependentsScheduled, unproven.Count, inconsistent.Count);
    }

    private async Task<JobRow?> LockJobAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var names = Names();
        var rows = await storage.Database.SqlQueryRaw<JobRow>(
                $"SELECT r.{names.JobGeneration}, r.{names.JobPending}, " +
                $"r.{names.JobPhase}, r.{names.JobCursor} " +
                $"FROM {names.JobTable} AS r WHERE r.{names.JobItem} = @item FOR UPDATE",
                new NpgsqlParameter("item", itemId))
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    private async Task<List<EntryRow>> ReadPageAsync(Guid itemId, Guid? cursor, int batchSize, CancellationToken cancellationToken)
    {
        var names = Names();
        // Native PostgreSQL uuid comparison/order on the (statistics_item_id, id) index; no OFFSET.
        var sql = $"SELECT e.{names.EntryId}, e.{names.EntryConfig}, " +
            $"e.{names.EntryTransponder}, e.{names.EntryStart}, " +
            $"e.{names.EntryEnd}, e.{names.EntryDuration}, " +
            $"e.{names.EntryValid}, e.{names.EntryReason}, " +
            $"e.{names.EntryLineage} " +
            $"FROM {names.EntryTable} AS e WHERE e.{names.EntryItem} = @item " +
            (cursor.HasValue ? $"AND e.{names.EntryId} > @cursor " : "") +
            $"ORDER BY e.{names.EntryId} LIMIT @limit";
        var parameters = new List<object> { new NpgsqlParameter("item", itemId), new NpgsqlParameter("limit", batchSize) };
        if (cursor.HasValue) parameters.Add(new NpgsqlParameter("cursor", cursor.Value));
        var rows = await storage.Database.SqlQueryRaw<EntryRowData>(sql, parameters.ToArray()).ToListAsync(cancellationToken);
        return rows.Select(r => new EntryRow(r.Id, r.StatisticsItemConfigId, r.TransponderId,
            DateTime.SpecifyKind(r.TimeStart, DateTimeKind.Utc), DateTime.SpecifyKind(r.TimeEnd, DateTimeKind.Utc), r.Duration,
            r.IsValid, r.InvalidReason is null ? null : (StatisticsEntryInvalidReason)r.InvalidReason.Value,
            r.HasCompleteContributorLineage)).ToList();
    }

    /// <summary>
    /// Contributor validity for Multi entries. Recorded lineage is used when complete and consistent.
    /// Legacy rows (no lineage) are reconstructed only when provable; otherwise they are treated as having
    /// an invalid contributor and reported. Never substitutes or skips contributors.
    /// </summary>
    private async Task<Dictionary<Guid, bool>> EvaluateContributorsAsync(List<EntryRow> multiRows,
        IReadOnlyDictionary<Guid, StatisticsItemConfig> configs, List<Guid> unproven, List<Guid> inconsistent,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, bool>();
        var withLineage = multiRows.Where(r => r.HasCompleteContributorLineage).Select(r => r.Id).ToList();
        var links = withLineage.Count == 0 ? [] : await storage.Set<StatisticsEntryContributor>().AsNoTracking()
            .Where(c => withLineage.Contains(c.StatisticsEntryId))
            .Select(c => new ContributorRow(c.StatisticsEntryId, c.Position, c.ContributorEntry.StatisticsItemConfigId,
                c.ContributorEntry.TransponderId, c.ContributorEntry.IsValid))
            .ToListAsync(cancellationToken);
        var linksByEntry = links.ToLookup(l => l.StatisticsEntryId);
        var parentItems = await ParentItemIdsAsync(multiRows, configs, cancellationToken);

        foreach (var row in multiRows)
        {
            var config = (MultiStatisticsItemConfig)configs[row.StatisticsItemConfigId];
            if (row.HasCompleteContributorLineage)
            {
                var ordered = linksByEntry[row.Id].OrderBy(l => l.Position).ToList();
                var consistent = config.Repetitions > 0 && ordered.Count == config.Repetitions &&
                    ordered.Select((l, i) => l.Position == i && l.StatisticsItemConfigId == config.ParentConfigId &&
                        l.TransponderId == row.TransponderId).All(ok => ok);
                if (!consistent)
                {
                    inconsistent.Add(row.Id);
                    result[row.Id] = false;
                    continue;
                }
                result[row.Id] = ordered.All(l => l.IsValid);
                continue;
            }

            var proven = parentItems.TryGetValue(config.ParentConfigId, out var parentItemId)
                ? await ProveLegacyContributorsAsync(row, config, parentItemId, cancellationToken)
                : null;
            if (proven is null)
            {
                logger.LogDebug("Legacy Multi entry {EntryId} has unprovable contributors; classifying InvalidContributor", row.Id);
                unproven.Add(row.Id);
                result[row.Id] = false;
                continue;
            }
            result[row.Id] = proven.Value;
        }
        return result;
    }

    private async Task<Dictionary<Guid, Guid>> ParentItemIdsAsync(List<EntryRow> multiRows,
        IReadOnlyDictionary<Guid, StatisticsItemConfig> configs, CancellationToken cancellationToken)
    {
        var parentConfigIds = multiRows.Where(r => !r.HasCompleteContributorLineage)
            .Select(r => ((MultiStatisticsItemConfig)configs[r.StatisticsItemConfigId]).ParentConfigId).Distinct().ToList();
        if (parentConfigIds.Count == 0) return [];
        return await storage.Set<StatisticsItemConfig>().AsNoTracking()
            .Where(c => parentConfigIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.StatisticsItemId, cancellationToken);
    }

    /// <summary>
    /// Legacy selection was "latest N parent entries by TimeEnd" at creation (no tie-breaker, no trigger
    /// scope); entries are never deleted. Let R be this transponder's parent entries inside
    /// [TimeStart, TimeEnd]. The original window W is provably R when |R| = N, R's newest ends at the
    /// Multi end, R's unique oldest-ending entry starts at the Multi start, and no parent entry starting
    /// before the Multi start ends within [min end of R, Multi end]. (Any w in W\R would start before
    /// the Multi start and end no earlier than W's oldest-ending entry, which is in R.) Returns all-valid
    /// for proven windows, null when not provable (e.g. a later overlapping or tied entry exists).
    /// </summary>
    private async Task<bool?> ProveLegacyContributorsAsync(EntryRow multi, MultiStatisticsItemConfig config, Guid parentItemId,
        CancellationToken cancellationToken)
    {
        var repetitions = config.Repetitions;
        if (repetitions < 1) return null;
        var parents = storage.Set<StatisticsEntry>().AsNoTracking().Where(p =>
            p.StatisticsItemId == parentItemId && p.TransponderId == multi.TransponderId &&
            p.StatisticsItemConfigId == config.ParentConfigId);
        var window = await parents
            .Where(p => p.TimeStart >= multi.TimeStart && p.TimeEnd <= multi.TimeEnd)
            .OrderByDescending(p => p.TimeEnd).ThenByDescending(p => p.Id)
            .Take(repetitions + 1)
            .Select(p => new { p.TimeStart, p.TimeEnd, p.IsValid })
            .ToListAsync(cancellationToken);
        if (window.Count != repetitions || window[0].TimeEnd != multi.TimeEnd) return null;
        var oldestEnd = window.Min(p => p.TimeEnd);
        var oldest = window.Where(p => p.TimeEnd == oldestEnd).ToList();
        if (oldest.Count != 1 || oldest[0].TimeStart != multi.TimeStart) return null;
        var competing = await parents.AnyAsync(p =>
            p.TimeStart < multi.TimeStart && p.TimeEnd >= oldestEnd && p.TimeEnd <= multi.TimeEnd, cancellationToken);
        return competing ? null : window.All(p => p.IsValid);
    }

    private void RequireNpgsql()
    {
        if (!storage.Database.IsNpgsql())
            throw new NotSupportedException("Statistics validity revalidation requires PostgreSQL (row locks and uuid keyset order).");
    }

    private StoreNames Names() => StoreNames.For(storage);

    // Ad-hoc SqlQuery types map columns with the context's naming convention, exactly like the entity
    // properties they mirror, so the selected store column names match without aliases.
    private sealed class JobRow
    {
        public long Generation { get; init; }
        public bool IsPending { get; init; }
        public int Phase { get; init; }
        public Guid? LastEntryId { get; init; }
    }

    private sealed class EntryRowData
    {
        public Guid Id { get; init; }
        public Guid StatisticsItemConfigId { get; init; }
        public Guid TransponderId { get; init; }
        public DateTime TimeStart { get; init; }
        public DateTime TimeEnd { get; init; }
        public TimeSpan Duration { get; init; }
        public bool IsValid { get; init; }
        public int? InvalidReason { get; init; }
        public bool HasCompleteContributorLineage { get; init; }
    }

    private sealed record EntryRow(Guid Id, Guid StatisticsItemConfigId, Guid TransponderId, DateTime TimeStart,
        DateTime TimeEnd, TimeSpan Duration, bool IsValid, StatisticsEntryInvalidReason? InvalidReason,
        bool HasCompleteContributorLineage);

    private sealed record ContributorRow(Guid StatisticsEntryId, int Position, Guid StatisticsItemConfigId,
        Guid TransponderId, bool IsValid);

    private sealed record StoreNames(string JobTable, string JobItem, string JobGeneration, string JobPending,
        string JobPhase, string JobCursor, string EntryTable, string EntryId, string EntryItem, string EntryConfig,
        string EntryTransponder, string EntryStart, string EntryEnd, string EntryDuration, string EntryValid,
        string EntryReason, string EntryLineage)
    {
        public static StoreNames For(DbContext storage)
        {
            var sql = storage.GetService<ISqlGenerationHelper>();
            var job = storage.Model.FindEntityType(typeof(StatisticsValidityRevalidation))!;
            var entry = storage.Model.FindEntityType(typeof(StatisticsEntry))!;
            var jobStore = StoreObjectIdentifier.Table(job.GetTableName()!, job.GetSchema());
            var entryStore = StoreObjectIdentifier.Table(entry.GetTableName()!, entry.GetSchema());
            string J(string p) => sql.DelimitIdentifier(job.FindProperty(p)!.GetColumnName(jobStore)!);
            string E(string p) => sql.DelimitIdentifier(entry.FindProperty(p)!.GetColumnName(entryStore)!);
            return new(sql.DelimitIdentifier(jobStore.Name, jobStore.Schema),
                J(nameof(StatisticsValidityRevalidation.StatisticsItemId)), J(nameof(StatisticsValidityRevalidation.Generation)),
                J(nameof(StatisticsValidityRevalidation.IsPending)), J(nameof(StatisticsValidityRevalidation.Phase)),
                J(nameof(StatisticsValidityRevalidation.LastEntryId)),
                sql.DelimitIdentifier(entryStore.Name, entryStore.Schema),
                E(nameof(StatisticsEntry.Id)), E(nameof(StatisticsEntry.StatisticsItemId)), E(nameof(StatisticsEntry.StatisticsItemConfigId)),
                E(nameof(StatisticsEntry.TransponderId)), E(nameof(StatisticsEntry.TimeStart)), E(nameof(StatisticsEntry.TimeEnd)),
                E(nameof(StatisticsEntry.Duration)), E(nameof(StatisticsEntry.IsValid)), E(nameof(StatisticsEntry.InvalidReason)),
                E(nameof(StatisticsEntry.HasCompleteContributorLineage)));
        }
    }
}
#pragma warning restore EF1002, EF1003
