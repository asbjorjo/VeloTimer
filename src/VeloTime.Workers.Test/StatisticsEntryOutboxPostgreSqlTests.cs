using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SlimMessageBus;
using Npgsql;
using VeloTime.Module.Statistics;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    [PostgreSqlStatisticsFact]
    public async Task EntryOutboxAtomicMultipleOutputsConcurrentRedeliveryAndOwnershipRollback()
    {
        var first = Simple();
        var second = Simple();
        second.CoursePointStart = first.CoursePointStart;
        second.CoursePointEnd = first.CoursePointEnd;
        var multiA = ReplayMulti(first);
        var multiB = ReplayMulti(first);
        var rider = Guid.NewGuid();
        var sample = CompletionSample(first, rider);
        var source = new SampleCompleteOutbox
        {
            SampleId = sample.Id, TransponderId = rider, TimeStart = sample.TimeStart, TimeEnd = sample.TimeEnd,
            CoursePointStart = first.CoursePointStart, CoursePointEnd = first.CoursePointEnd, Distance = 250,
            DeliveredAtUtc = DateTime.UtcNow
        };
        var message = new SampleComplete(rider, sample.TimeStart, sample.TimeEnd,
            first.CoursePointStart, first.CoursePointEnd, 250);
        await using var services = new ServiceCollection().AddMetrics().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        var reader = new StatisticsOwnershipTests.Reader
        {
            Owners = [new(Guid.NewGuid(), rider, Guid.NewGuid(), new DateTimeOffset(Start.AddDays(-1)), null)]
        };
        SampleCompleteHandler Handler(StatisticsDbContext db) => new(new(db, cache), new(db, reader),
            new Metrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()),
            NullLogger<SampleCompleteHandler>.Instance);
        await using (var seed = Db())
        {
            seed.AddRange(first, second, multiA, multiB, sample, source);
            await seed.SaveChangesAsync();
            await seed.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION statistics.reject_entry_outbox() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'injected entry outbox failure'; END $$;
                CREATE TRIGGER reject_entry_outbox BEFORE INSERT ON statistics.entry_created_outbox
                    FOR EACH ROW EXECUTE FUNCTION statistics.reject_entry_outbox();
                """);
        }
        await using (var failed = Db())
            await Assert.ThrowsAsync<DbUpdateException>(() => Handler(failed).OnHandle(message, CancellationToken.None));
        await using (var verify = Db())
        {
            Assert.Empty(await verify.Set<StatisticsEntry>().ToListAsync());
            Assert.Empty(await verify.Set<StatsProfile>().ToListAsync());
            Assert.Empty(await verify.Set<EntryCreatedOutbox>().ToListAsync());
            Assert.Null((await verify.Set<SampleCompleteOutbox>().SingleAsync()).EntriesCreatedAtUtc);
            await verify.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_entry_outbox ON statistics.entry_created_outbox");
        }
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = Db();
            await Handler(db).OnHandle(message, CancellationToken.None);
        }));
        await using (var verify = Db())
        {
            Assert.Equal(2, await verify.Set<StatisticsEntry>().CountAsync());
            Assert.All(await verify.Set<StatisticsEntry>().ToListAsync(), e => Assert.Equal(reader.Owners[0].OwnerId, e.StatsProfileUserId));
            Assert.Equal(2, await verify.Set<EntryCreatedOutbox>().CountAsync());
            Assert.NotNull((await verify.Set<SampleCompleteOutbox>().SingleAsync()).EntriesCreatedAtUtc);
        }
        var parent = new EntryCreated(rider, Start, Start.AddSeconds(10), first.StatisticsItemId, first.Id);
        EntryCreatedHandler ParentHandler(StatisticsDbContext db) => new(new(db, cache), new(db, reader),
            NullLogger<EntryCreatedHandler>.Instance);
        // The two fixed results can be allocated in either order. If the unrelated fixed
        // result was first, process its empty Multi set before the tested parent.
        await using (var earlier = Db())
        {
            var receipt = await earlier.Set<EntryCreatedOutbox>().OrderBy(r => r.Id).FirstAsync();
            if (receipt.ConfigItemId != first.Id)
                await ParentHandler(earlier).OnHandle(new EntryCreated(receipt.TransponderId,
                    receipt.TimeStart, receipt.TimeEnd, receipt.StatisticsItemId, receipt.ConfigItemId,
                    receipt.EntryId), CancellationToken.None);
        }
        await using (var seed = Db())
            await seed.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER reject_entry_outbox BEFORE INSERT ON statistics.entry_created_outbox
                    FOR EACH ROW EXECUTE FUNCTION statistics.reject_entry_outbox();
                """);
        await using (var failed = Db())
            await Assert.ThrowsAsync<DbUpdateException>(() => ParentHandler(failed).OnHandle(parent, CancellationToken.None));
        await using (var verify = Db())
        {
            Assert.Equal(2, await verify.Set<StatisticsEntry>().CountAsync());
            Assert.Equal(2, await verify.Set<EntryCreatedOutbox>().CountAsync());
            await verify.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_entry_outbox ON statistics.entry_created_outbox");
        }
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = Db();
            await ParentHandler(db).OnHandle(parent, CancellationToken.None);
        }));
        // The unrelated fixed receipt may be later in local order and still needs
        // its empty decision set before its successors can be dispatched.
        await using (var remaining = Db())
        {
            var receipt = await remaining.Set<EntryCreatedOutbox>()
                .SingleAsync(r => r.ConfigItemId == second.Id && r.Single);
            await ParentHandler(remaining).OnHandle(new EntryCreated(receipt.TransponderId,
                receipt.TimeStart, receipt.TimeEnd, receipt.StatisticsItemId, receipt.ConfigItemId,
                receipt.EntryId), CancellationToken.None);
        }
        await using (var verify = Db())
        {
            Assert.Equal(4, await verify.Set<StatisticsEntry>().CountAsync());
            Assert.Equal(4, await verify.Set<EntryCreatedOutbox>().CountAsync());
            Assert.Equal(2, await verify.Set<EntryCreatedOutbox>().CountAsync(o => !o.Single));
        }
        var bus = DispatchProxy.Create<IMessageBus, EntryOutboxBus>();
        await using (var delivery = Db())
            while (await new EntryCreatedOutboxDispatcher(delivery, bus).DispatchOneAsync(CancellationToken.None)) { }
        await using (var replay = Db())
        {
            await Handler(replay).OnHandle(message, CancellationToken.None);
            await ParentHandler(replay).OnHandle(parent, CancellationToken.None);
            Assert.Equal(4, await replay.Set<EntryCreatedOutbox>().CountAsync());
            Assert.True(await replay.Set<EntryCreatedOutbox>().AllAsync(o => o.DeliveredAtUtc != null));
        }
    }

    [PostgreSqlStatisticsFact]
    public async Task EntryOutboxMigrationPreservesUnknownHistoryAndGuardsRollback()
    {
        const string previous = "20261123135436_statistics_sample_complete_outbox";
        var entryId = Guid.NewGuid();
        await RecreateAtMigrationAsync(previous);
        await using (var db = Db())
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE statistics.smb_outbox (sentinel integer PRIMARY KEY);
                INSERT INTO statistics.smb_outbox VALUES (42);
                INSERT INTO statistics.sample_complete_outbox
                    (sample_id, transponder_id, time_start, time_end, course_point_start, course_point_end, distance, delivered_at_utc)
                VALUES (gen_random_uuid(), gen_random_uuid(), now(), now(), gen_random_uuid(), gen_random_uuid(), 1, now());
                """);
        }
        await using (var db = Db())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261123145848_statistics_entry_created_outbox");
            var old = await db.Set<SampleCompleteOutbox>().SingleAsync();
            Assert.NotNull(old.DeliveredAtUtc);
            Assert.Null(old.EntriesCreatedAtUtc); // Never guess historical processing from acknowledgement.
            Assert.Equal(42, await db.Database.SqlQueryRaw<int>("SELECT sentinel AS \"Value\" FROM statistics.smb_outbox").SingleAsync());
            // The current EF entity includes columns introduced after this historical target.
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO statistics.entry_created_outbox
                    (entry_id, transponder_id, time_start, time_end, statistics_item_id, config_item_id, single)
                VALUES ({entryId}, {Guid.NewGuid()}, {Start}, {Start.AddSeconds(1)}, {Guid.NewGuid()}, {Guid.NewGuid()}, TRUE)
                """);
        }
        foreach (var delivered in new[] { false, true })
        {
            await using var db = Db();
            if (delivered)
                await db.Database.ExecuteSqlAsync($"UPDATE statistics.entry_created_outbox SET delivered_at_utc = {DateTime.UtcNow} WHERE entry_id = {entryId}");
            var exception = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(previous));
            Assert.Contains("Cannot drop EntryCreated outbox", exception.MessageText);
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(delivered
                ? """SELECT count(*)::int AS "Value" FROM statistics.entry_created_outbox WHERE delivered_at_utc IS NOT NULL"""
                : """SELECT count(*)::int AS "Value" FROM statistics.entry_created_outbox WHERE delivered_at_utc IS NULL""").SingleAsync());
        }
    }

    [PostgreSqlStatisticsFact]
    public async Task MultiDecisionMigrationUpgradesLegacyReceiptAndGuardsRetainedDecisions()
    {
        await RecreateAtMigrationAsync(BeforeSampleActivation);
        await using var db = Db();
        await AssertMigrationsPendingAfterEntryOutboxAsync(db);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET velotime.statistics_sample_reset = 'approved'");
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(SampleActivationMigration);
        Assert.Equal(new[] { MultiDecisionMigration }, await db.Database.GetPendingMigrationsAsync());

        var entryId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO statistics.entry_created_outbox
                (entry_id, transponder_id, time_start, time_end, statistics_item_id, config_item_id, single)
            VALUES ({entryId}, {Guid.NewGuid()}, {Start}, {Start.AddSeconds(1)}, {Guid.NewGuid()}, {Guid.NewGuid()}, TRUE)
            """);
        await migrator.MigrateAsync(MultiDecisionMigration);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM statistics.entry_created_outbox
            WHERE multi_decision_version = 0 AND multi_decisions_created_at_utc IS NULL
            """).SingleAsync());
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO statistics.multi_creation_decision (parent_entry_id, multi_config_id, result_entry_id)
            VALUES ({entryId}, {configId}, NULL)
            """);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(SampleActivationMigration));
        Assert.Contains("Cannot remove retained Multi decisions", failure.MessageText);
        Assert.Contains(MultiDecisionMigration, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM statistics.multi_creation_decision WHERE result_entry_id IS NULL
            """).SingleAsync());
        await db.Database.ExecuteSqlRawAsync("DELETE FROM statistics.multi_creation_decision");
        await migrator.MigrateAsync(SampleActivationMigration);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM statistics.entry_created_outbox
            """).SingleAsync());
        await migrator.MigrateAsync(MultiDecisionMigration);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM statistics.entry_created_outbox
            WHERE multi_decision_version = 0 AND multi_decisions_created_at_utc IS NULL
            """).SingleAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [PostgreSqlStatisticsFact]
    public async Task EntryOutboxWorkerRestartAndFailureAfterAcknowledgementKeepExactPayload()
    {
        var row = PendingEntry(Guid.NewGuid());
        await using (var seed = Db())
        {
            // No FK to entries/configs: deletion/rerouting cannot erase pending payloads.
            seed.Add(row);
            await seed.SaveChangesAsync();
            await seed.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION statistics.reject_entry_delivery() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'injected delivery mark failure'; END $$;
                CREATE TRIGGER reject_entry_delivery BEFORE UPDATE ON statistics.entry_created_outbox
                    FOR EACH ROW EXECUTE FUNCTION statistics.reject_entry_delivery();
                """);
        }
        var bus = DispatchProxy.Create<IMessageBus, EntryOutboxBus>();
        var proxy = (EntryOutboxBus)(object)bus;
        await using (var failed = Db())
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                new EntryCreatedOutboxDispatcher(failed, bus).DispatchOneAsync(CancellationToken.None));
        await using (var verify = Db())
        {
            Assert.Null((await verify.Set<EntryCreatedOutbox>().SingleAsync()).DeliveredAtUtc);
            await verify.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_entry_delivery ON statistics.entry_created_outbox");
        }

        await using var provider = new ServiceCollection().AddScoped(_ => Db()).AddSingleton(bus)
            .AddScoped<EntryCreatedOutboxDispatcher>().BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        proxy.Fail = true;
        using (var worker = new EntryCreatedOutboxWorker(scopes, NullLogger<EntryCreatedOutboxWorker>.Instance))
        {
            await worker.StartAsync(CancellationToken.None);
            await WaitFor(() => Task.FromResult(proxy.Messages.Count >= 2));
            await worker.StopAsync(CancellationToken.None);
        }
        proxy.Fail = false;
        using (var restarted = new EntryCreatedOutboxWorker(scopes, NullLogger<EntryCreatedOutboxWorker>.Instance))
        {
            await restarted.StartAsync(CancellationToken.None);
            await WaitFor(async () =>
            {
                await using var db = Db();
                return await db.Set<EntryCreatedOutbox>().AllAsync(o => o.DeliveredAtUtc != null);
            });
            await restarted.StopAsync(CancellationToken.None);
        }
        Assert.Equal(3, proxy.Messages.Count);
        Assert.Single(proxy.Messages.Distinct());
        Assert.All(proxy.Headers, h => Assert.Equal(true, h!["Single"]));
        await using var empty = Db();
        Assert.False(await new EntryCreatedOutboxDispatcher(empty, bus).DispatchOneAsync(CancellationToken.None));
    }

    [PostgreSqlStatisticsFact]
    public async Task EntryOutboxCannotPublishBeforeCommitAndCancelledSendRemainsPending()
    {
        var bus = DispatchProxy.Create<IMessageBus, EntryOutboxBus>();
        var proxy = (EntryOutboxBus)(object)bus;
        await using (var creation = Db())
        {
            await using var tx = await creation.Database.BeginTransactionAsync();
            creation.Add(PendingEntry(Guid.NewGuid()));
            await creation.SaveChangesAsync();
            await using var dispatcher = Db();
            Assert.False(await new EntryCreatedOutboxDispatcher(dispatcher, bus).DispatchOneAsync(CancellationToken.None));
            Assert.Empty(proxy.Messages);
            await tx.RollbackAsync();
        }
        await using (var seed = Db())
        {
            Assert.Empty(await seed.Set<EntryCreatedOutbox>().ToListAsync());
            seed.Add(PendingEntry(Guid.NewGuid()));
            await seed.SaveChangesAsync();
        }
        proxy.BlockFirst = true;
        using var cancelled = new CancellationTokenSource();
        await using (var dispatch = Db())
        {
            var sending = new EntryCreatedOutboxDispatcher(dispatch, bus).DispatchOneAsync(cancelled.Token);
            await proxy.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        }
        await using var retry = Db();
        Assert.Null((await retry.Set<EntryCreatedOutbox>().SingleAsync()).DeliveredAtUtc);
        Assert.True(await new EntryCreatedOutboxDispatcher(retry, bus).DispatchOneAsync(CancellationToken.None));
        Assert.Equal(2, proxy.Messages.Count);
        Assert.Single(proxy.Messages.Distinct());
    }

    [PostgreSqlStatisticsFact]
    public async Task EntryOutboxTwoReplicasPreserveRiderSequenceWhileOtherRiderProgresses()
    {
        var rider = Guid.NewGuid();
        var other = Guid.NewGuid();
        await using (var seed = Db())
        {
            foreach (var id in new[] { rider, rider, other })
            {
                seed.Add(PendingEntry(id));
                await seed.SaveChangesAsync();
            }
        }
        var bus = DispatchProxy.Create<IMessageBus, EntryOutboxBus>();
        var proxy = (EntryOutboxBus)(object)bus;
        proxy.BlockFirst = true;
        await using var a = Db();
        await using var b = Db();
        var first = new EntryCreatedOutboxDispatcher(a, bus).DispatchOneAsync(CancellationToken.None);
        await proxy.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(await new EntryCreatedOutboxDispatcher(b, bus).DispatchOneAsync(CancellationToken.None));
        Assert.Equal(new[] { rider, other }, proxy.Messages.Select(m => m.TransponderId));
        proxy.Release.SetResult();
        Assert.True(await first);
        await using (var blocked = Db())
            Assert.False(await new EntryCreatedOutboxDispatcher(blocked, bus).DispatchOneAsync(CancellationToken.None));
        await using (var processed = Db())
        {
            var receipt = await processed.Set<EntryCreatedOutbox>().OrderBy(o => o.Id).FirstAsync();
            receipt.MultiDecisionsCreatedAtUtc = DateTime.UtcNow;
            await processed.SaveChangesAsync();
        }
        await using var c = Db();
        Assert.True(await new EntryCreatedOutboxDispatcher(c, bus).DispatchOneAsync(CancellationToken.None));
        Assert.Equal(new[] { rider, other, rider }, proxy.Messages.Select(m => m.TransponderId));
    }

    [PostgreSqlStatisticsFact]
    public async Task SampleOutboxWaitsForEntryCreationEvenForZeroOutputs()
    {
        var rider = Guid.NewGuid();
        var point = Guid.NewGuid();
        await using (var seed = Db())
        {
            for (var i = 0; i < 2; i++)
            {
                var sampleId = Guid.NewGuid();
                seed.Add(new Sample
                {
                    Id = sampleId, TransponderId = rider, FacilityId = Guid.NewGuid(),
                    TimeStart = Start.AddSeconds(i), TimeEnd = Start.AddSeconds(i + 1),
                    CoursePointStartId = point, CoursePointEndId = point,
                    Duration = TimeSpan.FromSeconds(1), Distance = 0, Speed = 0
                });
                seed.Add(new SampleCompleteOutbox
                {
                    SampleId = sampleId, TransponderId = rider, TimeStart = Start.AddSeconds(i),
                    TimeEnd = Start.AddSeconds(i + 1), CoursePointStart = point, CoursePointEnd = point
                });
                await seed.SaveChangesAsync();
            }
        }
        var bus = DispatchProxy.Create<IMessageBus, OutboxBus>();
        ((OutboxBus)(object)bus).Fail = false;
        await using (var db = Db())
            Assert.True(await new SampleCompleteOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None));
        await using (var db = Db())
            Assert.False(await new SampleCompleteOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None));
        await using var services = new ServiceCollection().AddMetrics().AddHybridCache().Services.BuildServiceProvider();
        await using (var db = Db())
        {
            await new SampleCompleteHandler(new(db, services.GetRequiredService<HybridCache>()),
                new(db, new RequiredPointEmptyOwnershipReader()),
                new Metrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()),
                NullLogger<SampleCompleteHandler>.Instance)
                .OnHandle(new SampleComplete(rider, Start, Start.AddSeconds(1), point, point, 0), CancellationToken.None);
            Assert.Empty(await db.Set<EntryCreatedOutbox>().ToListAsync());
        }
        await using (var db = Db())
            Assert.True(await new SampleCompleteOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None));
    }

    private static EntryCreatedOutbox PendingEntry(Guid rider) => new()
    {
        EntryId = Guid.NewGuid(), TransponderId = rider, TimeStart = Start, TimeEnd = Start.AddSeconds(10),
        StatisticsItemId = Guid.NewGuid(), ConfigItemId = Guid.NewGuid(), Single = true, MultiDecisionVersion = 1
    };

    public class EntryOutboxBus : DispatchProxy
    {
        public volatile bool Fail;
        public bool BlockFirst;
        public ConcurrentQueue<EntryCreated> Messages { get; } = new();
        public ConcurrentQueue<IDictionary<string, object>?> Headers { get; } = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Assert.Equal("Publish", method?.Name);
            Messages.Enqueue(Assert.IsType<EntryCreated>(args![0]));
            Headers.Enqueue(args.OfType<IDictionary<string, object>>().SingleOrDefault());
            if (BlockFirst && Messages.Count == 1)
            {
                Started.SetResult();
                return Release.Task.WaitAsync(args.OfType<CancellationToken>().Single());
            }
            return Fail ? Task.FromException(new InvalidOperationException("broker unavailable/ack uncertain")) : Task.CompletedTask;
        }
    }
}
