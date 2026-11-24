using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SlimMessageBus;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    [PostgreSqlStatisticsFact]
    public async Task EntryDispatchWaitsForCommittedEmptyDecisionAndAllowsOtherRiders()
    {
        var config = Simple();
        var rider = Guid.NewGuid();
        var first = DecisionParent(config, rider, Guid.NewGuid(), Start, Start.AddSeconds(10));
        var second = DecisionParent(config, rider, first.FacilityId, Start, Start.AddSeconds(10));
        var other = PendingEntry(Guid.NewGuid());
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        await using (var seed = Db())
        {
            seed.AddRange(config, first, second);
            await seed.SaveChangesAsync();
            await DecisionReceipt(seed, first);
            await DecisionReceipt(seed, second);
            // Make the two source receipts pending in creation order.
            await seed.Database.ExecuteSqlRawAsync("UPDATE statistics.entry_created_outbox SET delivered_at_utc = NULL");
            seed.Add(other);
            await seed.SaveChangesAsync();
        }
        var bus = DispatchProxy.Create<IMessageBus, EntryOutboxBus>();
        var proxy = (EntryOutboxBus)(object)bus;
        async Task<bool> Dispatch()
        {
            await using var db = Db();
            return await new EntryCreatedOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None);
        }
        Assert.True(await Dispatch());
        Assert.Equal(first.Id, proxy.Messages.First().EntryId);
        Assert.True(await Dispatch());
        Assert.Equal(other.EntryId, proxy.Messages.Last().EntryId);
        Assert.False(await Dispatch());

        // An uncommitted marker, including one rolled back after a crash, never releases the barrier.
        await using (var writer = Db())
        {
            await using var tx = await writer.Database.BeginTransactionAsync();
            var row = await writer.Set<EntryCreatedOutbox>().SingleAsync(o => o.EntryId == first.Id);
            row.MultiDecisionsCreatedAtUtc = DateTime.UtcNow;
            await writer.SaveChangesAsync();
            Assert.False(await Dispatch());
            await tx.RollbackAsync();
        }
        Assert.False(await Dispatch());
        await using (var processing = Db())
            await DecisionHandler(processing, services.GetRequiredService<HybridCache>())
                .OnHandle(DecisionMessage(first), CancellationToken.None);
        await using (var verify = Db())
        {
            Assert.Empty(await verify.Set<MultiCreationDecision>().ToListAsync());
            Assert.NotNull((await verify.Set<EntryCreatedOutbox>().SingleAsync(o => o.EntryId == first.Id))
                .MultiDecisionsCreatedAtUtc);
        }
        Assert.True(await Dispatch());
        Assert.Equal(second.Id, proxy.Messages.Last().EntryId);
    }

    [PostgreSqlStatisticsFact]
    public async Task EntryDispatchRetriesUncertainAckBeforeLaterReceiptEvenIfDecisionCommitted()
    {
        var rider = Guid.NewGuid();
        var first = PendingEntry(rider);
        var second = PendingEntry(rider);
        await using (var db = Db())
        {
            db.Add(first);
            await db.SaveChangesAsync();
            db.Add(second);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION statistics.reject_entry_delivery() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'injected commit boundary failure'; END $$;
                CREATE CONSTRAINT TRIGGER reject_entry_delivery AFTER UPDATE ON statistics.entry_created_outbox
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
                    WHEN (NEW.delivered_at_utc IS NOT NULL AND OLD.delivered_at_utc IS NULL)
                    EXECUTE FUNCTION statistics.reject_entry_delivery();
                """);
        }
        var bus = DispatchProxy.Create<IMessageBus, EntryOutboxBus>();
        var proxy = (EntryOutboxBus)(object)bus;
        await using (var db = Db())
            await Assert.ThrowsAsync<PostgresException>(() =>
                new EntryCreatedOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None));
        await using (var db = Db())
        {
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_entry_delivery ON statistics.entry_created_outbox");
            (await db.Set<EntryCreatedOutbox>().SingleAsync(o => o.EntryId == first.EntryId))
                .MultiDecisionsCreatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        await using (var db = Db())
            Assert.True(await new EntryCreatedOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None));
        await using (var db = Db())
            Assert.True(await new EntryCreatedOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None));
        Assert.Equal(new Guid?[] { first.EntryId, first.EntryId, second.EntryId }, proxy.Messages.Select(m => m.EntryId));
    }

    [PostgreSqlStatisticsFact]
    public async Task EntryDispatchLegacyAndPoisonBlockOnlyTheirRiderAndNonSingleDoesNotRequireDecision()
    {
        var legacyRider = Guid.NewGuid();
        var poisonRider = Guid.NewGuid();
        var multiRider = Guid.NewGuid();
        var legacy = PendingEntry(legacyRider);
        var poison = PendingEntry(poisonRider);
        var afterPoison = PendingEntry(poisonRider);
        var afterLegacy = PendingEntry(legacyRider);
        var multi = PendingEntry(multiRider);
        var afterMulti = PendingEntry(multiRider);
        await using (var db = Db())
        {
            // Explicit old-writer row; delivered state does not establish a decision.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO statistics.entry_created_outbox
                  (entry_id, transponder_id, time_start, time_end, statistics_item_id, config_item_id, single, delivered_at_utc)
                VALUES ({legacy.EntryId}, {legacyRider}, {Start}, {Start.AddSeconds(10)},
                  {legacy.StatisticsItemId}, {legacy.ConfigItemId}, TRUE, {DateTime.UtcNow})
                """);
            db.Add(afterLegacy);
            await db.SaveChangesAsync();
            db.Add(poison);
            await db.SaveChangesAsync();
            db.Add(afterPoison);
            await db.SaveChangesAsync();
            db.Add(new EntryCreatedOutbox
            {
                EntryId = multi.EntryId, TransponderId = multiRider, TimeStart = multi.TimeStart,
                TimeEnd = multi.TimeEnd, StatisticsItemId = multi.StatisticsItemId,
                ConfigItemId = multi.ConfigItemId, Single = false, MultiDecisionVersion = 1
            });
            await db.SaveChangesAsync();
            db.Add(afterMulti);
            await db.SaveChangesAsync();
        }
        var bus = DispatchProxy.Create<IMessageBus, EntryOutboxBus>();
        var proxy = (EntryOutboxBus)(object)bus;
        async Task<bool> Dispatch()
        {
            await using var db = Db();
            return await new EntryCreatedOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None);
        }
        Assert.True(await Dispatch()); // poison delivered, but processing keeps failing / marker absent
        Assert.True(await Dispatch()); // Multi is not a Single decision source
        Assert.True(await Dispatch()); // its successor may progress
        Assert.False(await Dispatch());
        Assert.Equal(new Guid?[] { poison.EntryId, multi.EntryId, afterMulti.EntryId },
            proxy.Messages.Select(m => m.EntryId));
        Assert.Null(proxy.Headers.ElementAt(1));
        await using var verify = Db();
        Assert.Null((await verify.Set<EntryCreatedOutbox>().SingleAsync(o => o.EntryId == afterLegacy.EntryId)).DeliveredAtUtc);
        Assert.Null((await verify.Set<EntryCreatedOutbox>().SingleAsync(o => o.EntryId == afterPoison.EntryId)).DeliveredAtUtc);
    }

    [PostgreSqlStatisticsFact]
    public async Task EntryDispatchWaitsWhilePoisonDecisionRollsBackAndResumesAfterRetry()
    {
        var config = Simple();
        var multi = ReplayMulti(config);
        multi.Repetitions = 2;
        var rider = Guid.NewGuid();
        var first = DecisionParent(config, rider, Guid.NewGuid(), Start, Start.AddSeconds(10));
        var second = DecisionParent(config, rider, first.FacilityId, Start, Start.AddSeconds(10));
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        await using (var db = Db())
        {
            db.AddRange(config, multi, first, second);
            await db.SaveChangesAsync();
            await DecisionReceipt(db, first);
            await DecisionReceipt(db, second);
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE statistics.entry_created_outbox SET delivered_at_utc = NULL;
                CREATE FUNCTION statistics.reject_multi_decision() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'injected decision failure'; END $$;
                CREATE TRIGGER reject_multi_decision BEFORE INSERT ON statistics.multi_creation_decision
                    FOR EACH ROW EXECUTE FUNCTION statistics.reject_multi_decision();
                """);
        }
        var bus = DispatchProxy.Create<IMessageBus, EntryOutboxBus>();
        await using (var db = Db())
            Assert.True(await new EntryCreatedOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using (var db = Db())
                await Assert.ThrowsAsync<DbUpdateException>(() =>
                    DecisionHandler(db, cache).OnHandle(DecisionMessage(first), CancellationToken.None));
            await using (var db = Db())
                Assert.False(await new EntryCreatedOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None));
        }
        await using (var db = Db())
        {
            Assert.Empty(await db.Set<MultiCreationDecision>().ToListAsync());
            Assert.Null((await db.Set<EntryCreatedOutbox>().SingleAsync(o => o.EntryId == first.Id))
                .MultiDecisionsCreatedAtUtc);
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_multi_decision ON statistics.multi_creation_decision");
        }
        await using (var db = Db())
            await DecisionHandler(db, cache).OnHandle(DecisionMessage(first), CancellationToken.None);
        await using (var db = Db())
            Assert.True(await new EntryCreatedOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None));
        await using (var db = Db())
        {
            var decision = Assert.Single(await db.Set<MultiCreationDecision>().ToListAsync());
            Assert.Null(decision.ResultEntryId);
        }
        Assert.Equal(new Guid?[] { first.Id, second.Id },
            ((EntryOutboxBus)(object)bus).Messages.Select(m => m.EntryId));
    }

    [PostgreSqlStatisticsFact]
    public async Task EntryDispatchDoesNotSendPendingLegacyReceiptOrItsSuccessor()
    {
        var rider = Guid.NewGuid();
        var legacy = PendingEntry(rider);
        var successor = PendingEntry(rider);
        var other = PendingEntry(Guid.NewGuid());
        await using (var db = Db())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO statistics.entry_created_outbox
                  (entry_id, transponder_id, time_start, time_end, statistics_item_id, config_item_id, single)
                VALUES ({legacy.EntryId}, {rider}, {Start}, {Start.AddSeconds(10)},
                  {legacy.StatisticsItemId}, {legacy.ConfigItemId}, FALSE)
                """);
            db.Add(successor);
            await db.SaveChangesAsync();
            db.Add(other);
            await db.SaveChangesAsync();
        }
        var bus = DispatchProxy.Create<IMessageBus, EntryOutboxBus>();
        await using (var db = Db())
            Assert.True(await new EntryCreatedOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None));
        await using (var db = Db())
            Assert.False(await new EntryCreatedOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None));
        Assert.Equal(other.EntryId, Assert.Single(((EntryOutboxBus)(object)bus).Messages).EntryId);
        await using var verify = Db();
        Assert.Equal(2, await verify.Set<EntryCreatedOutbox>().CountAsync(o => o.DeliveredAtUtc == null));
    }
}
