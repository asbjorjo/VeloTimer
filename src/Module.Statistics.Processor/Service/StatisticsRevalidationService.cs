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
    /// <summary>A batch committed; the cursor advanced or a coalesced Multi follow-up pass starts next.</summary>
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
    // Retained for diagnostic caller compatibility; window-based processing always returns zero.
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
            var contributorsValid = await StatisticsService.ReadMultiContributorValidityAsync(
                storage, multiRows.Select(r => r.Id), cancellationToken);
            classified.AddRange(multiRows.Select(r => (r, StatisticsValidityEvaluator.Evaluate(
                r.Duration, contributorsValid[r.Id], bounds[r.StatisticsItemConfigId]))));
        }

        var changed = classified.Where(c => c.Entry.IsValid != (c.Reason is null) || c.Entry.InvalidReason != c.Reason).ToList();
        foreach (var group in changed.GroupBy(c => c.Reason))
        {
            var ids = group.Select(c => c.Entry.Id).ToList();
            var reason = group.Key;
            var valid = reason is null;
            // Validity only: measurements, ownership and creation events are untouched.
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
        // Consume the rescan only at the end of a Multi sweep, never at each batch. The row lock
        // serializes arrivals with this checkpoint: a later arrival either requests another pass
        // on the pending job or wakes an idle job, so none can be lost at completion.
        var rescan = exhausted && phase == StatisticsValidityRevalidationPhase.Multi && job.MultiRescanPending;
        var outcome = !exhausted || rescan ? StatisticsRevalidationBatchOutcome.Progressed
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
                .SetProperty(r => r.MultiRescanPending, !exhausted && job.MultiRescanPending)
                .SetProperty(r => r.IsPending, stillPending), cancellationToken);
        if (advanced != 1)
            throw new DbUpdateConcurrencyException(
                $"Revalidation job for item {itemId} no longer has generation {expectedGeneration}; discarding the batch.");

        await transaction.CommitAsync(cancellationToken);

        metrics.RevalidationBatchCommitted(rows.Count, changed.Count, dependentsScheduled, 0, 0);
        logger.LogDebug(
            "Revalidation of item {StatisticsItemId} generation {Generation} phase {Phase}: scanned {Scanned}, reclassified {Reclassified}, dependents scheduled {Dependents}, outcome {Outcome}",
            itemId, expectedGeneration, phase, rows.Count, changed.Count, dependentsScheduled, outcome);
        return new(outcome, expectedGeneration, rows.Count, changed.Count, dependentsScheduled, 0, 0);
    }

    private async Task<JobRow?> LockJobAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var names = Names();
        var rows = await storage.Database.SqlQueryRaw<JobRow>(
                $"SELECT r.{names.JobGeneration}, r.{names.JobPending}, " +
                $"r.{names.JobPhase}, r.{names.JobCursor}, r.{names.JobRescan} " +
                $"FROM {names.JobTable} AS r WHERE r.{names.JobItem} = @item FOR UPDATE",
                new NpgsqlParameter("item", itemId))
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    private async Task<List<EntryRow>> ReadPageAsync(Guid itemId, Guid? cursor, int batchSize, CancellationToken cancellationToken)
    {
        var names = Names();
        // Native PostgreSQL uuid comparison/order on the (statistics_item_id, id) index; no OFFSET.
        var sql = $"SELECT e.{names.EntryId}, e.{names.EntryConfig}, e.{names.EntryDuration}, " +
            $"e.{names.EntryValid}, e.{names.EntryReason} " +
            $"FROM {names.EntryTable} AS e WHERE e.{names.EntryItem} = @item " +
            (cursor.HasValue ? $"AND e.{names.EntryId} > @cursor " : "") +
            $"ORDER BY e.{names.EntryId} LIMIT @limit";
        var parameters = new List<object> { new NpgsqlParameter("item", itemId), new NpgsqlParameter("limit", batchSize) };
        if (cursor.HasValue) parameters.Add(new NpgsqlParameter("cursor", cursor.Value));
        var rows = await storage.Database.SqlQueryRaw<EntryRowData>(sql, parameters.ToArray()).ToListAsync(cancellationToken);
        return rows.Select(r => new EntryRow(r.Id, r.StatisticsItemConfigId, r.Duration,
            r.IsValid, r.InvalidReason is null ? null : (StatisticsEntryInvalidReason)r.InvalidReason.Value)).ToList();
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
        public bool MultiRescanPending { get; init; }
    }

    private sealed class EntryRowData
    {
        public Guid Id { get; init; }
        public Guid StatisticsItemConfigId { get; init; }
        public TimeSpan Duration { get; init; }
        public bool IsValid { get; init; }
        public int? InvalidReason { get; init; }
    }

    private sealed record EntryRow(Guid Id, Guid StatisticsItemConfigId, TimeSpan Duration,
        bool IsValid, StatisticsEntryInvalidReason? InvalidReason);

    private sealed record StoreNames(string JobTable, string JobItem, string JobGeneration, string JobPending,
        string JobPhase, string JobCursor, string JobRescan, string EntryTable, string EntryId, string EntryItem, string EntryConfig,
        string EntryDuration, string EntryValid, string EntryReason)
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
                J(nameof(StatisticsValidityRevalidation.MultiRescanPending)),
                sql.DelimitIdentifier(entryStore.Name, entryStore.Schema),
                E(nameof(StatisticsEntry.Id)), E(nameof(StatisticsEntry.StatisticsItemId)), E(nameof(StatisticsEntry.StatisticsItemConfigId)),
                E(nameof(StatisticsEntry.Duration)), E(nameof(StatisticsEntry.IsValid)), E(nameof(StatisticsEntry.InvalidReason)));
        }
    }
}
#pragma warning restore EF1002, EF1003
