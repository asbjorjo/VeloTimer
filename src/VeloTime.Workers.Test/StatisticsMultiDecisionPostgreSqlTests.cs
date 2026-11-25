using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    private static StatisticsEntry DecisionParent(SimpleStatisticsItemConfig config, Guid rider, Guid facility,
        DateTime start, DateTime end) => new()
    {
        Id = Guid.NewGuid(), TransponderId = rider, FacilityId = facility,
        StatisticsItemId = config.StatisticsItemId, StatisticsItemConfigId = config.Id,
        TimeStart = start, TimeEnd = end, Duration = end - start, Speed = 25
    };

    private static async Task DecisionReceipt(StatisticsDbContext db, StatisticsEntry entry, int version = 1)
    {
        db.Add(new EntryCreatedOutbox
        {
            EntryId = entry.Id, TransponderId = entry.TransponderId, StatisticsItemId = entry.StatisticsItemId,
            ConfigItemId = entry.StatisticsItemConfigId, TimeStart = entry.TimeStart, TimeEnd = entry.TimeEnd,
            Single = true, MultiDecisionVersion = version
        });
        await db.SaveChangesAsync();
    }

    private static EntryCreated DecisionMessage(StatisticsEntry entry) => new(
        entry.TransponderId, entry.TimeStart, entry.TimeEnd, entry.StatisticsItemId, entry.StatisticsItemConfigId,
        entry.Id);

    private static DomainEntryConsumer DecisionHandler(StatisticsDbContext db, HybridCache cache) =>
        new(new(db, cache), new(db, new RequiredPointEmptyOwnershipReader(), DomainHeads(db)),
            NullLogger<EntryCreatedHandler>.Instance);

    private static async Task ProcessPriorDecision(StatisticsDbContext db, HybridCache cache, StatisticsEntry entry)
    {
        await DecisionHandler(db, cache).OnHandle(DecisionMessage(entry), CancellationToken.None);
    }

    [PostgreSqlStatisticsFact]
    public async Task MultiDecisionSelectsOlderSameFacilityAndExcludesLaterReceipt()
    {
        var parent = Simple();
        var multi = ReplayMulti(parent);
        multi.Repetitions = 2;
        var rider = Guid.NewGuid();
        var facilityA = Guid.NewGuid();
        var facilityB = Guid.NewGuid();
        var olderB = DecisionParent(parent, rider, facilityB, Start, Start.AddSeconds(10));
        var newerA = DecisionParent(parent, rider, facilityA, Start.AddSeconds(10), Start.AddSeconds(20));
        var triggerB = DecisionParent(parent, rider, facilityB, Start.AddSeconds(20), Start.AddSeconds(30));
        var laterB = DecisionParent(parent, rider, facilityB, Start.AddSeconds(15), Start.AddSeconds(25));
        laterB.IsValid = false;
        laterB.InvalidReason = StatisticsEntryInvalidReason.BelowMinimumDuration;
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        await using (var db = Db())
        {
            db.AddRange(parent, multi, olderB, newerA, triggerB, laterB);
            await db.SaveChangesAsync();
            foreach (var entry in new[] { olderB, newerA, triggerB, laterB })
                await DecisionReceipt(db, entry);
        }
        await using (var db = Db())
        {
            var receipts = await db.Set<EntryCreatedOutbox>().ToDictionaryAsync(r => r.EntryId);
            Assert.True(receipts[olderB.Id].Id < receipts[newerA.Id].Id);
            Assert.True(receipts[newerA.Id].Id < receipts[triggerB.Id].Id);
            Assert.True(receipts[triggerB.Id].Id < receipts[laterB.Id].Id);
            Assert.All(receipts.Values, r => Assert.Null(r.DeliveredAtUtc));
        }
        foreach (var entry in new[] { olderB, newerA, triggerB, olderB })
        {
            await using var db = Db();
            await DecisionHandler(db, cache).OnHandle(DecisionMessage(entry), CancellationToken.None);
        }
        await using var verify = Db();
        var decisions = await verify.Set<MultiCreationDecision>().ToDictionaryAsync(d => d.ParentEntryId);
        Assert.Equal(3, decisions.Count);
        Assert.All(decisions.Values, d => Assert.Equal(multi.Id, d.MultiConfigId));
        Assert.Null(decisions[olderB.Id].ResultEntryId);
        Assert.Null(decisions[newerA.Id].ResultEntryId);
        var result = Assert.Single(await verify.Set<StatisticsEntry>()
            .Where(e => e.StatisticsItemConfigId == multi.Id).ToListAsync());
        Assert.Equal(decisions[triggerB.Id].ResultEntryId, result.Id);
        Assert.Equal(facilityB, result.FacilityId);
        Assert.Equal(DateTimeKind.Utc, result.TimeStart.Kind);
        Assert.Equal(DateTimeKind.Utc, result.TimeEnd.Kind);
        Assert.Equal(Start, result.TimeStart);
        Assert.Equal(Start.AddSeconds(30), result.TimeEnd);
        Assert.Equal(TimeSpan.FromSeconds(30), result.Duration);
        Assert.Equal(250d / 30d, result.Speed);
        Assert.True(result.IsValid);
        Assert.Null(result.InvalidReason);
        var sources = await verify.Set<EntryCreatedOutbox>().Where(r => r.Single).ToDictionaryAsync(r => r.EntryId);
        Assert.NotNull(sources[olderB.Id].MultiDecisionsCreatedAtUtc);
        Assert.NotNull(sources[newerA.Id].MultiDecisionsCreatedAtUtc);
        Assert.NotNull(sources[triggerB.Id].MultiDecisionsCreatedAtUtc);
        Assert.Null(sources[laterB.Id].MultiDecisionsCreatedAtUtc);
        var output = Assert.Single(await verify.Set<EntryCreatedOutbox>().Where(r => !r.Single).ToListAsync());
        Assert.Equal(result.Id, output.EntryId);
    }

    [PostgreSqlStatisticsFact]
    public async Task MultiDecisionEqualTimeReceiptOrderAndReplayStayStable()
    {
        var parent = Simple();
        var multi = ReplayMulti(parent);
        multi.Repetitions = 2;
        var rider = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var first = DecisionParent(parent, rider, facility, Start, Start.AddSeconds(10));
        var second = DecisionParent(parent, rider, facility, Start, Start.AddSeconds(10));
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        await using (var seed = Db())
        {
            seed.AddRange(parent, multi, first, second);
            await seed.SaveChangesAsync();
            await DecisionReceipt(seed, first);
            await DecisionReceipt(seed, second);
        }
        await using (var outOfOrder = Db())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DecisionHandler(outOfOrder, cache).OnHandle(DecisionMessage(second), CancellationToken.None));
        await using (var verify = Db())
        {
            Assert.Empty(await verify.Set<MultiCreationDecision>().ToListAsync());
            Assert.All(await verify.Set<EntryCreatedOutbox>().ToListAsync(), r => Assert.Null(r.MultiDecisionsCreatedAtUtc));
        }
        await using (var firstDb = Db())
            await DecisionHandler(firstDb, cache).OnHandle(DecisionMessage(first), CancellationToken.None);
        await using (var verify = Db())
        {
            var decision = Assert.Single(await verify.Set<MultiCreationDecision>().ToListAsync());
            Assert.Equal(first.Id, decision.ParentEntryId);
            Assert.Equal(multi.Id, decision.MultiConfigId);
            Assert.Null(decision.ResultEntryId);
            Assert.Empty(await verify.Set<StatisticsEntry>().Where(e => e.StatisticsItemConfigId == multi.Id).ToListAsync());
        }
        await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var db = Db();
            await DecisionHandler(db, cache).OnHandle(DecisionMessage(second), CancellationToken.None);
        }));
        await using (var edit = Db())
        {
            (await edit.Set<MultiStatisticsItemConfig>().SingleAsync()).Repetitions = 1;
            await edit.SaveChangesAsync();
            await DecisionHandler(edit, cache).OnHandle(DecisionMessage(first), CancellationToken.None);
        }
        await using (var verify = Db())
        {
            var decisions = await verify.Set<MultiCreationDecision>().OrderBy(d => d.ParentEntryId).ToListAsync();
            Assert.Equal(2, decisions.Count);
            Assert.Null(Assert.Single(decisions, d => d.ParentEntryId == first.Id).ResultEntryId);
            var resultId = Assert.Single(decisions, d => d.ParentEntryId == second.Id).ResultEntryId;
            Assert.NotNull(resultId);
            var result = Assert.Single(await verify.Set<StatisticsEntry>().Where(e => e.StatisticsItemConfigId == multi.Id).ToListAsync());
            Assert.Equal(resultId, result.Id);
            Assert.Equal(facility, result.FacilityId);
            Assert.True(result.IsValid);
            Assert.Equal(Start, result.TimeStart);
            Assert.Equal(Start.AddSeconds(10), result.TimeEnd);
            Assert.Equal(1, await verify.Set<EntryCreatedOutbox>().CountAsync(r => !r.Single));
            Assert.All(await verify.Set<EntryCreatedOutbox>().Where(r => r.Single).ToListAsync(),
                r => Assert.NotNull(r.MultiDecisionsCreatedAtUtc));
        }
    }

    [PostgreSqlStatisticsFact]
    public async Task MultiDecisionEmptyConfigurationSetIsFinalEvenAfterNewConfig()
    {
        var parent = Simple();
        var source = DecisionParent(parent, Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddSeconds(10));
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        await using (var db = Db())
        {
            db.AddRange(parent, source);
            await db.SaveChangesAsync();
            await DecisionReceipt(db, source);
            await DecisionHandler(db, cache).OnHandle(DecisionMessage(source), CancellationToken.None);
        }
        await using (var db = Db())
        {
            var persistedParent = await db.Set<SimpleStatisticsItemConfig>().Include(c => c.StatisticsItem).SingleAsync();
            db.Add(ReplayMulti(persistedParent));
            await db.SaveChangesAsync();
            await DecisionHandler(db, cache).OnHandle(DecisionMessage(source), CancellationToken.None);
        }
        await using var verify = Db();
        Assert.Empty(await verify.Set<MultiCreationDecision>().ToListAsync());
        Assert.Single(await verify.Set<StatisticsEntry>().ToListAsync());
        Assert.NotNull((await verify.Set<EntryCreatedOutbox>().SingleAsync()).MultiDecisionsCreatedAtUtc);
    }

    [PostgreSqlStatisticsFact]
    public async Task MultiDecisionLegacyAndMissingReceiptsFailWithoutPartialChanges()
    {
        var parent = Simple();
        var multi = ReplayMulti(parent);
        var rider = Guid.NewGuid();
        var missing = DecisionParent(parent, rider, Guid.NewGuid(), Start, Start.AddSeconds(10));
        var legacy = DecisionParent(parent, Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddSeconds(10));
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        await using (var db = Db())
        {
            db.AddRange(parent, multi, missing, legacy);
            await db.SaveChangesAsync();
            await DecisionReceipt(db, legacy, 0);
        }
        foreach (var source in new[] { missing, legacy })
        {
            await using var db = Db();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DecisionHandler(db, cache).OnHandle(DecisionMessage(source), CancellationToken.None));
        }
        await using var verify = Db();
        Assert.Equal(2, await verify.Set<StatisticsEntry>().CountAsync());
        Assert.Empty(await verify.Set<MultiCreationDecision>().ToListAsync());
        Assert.Null((await verify.Set<EntryCreatedOutbox>().SingleAsync()).MultiDecisionsCreatedAtUtc);
    }

    [PostgreSqlStatisticsFact]
    public async Task MultiDecisionLateBackdatedParentDoesNotReviseEarlierNoResult()
    {
        var parent = Simple();
        var multi = ReplayMulti(parent);
        multi.Repetitions = 2;
        var rider = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var first = DecisionParent(parent, rider, facility, Start, Start.AddSeconds(10));
        var late = DecisionParent(parent, rider, facility, Start.AddSeconds(-10), Start);
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        await using (var db = Db())
        {
            db.AddRange(parent, multi, first);
            await db.SaveChangesAsync();
            await DecisionReceipt(db, first);
            await DecisionHandler(db, cache).OnHandle(DecisionMessage(first), CancellationToken.None);
            db.Add(late);
            await db.SaveChangesAsync();
            await DecisionReceipt(db, late);
        }
        await using (var db = Db())
        {
            await DecisionHandler(db, cache).OnHandle(DecisionMessage(late), CancellationToken.None);
            await DecisionHandler(db, cache).OnHandle(DecisionMessage(first), CancellationToken.None);
        }
        await using var verify = Db();
        Assert.Equal(2, await verify.Set<MultiCreationDecision>().CountAsync(d => d.ResultEntryId == null));
        Assert.Equal(2, await verify.Set<StatisticsEntry>().CountAsync());
        Assert.Equal(2, await verify.Set<EntryCreatedOutbox>().CountAsync());
    }

    [PostgreSqlStatisticsFact]
    public async Task MultiDecisionMissingCandidateReceiptFailsWithoutMarkingSource()
    {
        var parent = Simple();
        var multi = ReplayMulti(parent);
        multi.Repetitions = 2;
        var rider = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var missing = DecisionParent(parent, rider, facility, Start, Start.AddSeconds(10));
        var trigger = DecisionParent(parent, rider, facility, Start.AddSeconds(10), Start.AddSeconds(20));
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        await using (var db = Db())
        {
            db.AddRange(parent, multi, missing, trigger);
            await db.SaveChangesAsync();
            await DecisionReceipt(db, trigger);
        }
        await using (var db = Db())
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DecisionHandler(db, cache).OnHandle(DecisionMessage(trigger), CancellationToken.None));
            Assert.Contains("candidate", error.Message);
        }
        await using var verify = Db();
        Assert.Empty(await verify.Set<MultiCreationDecision>().ToListAsync());
        Assert.Equal(2, await verify.Set<StatisticsEntry>().CountAsync());
        Assert.Null((await verify.Set<EntryCreatedOutbox>().SingleAsync()).MultiDecisionsCreatedAtUtc);
    }

    [PostgreSqlStatisticsFact]
    public async Task MultiDecisionOutputFailureRollsBackLedgerAndMarkerThenRetries()
    {
        var parent = Simple();
        var multi = ReplayMulti(parent);
        var source = DecisionParent(parent, Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddSeconds(10));
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        await using (var db = Db())
        {
            db.AddRange(parent, multi, source);
            await db.SaveChangesAsync();
            await DecisionReceipt(db, source);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION statistics.fail_multi_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN IF NOT NEW.single THEN RAISE EXCEPTION 'injected'; END IF; RETURN NEW; END $$;
                CREATE TRIGGER fail_multi_receipt BEFORE INSERT ON statistics.entry_created_outbox
                    FOR EACH ROW EXECUTE FUNCTION statistics.fail_multi_receipt();
                """);
        }
        await using (var db = Db())
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                DecisionHandler(db, cache).OnHandle(DecisionMessage(source), CancellationToken.None));
        await using (var db = Db())
        {
            Assert.Empty(await db.Set<MultiCreationDecision>().ToListAsync());
            Assert.Single(await db.Set<StatisticsEntry>().ToListAsync());
            Assert.Null((await db.Set<EntryCreatedOutbox>().SingleAsync()).MultiDecisionsCreatedAtUtc);
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_multi_receipt ON statistics.entry_created_outbox");
            await DecisionHandler(db, cache).OnHandle(DecisionMessage(source), CancellationToken.None);
        }
        await using var verify = Db();
        Assert.NotNull(Assert.Single(await verify.Set<MultiCreationDecision>().ToListAsync()).ResultEntryId);
        Assert.Equal(2, await verify.Set<StatisticsEntry>().CountAsync());
        Assert.Equal(2, await verify.Set<EntryCreatedOutbox>().CountAsync());
    }
}
