using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service;

// Raw SQL here only concatenates delimited identifiers resolved from the EF model; all values are parameters.
#pragma warning disable EF1003

public enum StatisticsItemLockMode
{
    /// <summary>Entry writers: classify new entries against current bounds/contributors.</summary>
    Shared,
    /// <summary>Definition writers and revalidation batches: change bounds, validity or the job.</summary>
    Exclusive
}

/// <summary>
/// Item lock and durable revalidation scheduling over the per-item
/// <see cref="StatisticsValidityRevalidation"/> rows. Use the same scoped
/// <see cref="StatisticsDbContext"/> and active transaction as the definition/entry write and the
/// outbox publish, then commit. Locks are transaction scoped (PostgreSQL row locks); nothing is held
/// across transactions. On non-PostgreSQL providers (SQLite tests) rows are maintained but not locked.
/// </summary>
/// <remarks>
/// A missing row means the item has never been classified under this protocol, so every insertion
/// path creates it <b>pending</b>; existing rows (pending or completed) are never reset by locking or
/// bootstrap. Callers must retry PostgreSQL deadlock (40P01) and serialization (40001) failures.
/// </remarks>
public sealed class StatisticsValidityScheduler(StatisticsDbContext storage)
{
    /// <summary>
    /// Creates missing checkpoint rows (pending) for existing items, then locks the rows in PostgreSQL
    /// UUID order with FOR SHARE (<see cref="StatisticsItemLockMode.Shared"/>) or FOR UPDATE
    /// (<see cref="StatisticsItemLockMode.Exclusive"/>). Must run before reading definitions,
    /// thresholds or contributor validity. Requires an active transaction on PostgreSQL.
    /// Returns the IDs of existing items that are now locked; unknown item IDs are omitted.
    /// Re-locking an already locked item in the same transaction is harmless.
    /// </summary>
    public async Task<IReadOnlyCollection<Guid>> LockItemsAsync(IEnumerable<Guid> itemIds, StatisticsItemLockMode mode,
        CancellationToken cancellationToken = default)
    {
        var ids = itemIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        RequireTransaction();
        await EnsureRowsAsync(ids, cancellationToken);
        if (!storage.Database.IsNpgsql())
        {
            return await storage.Set<StatisticsValidityRevalidation>().AsNoTracking()
                .Where(r => ids.Contains(r.StatisticsItemId)).Select(r => r.StatisticsItemId).ToListAsync(cancellationToken);
        }

        var names = Names.For(storage);
        var clause = mode == StatisticsItemLockMode.Exclusive ? "FOR UPDATE" : "FOR SHARE";
        // Single ordered statement: rows are locked in uuid order after sorting, giving all writers one order.
        var sql = $"SELECT r.{names.JobItem} AS \"Value\" FROM {names.JobTable} AS r " +
            $"WHERE r.{names.JobItem} = ANY(@ids) ORDER BY r.{names.JobItem} {clause} OF r";
        return await storage.Database.SqlQueryRaw<Guid>(sql, UuidArray("ids", ids)).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Items owning Multi configurations whose parent Simple configuration belongs to one of the given
    /// items, from configuration definitions (covers legacy rows without lineage). May include a parent
    /// item itself when it also owns such a Multi configuration.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetDependentMultiItemIdsAsync(IEnumerable<Guid> parentItemIds,
        CancellationToken cancellationToken = default)
    {
        var ids = parentItemIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return await storage.Set<MultiStatisticsItemConfig>().AsNoTracking()
            .Where(m => ids.Contains(m.ParentConfig.StatisticsItemId))
            .Select(m => m.StatisticsItemId).Distinct().ToListAsync(cancellationToken);
    }

    /// <summary>Items owning Multi configurations whose parent is one of the given Simple configurations.</summary>
    public async Task<IReadOnlyList<Guid>> GetDependentMultiItemIdsByParentConfigAsync(IEnumerable<Guid> parentConfigIds,
        CancellationToken cancellationToken = default)
    {
        var ids = parentConfigIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return await storage.Set<MultiStatisticsItemConfig>().AsNoTracking()
            .Where(m => ids.Contains(m.ParentConfigId))
            .Select(m => m.StatisticsItemId).Distinct().ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Lock scope for a threshold save: the item and, when requested, dependent Multi items, locked
    /// exclusively in one ordered statement. Call before reading/validating definitions.
    /// </summary>
    public async Task<IReadOnlyCollection<Guid>> LockThresholdScopeAsync(Guid itemId, bool includeDependentMultiItems,
        CancellationToken cancellationToken = default)
    {
        var scope = new List<Guid> { itemId };
        if (includeDependentMultiItems) scope.AddRange(await GetDependentMultiItemIdsAsync([itemId], cancellationToken));
        return await LockItemsAsync(scope, StatisticsItemLockMode.Exclusive, cancellationToken);
    }

    /// <summary>
    /// For a real (non-no-op) global or configuration threshold save of <paramref name="itemId"/>:
    /// restarts the item's job (Generation+1, pending, Simple phase, no cursor) and, when requested,
    /// schedules dependent Multi items via <see cref="ScheduleDependentMultiRevalidationAsync"/>.
    /// Locks exclusively (no-op if already held). Dependents are optional because the worker already
    /// schedules them in the same transaction as any parent validity change; including them only adds
    /// an early sweep. Returns the number of rows scheduled.
    /// </summary>
    public async Task<int> ScheduleThresholdChangeAsync(Guid itemId, bool includeDependentMultiItems,
        CancellationToken cancellationToken = default)
    {
        var locked = await LockThresholdScopeAsync(itemId, includeDependentMultiItems, cancellationToken);
        var scheduled = await ScheduleItemRevalidationAsync([itemId], cancellationToken);
        if (includeDependentMultiItems)
            scheduled += await ScheduleDependentMultiRevalidationAsync(locked.Where(id => id != itemId), cancellationToken);
        return scheduled;
    }

    /// <summary>
    /// Full restart for items whose own bounds changed: Generation+1, pending, Simple phase, cursor null.
    /// Locks the rows exclusively first. Do not call for no-op saves.
    /// </summary>
    public async Task<int> ScheduleItemRevalidationAsync(IEnumerable<Guid> itemIds, CancellationToken cancellationToken = default)
    {
        var ids = (await LockItemsAsync(itemIds, StatisticsItemLockMode.Exclusive, cancellationToken)).ToArray();
        if (ids.Length == 0) return 0;
        return await storage.Set<StatisticsValidityRevalidation>()
            .Where(r => ids.Contains(r.StatisticsItemId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Generation, r => r.Generation + 1)
                .SetProperty(r => r.IsPending, true)
                .SetProperty(r => r.Phase, StatisticsValidityRevalidationPhase.Simple)
                .SetProperty(r => r.LastEntryId, (Guid?)null), cancellationToken);
    }

    /// <summary>
    /// Parent contributor validity changed: Multi results of these items must be re-evaluated.
    /// Generation+1 and pending. A job still pending in its Simple phase keeps phase and cursor
    /// (its Multi phase starts from the beginning afterwards); otherwise the Multi phase restarts
    /// from the beginning. Own Simple entries are not rescanned. Locks the rows exclusively first.
    /// Do not pass the item whose Simple batch caused the change: its own later Multi phase covers it.
    /// </summary>
    public async Task<int> ScheduleDependentMultiRevalidationAsync(IEnumerable<Guid> itemIds,
        CancellationToken cancellationToken = default)
    {
        var ids = (await LockItemsAsync(itemIds, StatisticsItemLockMode.Exclusive, cancellationToken)).ToArray();
        if (ids.Length == 0) return 0;
        return await storage.Set<StatisticsValidityRevalidation>()
            .Where(r => ids.Contains(r.StatisticsItemId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Generation, r => r.Generation + 1)
                .SetProperty(r => r.LastEntryId, r =>
                    r.IsPending && r.Phase == StatisticsValidityRevalidationPhase.Simple ? r.LastEntryId : null)
                .SetProperty(r => r.Phase, r =>
                    r.IsPending && r.Phase == StatisticsValidityRevalidationPhase.Simple
                        ? StatisticsValidityRevalidationPhase.Simple
                        : StatisticsValidityRevalidationPhase.Multi)
                .SetProperty(r => r.IsPending, true), cancellationToken);
    }

    /// <summary>
    /// Rollout bootstrap: creates a pending job for every item lacking one. Idempotent; never resets
    /// existing (pending or completed) jobs, so it is safe on every startup. Returns rows inserted.
    /// </summary>
    public async Task<int> EnsureJobsForAllItemsAsync(CancellationToken cancellationToken = default)
    {
        var names = Names.For(storage);
        if (storage.Database.IsNpgsql())
        {
            return await storage.Database.ExecuteSqlRawAsync(
                $"INSERT INTO {names.JobTable} ({names.JobColumns}) " +
                $"SELECT i.{names.ItemId}, 1, TRUE, 0, NULL FROM {names.ItemTable} AS i " +
                $"ON CONFLICT ({names.JobItem}) DO NOTHING", cancellationToken);
        }
        return await storage.Database.ExecuteSqlRawAsync(
            $"INSERT OR IGNORE INTO {names.JobTable} ({names.JobColumns}) " +
            $"SELECT i.{names.ItemId}, 1, 1, 0, NULL FROM {names.ItemTable} AS i", cancellationToken);
    }

    private async Task EnsureRowsAsync(Guid[] ids, CancellationToken cancellationToken)
    {
        var names = Names.For(storage);
        if (storage.Database.IsNpgsql())
        {
            await storage.Database.ExecuteSqlRawAsync(
                $"INSERT INTO {names.JobTable} ({names.JobColumns}) " +
                $"SELECT i.{names.ItemId}, 1, TRUE, 0, NULL FROM {names.ItemTable} AS i WHERE i.{names.ItemId} = ANY(@ids) " +
                $"ORDER BY i.{names.ItemId} ON CONFLICT ({names.JobItem}) DO NOTHING",
                [UuidArray("ids", ids)], cancellationToken);
            return;
        }
        foreach (var id in ids)
        {
            await storage.Database.ExecuteSqlRawAsync(
                $"INSERT OR IGNORE INTO {names.JobTable} ({names.JobColumns}) " +
                $"SELECT i.{names.ItemId}, 1, 1, 0, NULL FROM {names.ItemTable} AS i WHERE i.{names.ItemId} = {{0}}",
                [id], cancellationToken);
        }
    }

    private void RequireTransaction()
    {
        if (storage.Database.IsNpgsql() && storage.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Statistics item locks and revalidation scheduling require an active StatisticsDbContext transaction.");
    }

    private static NpgsqlParameter UuidArray(string name, Guid[] ids) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = ids };

    /// <summary>Store names resolved from the EF model (snake_case in production, defaults in SQLite tests).</summary>
    internal sealed record Names(string JobTable, string JobItem, string JobColumns, string ItemTable, string ItemId)
    {
        public static Names For(DbContext storage)
        {
            var sql = storage.GetService<ISqlGenerationHelper>();
            var job = storage.Model.FindEntityType(typeof(StatisticsValidityRevalidation))!;
            var item = storage.Model.FindEntityType(typeof(StatisticsItem))!;
            var jobStore = StoreObjectIdentifier.Table(job.GetTableName()!, job.GetSchema());
            var itemStore = StoreObjectIdentifier.Table(item.GetTableName()!, item.GetSchema());
            string Column(IEntityType type, StoreObjectIdentifier store, string property) =>
                sql.DelimitIdentifier(type.FindProperty(property)!.GetColumnName(store)!);
            var columns = string.Join(", ", new[]
            {
                nameof(StatisticsValidityRevalidation.StatisticsItemId),
                nameof(StatisticsValidityRevalidation.Generation),
                nameof(StatisticsValidityRevalidation.IsPending),
                nameof(StatisticsValidityRevalidation.Phase),
                nameof(StatisticsValidityRevalidation.LastEntryId)
            }.Select(p => Column(job, jobStore, p)));
            return new(
                sql.DelimitIdentifier(jobStore.Name, jobStore.Schema),
                Column(job, jobStore, nameof(StatisticsValidityRevalidation.StatisticsItemId)),
                columns,
                sql.DelimitIdentifier(itemStore.Name, itemStore.Schema),
                Column(item, itemStore, nameof(StatisticsItem.Id)));
        }
    }
}
#pragma warning restore EF1003
