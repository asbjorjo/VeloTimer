using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using VeloTime.Module.Statistics;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    [PostgreSqlStatisticsFact]
    public async Task SimpleArrivalsBetweenEveryMultiBatchCannotStarveTailOrFollowupAndEventuallyConverge()
    {
        const int size = 2;
        var (parent, multi, entries) = await SeedMultiSweep();
        await Change(multi.StatisticsItemId, null);
        Assert.Equal(StatisticsRevalidationBatchOutcome.SimplePhaseCompleted,
            (await Batch(multi.StatisticsItemId, size)).Outcome);

        // Keep real Simple creations arriving at EVERY batch boundary, for two whole
        // sweeps. These affect an already-visited Multi, behind the durable UUID cursor.
        // Each Batch/Job/arrival uses a fresh DbContext, also exercising restart persistence.
        for (var pass = 0; pass < 2; pass++)
        {
            for (var page = 0; page < entries.Length / size; page++)
            {
                var before = await Job(multi.StatisticsItemId);
                var result = await Batch(multi.StatisticsItemId, size);
                Assert.Equal(StatisticsRevalidationBatchOutcome.Progressed, result.Outcome);
                Assert.Equal(size, result.Scanned);
                var cursor = entries[(page + 1) * size - 1].Id;
                var advanced = await Job(multi.StatisticsItemId);
                Assert.Equal(cursor, advanced.LastEntryId);
                Assert.Equal(before.Generation, advanced.Generation);
                await CreateContainedSimple(parent, entries[0]);
                var notified = await Job(multi.StatisticsItemId);
                Assert.Equal(advanced.Generation + 1, notified.Generation);
                Assert.Equal(cursor, notified.LastEntryId);
                Assert.True(notified.MultiRescanPending);
                Assert.Equal(StatisticsValidityRevalidationPhase.Multi, notified.Phase);
            }

            // The invalid original tail must be reached BEFORE resetting the cursor.
            await using (var verify = Db())
            {
                var tailId = entries[^1].Id;
                var tail = await verify.Set<StatisticsEntry>().SingleAsync(e => e.Id == tailId);
                Assert.False(tail.IsValid);
                Assert.Equal(StatisticsEntryInvalidReason.InvalidContributor, tail.InvalidReason);
            }
            Assert.Equal(entries[^1].Id, (await Job(multi.StatisticsItemId)).LastEntryId);
            var exhausted = await Batch(multi.StatisticsItemId, size);
            Assert.Equal(0, exhausted.Scanned);
            Assert.Equal(StatisticsRevalidationBatchOutcome.Progressed, exhausted.Outcome);
            var followup = await Job(multi.StatisticsItemId);
            Assert.True(followup.IsPending);
            Assert.Equal(StatisticsValidityRevalidationPhase.Multi, followup.Phase);
            Assert.Null(followup.LastEntryId);
            Assert.False(followup.MultiRescanPending);
            await CreateContainedSimple(parent, entries[0]);
            Assert.True((await Job(multi.StatisticsItemId)).MultiRescanPending);
        }

        // Once arrivals stop, both the queued follow-up and job must finish.
        var final = await Drain(multi.StatisticsItemId, size);
        Assert.Equal(StatisticsRevalidationBatchOutcome.Completed, final[^1].Outcome);
        Assert.Equal(entries.Length * 2, final.Sum(b => b.Scanned));
        var idle = await Job(multi.StatisticsItemId);
        Assert.False(idle.IsPending);
        Assert.False(idle.MultiRescanPending);
        Assert.Null(idle.LastEntryId);
        Assert.Equal(StatisticsRevalidationBatchOutcome.NotPending,
            (await Batch(multi.StatisticsItemId, size)).Outcome);
        await using var db = Db();
        var persisted = await db.Set<StatisticsEntry>().Where(e => e.StatisticsItemId == multi.StatisticsItemId)
            .OrderBy(e => e.Id).ToArrayAsync();
        Assert.Equal(entries.Length, persisted.Length); // no aggregate recreation
        for (var i = 0; i < persisted.Length; i++)
        {
            var expectedValid = i != 0 && i != persisted.Length - 1;
            Assert.Equal(expectedValid, persisted[i].IsValid);
            Assert.Equal(expectedValid ? null : StatisticsEntryInvalidReason.InvalidContributor, persisted[i].InvalidReason);
            Assert.Equal(entries[i].TimeStart, persisted[i].TimeStart);
            Assert.Equal(entries[i].TimeEnd, persisted[i].TimeEnd);
            Assert.Equal(DateTimeKind.Utc, persisted[i].TimeStart.Kind);
            Assert.Equal(DateTimeKind.Utc, persisted[i].TimeEnd.Kind);
            Assert.Equal(entries[i].Duration, persisted[i].Duration);
            Assert.Equal(entries[i].Speed, persisted[i].Speed);
            Assert.Equal(entries[i].TransponderId, persisted[i].TransponderId);
            Assert.Equal(entries[i].FacilityId, persisted[i].FacilityId);
        }
    }

    [PostgreSqlStatisticsFact]
    public Task CrossItemSimpleCreationPreservesPendingSimpleAndMultiButWakesIdle() =>
        AssertCreationTransitions(sameItem: false);

    [PostgreSqlStatisticsFact]
    public Task SameItemSimpleCreationPreservesPendingSimpleAndMultiButWakesIdle() =>
        AssertCreationTransitions(sameItem: true);

    private async Task AssertCreationTransitions(bool sameItem)
    {
        var parent = Simple();
        var item = sameItem ? parent.StatisticsItem : new StatisticsItem
            { Id = Guid.NewGuid(), Name = "Dependent", Distance = 500 };
        var multi = new MultiStatisticsItemConfig
        {
            Id = Guid.NewGuid(), StatisticsItem = item, StatisticsItemId = item.Id,
            ParentConfig = parent, ParentConfigId = parent.Id, Repetitions = 2
        };
        await using (var db = Db())
        {
            db.AddRange(parent, multi);
            await db.SaveChangesAsync();
            await new StatisticsValidityScheduler(db).EnsureJobsForAllItemsAsync(CancellationToken.None);
        }
        Assert.False((await Job(item.Id)).MultiRescanPending); // bootstrap database default
        var cursor = Guid.Parse("80000000-0000-0000-0000-000000000123");
        foreach (var state in new[] { "idle", "pendingSimple", "activeMulti" })
        {
            await using (var db = Db())
            {
                var job = await db.Set<StatisticsValidityRevalidation>().SingleAsync(j => j.StatisticsItemId == item.Id);
                job.IsPending = state != "idle";
                job.Phase = state == "pendingSimple"
                    ? StatisticsValidityRevalidationPhase.Simple : StatisticsValidityRevalidationPhase.Multi;
                job.LastEntryId = cursor;
                job.MultiRescanPending = false;
                await db.SaveChangesAsync();
            }
            var before = await Job(item.Id);
            // Matching definitions, not an already existing aggregate, determine scheduling.
            await CreateContainedSimple(parent, new StatisticsEntry
                { TransponderId = Guid.NewGuid(), FacilityId = Guid.NewGuid(), TimeStart = Start });
            var after = await Job(item.Id);
            Assert.Equal(before.Generation + 1, after.Generation);
            Assert.True(after.IsPending);
            Assert.Equal(state == "pendingSimple" ? StatisticsValidityRevalidationPhase.Simple
                : StatisticsValidityRevalidationPhase.Multi, after.Phase);
            Assert.Equal(state == "idle" ? null : (Guid?)cursor, after.LastEntryId);
            Assert.Equal(state == "activeMulti", after.MultiRescanPending);

            // Duplicate IDs are coalesced to one item update, without losing the cursor.
            await Notify(item.Id, item.Id);
            var duplicate = await Job(item.Id);
            Assert.Equal(after.Generation + 1, duplicate.Generation);
            Assert.Equal(after.LastEntryId, duplicate.LastEntryId);
            Assert.Equal(state != "pendingSimple", duplicate.MultiRescanPending);
        }
        // A genuine own-threshold reset must discard any queued Multi-only follow-up.
        Assert.True((await Job(item.Id)).MultiRescanPending);
        Assert.Equal(cursor, (await Job(item.Id)).LastEntryId);
        var generation = (await Job(item.Id)).Generation;
        await Change(item.Id, TimeSpan.FromSeconds(1));
        var reset = await Job(item.Id);
        Assert.Equal(generation + 1, reset.Generation);
        Assert.True(reset.IsPending);
        Assert.Equal(StatisticsValidityRevalidationPhase.Simple, reset.Phase);
        Assert.Null(reset.LastEntryId);
        Assert.False(reset.MultiRescanPending);
        await Drain(item.Id, 2);
        Assert.False((await Job(item.Id)).IsPending);
    }

    [PostgreSqlStatisticsFact]
    public async Task FailedTailCommitRollsBackClassificationAndRescanConsumptionThenRestartRetries()
    {
        var (_, multi, entries) = await SeedMultiSweep();
        await Change(multi.StatisticsItemId, null);
        await Batch(multi.StatisticsItemId, 3); // Simple -> Multi
        await Batch(multi.StatisticsItemId, 3);
        await Batch(multi.StatisticsItemId, 3);
        await Notify(multi.StatisticsItemId);
        var before = await Job(multi.StatisticsItemId);
        Assert.Equal(entries[5].Id, before.LastEntryId);
        Assert.True(before.MultiRescanPending);
        using var services = new ServiceCollection().AddMetrics().AddSingleton<Metrics>().BuildServiceProvider();
        await using (var failing = new StatisticsDbContext(new DbContextOptionsBuilder<StatisticsDbContext>()
            .UseNpgsql(connection).UseSnakeCaseNamingConvention().AddInterceptors(new FailCommit()).Options))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new StatisticsRevalidationService(failing, new(failing),
                services.GetRequiredService<Metrics>(), NullLogger<StatisticsRevalidationService>.Instance)
                .ProcessBatchAsync(multi.StatisticsItemId, 3, CancellationToken.None));
        }
        var rolledBack = await Job(multi.StatisticsItemId);
        Assert.Equal(before.Generation, rolledBack.Generation);
        Assert.Equal(before.LastEntryId, rolledBack.LastEntryId);
        Assert.True(rolledBack.IsPending);
        Assert.True(rolledBack.MultiRescanPending);
        var tailId = entries[^1].Id;
        await using (var db = Db())
            Assert.True((await db.Set<StatisticsEntry>().SingleAsync(e => e.Id == tailId)).IsValid);
        var retried = await Batch(multi.StatisticsItemId, 3);
        Assert.Equal(2, retried.Scanned);
        Assert.Equal(1, retried.Reclassified);
        Assert.Equal(StatisticsRevalidationBatchOutcome.Progressed, retried.Outcome);
        var consumed = await Job(multi.StatisticsItemId);
        Assert.Null(consumed.LastEntryId);
        Assert.True(consumed.IsPending);
        Assert.False(consumed.MultiRescanPending);
        Assert.All(await Drain(multi.StatisticsItemId, 3), b => Assert.Equal(0, b.Reclassified));
        Assert.False((await Job(multi.StatisticsItemId)).IsPending);
    }

    [PostgreSqlStatisticsFact]
    public async Task NotificationWaitingBehindMultiCompletionLockIsNotLostOnIdleOrRescanConsumption()
    {
        var (_, multi, entries) = await SeedMultiSweep();
        await Change(multi.StatisticsItemId, null);
        foreach (var rescanPending in new[] { false, true })
        {
            await using (var db = Db())
            {
                var job = await db.Set<StatisticsValidityRevalidation>().SingleAsync(j => j.StatisticsItemId == multi.StatisticsItemId);
                job.Phase = StatisticsValidityRevalidationPhase.Multi;
                job.IsPending = true;
                job.LastEntryId = entries[^1].Id;
                job.MultiRescanPending = rescanPending;
                await db.SaveChangesAsync();
            }
            var before = await Job(multi.StatisticsItemId);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var gate = new PageGate();
            using var services = new ServiceCollection().AddMetrics().AddSingleton<Metrics>().BuildServiceProvider();
            await using var storage = new StatisticsDbContext(new DbContextOptionsBuilder<StatisticsDbContext>()
                .UseNpgsql(connection).UseSnakeCaseNamingConvention().AddInterceptors(gate).Options);
            var batch = new StatisticsRevalidationService(storage, new(storage), services.GetRequiredService<Metrics>(),
                NullLogger<StatisticsRevalidationService>.Instance)
                .ProcessBatchAsync(multi.StatisticsItemId, 2, timeout.Token);
            await gate.Entered.Task.WaitAsync(timeout.Token);
            var notification = Notify(multi.StatisticsItemId);
            try
            {
                await using var probe = Db();
                while (await probe.Database.SqlQueryRaw<int>("""
                    SELECT count(*)::int AS "Value" FROM pg_stat_activity
                    WHERE datname = current_database() AND wait_event_type = 'Lock'
                    """).SingleAsync(timeout.Token) == 0)
                    await Task.Delay(20, timeout.Token);
                Assert.False(notification.IsCompleted);
            }
            finally { gate.Release.TrySetResult(); }
            Assert.Equal(rescanPending ? StatisticsRevalidationBatchOutcome.Progressed
                : StatisticsRevalidationBatchOutcome.Completed, (await batch).Outcome);
            await notification;
            var after = await Job(multi.StatisticsItemId);
            Assert.Equal(before.Generation + 1, after.Generation);
            Assert.True(after.IsPending);
            Assert.Equal(StatisticsValidityRevalidationPhase.Multi, after.Phase);
            Assert.Null(after.LastEntryId);
            Assert.Equal(rescanPending, after.MultiRescanPending);
            var batches = await Drain(multi.StatisticsItemId, 2);
            Assert.Equal(entries.Length * (rescanPending ? 2 : 1), batches.Sum(b => b.Scanned));
            Assert.False((await Job(multi.StatisticsItemId)).IsPending);
        }
    }

    [PostgreSqlStatisticsFact]
    public async Task SimpleInsertionAndDependentNotificationRollbackTogether()
    {
        var (parent, multi, entries) = await SeedMultiSweep();
        await Change(multi.StatisticsItemId, null);
        await Batch(multi.StatisticsItemId, 2);
        await Batch(multi.StatisticsItemId, 2);
        var before = await Job(multi.StatisticsItemId);
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        await using (var db = Db())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await InsertContainedSimple(db, services.GetRequiredService<HybridCache>(), parent, entries[0]);
            var scheduled = await db.Set<StatisticsValidityRevalidation>().AsNoTracking()
                .SingleAsync(j => j.StatisticsItemId == multi.StatisticsItemId);
            Assert.Equal(before.Generation + 1, scheduled.Generation);
            Assert.True(scheduled.MultiRescanPending);
            await tx.RollbackAsync();
        }
        var after = await Job(multi.StatisticsItemId);
        Assert.Equal(before.Generation, after.Generation);
        Assert.Equal(before.LastEntryId, after.LastEntryId);
        Assert.Equal(before.MultiRescanPending, after.MultiRescanPending);
        await using (var db = Db())
            Assert.Equal(16, await db.Set<StatisticsEntry>().CountAsync(e => e.StatisticsItemId == parent.StatisticsItemId));
        await CreateContainedSimple(parent, entries[0]); // retry using new transaction/context
        Assert.True((await Job(multi.StatisticsItemId)).MultiRescanPending);
        await Drain(multi.StatisticsItemId, 2);
        await using var verify = Db();
        Assert.Equal(17, await verify.Set<StatisticsEntry>().CountAsync(e => e.StatisticsItemId == parent.StatisticsItemId));
        Assert.False((await verify.Set<StatisticsEntry>().SingleAsync(e => e.Id == entries[0].Id)).IsValid);
    }

    [PostgreSqlStatisticsFact]
    public async Task CoalescingMigrationAddsFalseDefaultWithoutResettingActiveOrIdleCheckpoints()
    {
        var upgradeDatabase = $"coalescing_upgrade_{Guid.NewGuid():N}";
        await Sql(admin, $"CREATE DATABASE \"{upgradeDatabase}\"");
        var upgradeConnection = new NpgsqlConnectionStringBuilder(admin) { Database = upgradeDatabase }.ConnectionString;
        try
        {
            await using var db = new StatisticsDbContext(new DbContextOptionsBuilder<StatisticsDbContext>()
                .UseNpgsql(upgradeConnection, o => o.MigrationsHistoryTable("__ef_migrations_history", "ef"))
                .UseSnakeCaseNamingConvention().Options);
            await db.GetService<IMigrator>().MigrateAsync("20261104085927_statistics_remove_contributor_lineage");
            var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            var cursor = Guid.NewGuid();
            for (var i = 0; i < ids.Length; i++)
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO statistics.statistics_item (id, name, description, distance)
                    VALUES ({ids[i]}, 'Existing', '', 250);
                    INSERT INTO statistics.statistics_validity_revalidation
                    (statistics_item_id, generation, is_pending, phase, last_entry_id)
                    VALUES ({ids[i]}, {7L + i}, {i != 2}, {(i == 0 ? 0 : 1)}, {cursor});
                    """);
            await db.Database.MigrateAsync();
            for (var i = 0; i < ids.Length; i++)
            {
                var job = await db.Set<StatisticsValidityRevalidation>().AsNoTracking()
                    .SingleAsync(j => j.StatisticsItemId == ids[i]);
                Assert.Equal(7L + i, job.Generation);
                Assert.Equal(i != 2, job.IsPending);
                Assert.Equal(i == 0 ? StatisticsValidityRevalidationPhase.Simple
                    : StatisticsValidityRevalidationPhase.Multi, job.Phase);
                Assert.Equal(cursor, job.LastEntryId);
                Assert.False(job.MultiRescanPending);
            }
            Assert.Equal(0, await new StatisticsValidityScheduler(db).EnsureJobsForAllItemsAsync(CancellationToken.None));
            await db.Database.MigrateAsync(); // idempotent, no metadata reset
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally
        {
            using var pool = new NpgsqlConnection(upgradeConnection);
            NpgsqlConnection.ClearPool(pool);
            await Sql(admin, $"DROP DATABASE \"{upgradeDatabase}\" WITH (FORCE)");
        }
    }

    private async Task<(SimpleStatisticsItemConfig Parent, MultiStatisticsItemConfig Multi, StatisticsEntry[] Entries)> SeedMultiSweep()
    {
        var parent = Simple();
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Double", Distance = 500 };
        var multi = new MultiStatisticsItemConfig
        {
            Id = Guid.NewGuid(), StatisticsItem = item, StatisticsItemId = item.Id,
            ParentConfig = parent, ParentConfigId = parent.Id, Repetitions = 2
        };
        await using var db = Db();
        db.AddRange(parent, multi);
        var entries = new List<StatisticsEntry>();
        for (var i = 0; i < 8; i++)
        {
            var rider = Guid.NewGuid();
            var facility = Guid.NewGuid();
            var first = Entry(parent, i * 2 + 1, rider, facility, Start, Start.AddSeconds(10));
            var second = Entry(parent, i * 2 + 2, rider, facility, Start.AddSeconds(10), Start.AddSeconds(20));
            if (i == 7)
            {
                second.IsValid = false;
                second.InvalidReason = StatisticsEntryInvalidReason.BelowMinimumDuration;
            }
            db.AddRange(first, second);
            var aggregate = new StatisticsEntry
            {
                // Explicit UUIDs ordered identically by PostgreSQL, independent of Guid.NewGuid.
                Id = Guid.Parse($"90000000-0000-0000-0000-{i + 1:D12}"),
                StatisticsItemId = item.Id, StatisticsItemConfigId = multi.Id,
                TransponderId = rider, FacilityId = facility, TimeStart = Start,
                TimeEnd = Start.AddSeconds(20), Duration = TimeSpan.FromSeconds(20), Speed = 25
            };
            db.Add(aggregate);
            entries.Add(aggregate);
        }
        await db.SaveChangesAsync();
        return (parent, multi, entries.ToArray());
    }

    private async Task Notify(params Guid[] items)
    {
        await using var db = Db();
        await using var tx = await db.Database.BeginTransactionAsync();
        Assert.Equal(items.Distinct().Count(),
            await new StatisticsValidityScheduler(db).ScheduleDependentMultiRevalidationAsync(items));
        await tx.CommitAsync();
    }

    private async Task CreateContainedSimple(SimpleStatisticsItemConfig parent, StatisticsEntry aggregate)
    {
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        await using var db = Db();
        await using var tx = await db.Database.BeginTransactionAsync();
        await InsertContainedSimple(db, services.GetRequiredService<HybridCache>(), parent, aggregate);
        await tx.CommitAsync();
    }

    private static async Task InsertContainedSimple(StatisticsDbContext db, HybridCache cache,
        SimpleStatisticsItemConfig parent, StatisticsEntry aggregate)
    {
        var start = aggregate.TimeStart.AddSeconds(2);
        db.Add(new Sample
        {
            Id = Guid.NewGuid(), TransponderId = aggregate.TransponderId, FacilityId = aggregate.FacilityId,
            CoursePointStartId = parent.CoursePointStart, CoursePointEndId = Guid.NewGuid(),
            TimeStart = start, TimeEnd = start.AddSeconds(1), Duration = TimeSpan.FromSeconds(1),
            Distance = 250, Speed = 250
        });
        await db.SaveChangesAsync();
        var entry = Assert.Single(await new StatisticsService(db, cache).ProcessCompletedAsync(
            aggregate.TransponderId, start.AddSeconds(2), start.AddSeconds(10),
            parent.CoursePointStart, parent.CoursePointEnd));
        Assert.True(entry.IsValid);
        Assert.Equal(start, entry.TimeStart);
        Assert.Equal(start.AddSeconds(10), entry.TimeEnd);
    }
}
