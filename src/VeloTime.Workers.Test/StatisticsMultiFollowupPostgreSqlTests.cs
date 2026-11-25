using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using SlimMessageBus;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    [PostgreSqlStatisticsFact]
    public async Task DispatchedMultiIsConsumedWithoutRecursionButConflictingReceiptsFail()
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
            await DecisionHandler(db, cache).OnHandle(DecisionMessage(source), CancellationToken.None);
        }
        EntryCreated message;
        await using (var db = Db())
        {
            var receipt = await db.Set<EntryCreatedOutbox>().SingleAsync(r => !r.Single);
            message = new(receipt.TransponderId, receipt.TimeStart, receipt.TimeEnd,
                receipt.StatisticsItemId, receipt.ConfigItemId, receipt.EntryId);
        }
        var generation = (await Job(multi.StatisticsItemId)).Generation;
        await using (var db = Db())
        {
            await DecisionHandler(db, cache).OnHandle(message, CancellationToken.None);
            await DecisionHandler(db, cache).OnHandle(message, CancellationToken.None);
        }
        await using (var db = Db())
        {
            Assert.Equal(2, await db.Set<StatisticsEntry>().CountAsync());
            Assert.Equal(2, await db.Set<EntryCreatedOutbox>().CountAsync());
            Assert.Single(await db.Set<MultiCreationDecision>().ToListAsync());
            Assert.NotNull((await db.Set<EntryCreatedOutbox>().SingleAsync(o => !o.Single)).MultiDecisionsCreatedAtUtc);
        }
        Assert.Equal(generation, (await Job(multi.StatisticsItemId)).Generation);

        // The harmless self-event path must not bypass source identity, receipt version or type checks.
        await using (var db = Db())
            await Assert.ThrowsAsync<InvalidOperationException>(() => DecisionHandler(db, cache).OnHandle(
                new EntryCreated(message.TransponderId, message.TimeStart, message.TimeEnd.AddSeconds(1),
                    message.StatisticsItemId, message.ConfigItemId, message.EntryId), CancellationToken.None));
        await using (var db = Db())
        {
            await db.Set<EntryCreatedOutbox>().Where(o => !o.Single)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.MultiDecisionVersion, 0)
                    .SetProperty(o => o.MultiDecisionsCreatedAtUtc, (DateTime?)null));
        }
        await using (var db = Db())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DecisionHandler(db, cache).OnHandle(message, CancellationToken.None));
        await using (var db = Db())
        {
            await db.Set<EntryCreatedOutbox>().Where(o => !o.Single).ExecuteUpdateAsync(s => s
                .SetProperty(o => o.MultiDecisionVersion, 1).SetProperty(o => o.Single, true));
        }
        await using (var db = Db())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DecisionHandler(db, cache).OnHandle(message, CancellationToken.None));
        await using (var db = Db())
            await db.Set<EntryCreatedOutbox>().Where(o => o.EntryId == message.EntryId).ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Single, false).SetProperty(o => o.TimeEnd, message.TimeEnd.AddSeconds(1)));
        await using (var db = Db())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DecisionHandler(db, cache).OnHandle(message, CancellationToken.None));
        await using var verify = Db();
        Assert.Equal(2, await verify.Set<StatisticsEntry>().CountAsync());
        Assert.Equal(2, await verify.Set<EntryCreatedOutbox>().CountAsync());
        Assert.Single(await verify.Set<MultiCreationDecision>().ToListAsync());
        Assert.Equal(generation, (await Job(multi.StatisticsItemId)).Generation);
    }

    [PostgreSqlStatisticsFact]
    public Task CreatedMultiRevalidatesAfterLaterEqualWindowSweepCompleted() => AssertCreatedMultiFollowup(false);

    [PostgreSqlStatisticsFact]
    public Task CreatedMultiBehindActiveCursorGetsFollowupSweep() => AssertCreatedMultiFollowup(true);

    private async Task AssertCreatedMultiFollowup(bool activeCursor)
    {
        var parent = Simple();
        var multi = ReplayMulti(parent);
        var rider = Guid.NewGuid();
        var laterSample = CompletionSample(parent, rider);
        var facility = laterSample.FacilityId;
        var first = DecisionParent(parent, rider, facility, Start, Start.AddSeconds(10));
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        await using (var db = Db())
        {
            db.AddRange(parent, multi, first, laterSample);
            await db.SaveChangesAsync();
            await DecisionReceipt(db, first);
        }
        await using (var db = Db())
        {
            var service = new StatisticsService(db, cache);
            var later = Assert.Single(await new StatisticsOwnershipService(db, new RequiredPointEmptyOwnershipReader(), DomainHeads(db))
                .CreateEntriesAsync(rider, ct => service.ProcessCompletedAsync(rider, laterSample.TimeStart,
                    laterSample.TimeEnd, parent.CoursePointStart, parent.CoursePointEnd, ct,
                    laterSample.Id, laterSample.Distance), CancellationToken.None, enqueueSingle: true));
            Assert.Equal((first.TimeStart, first.TimeEnd, facility), (later.TimeStart, later.TimeEnd, later.FacilityId));
        }
        Assert.Equal(StatisticsRevalidationBatchOutcome.Completed, (await Drain(multi.StatisticsItemId, 2))[^1].Outcome);
        Assert.False((await Job(multi.StatisticsItemId)).IsPending);

        if (activeCursor)
        {
            // This maximal UUID forces the not-yet-created deterministic output behind the real cursor.
            await using (var db = Db())
            {
                db.Add(new StatisticsEntry
                {
                    Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                    TransponderId = Guid.NewGuid(), FacilityId = Guid.NewGuid(),
                    StatisticsItemId = multi.StatisticsItemId, StatisticsItemConfigId = multi.Id,
                    TimeStart = Start, TimeEnd = Start.AddSeconds(10),
                    Duration = TimeSpan.FromSeconds(10), Speed = 50
                });
                await db.SaveChangesAsync();
            }
            await Change(multi.StatisticsItemId, null);
            Assert.Equal(StatisticsRevalidationBatchOutcome.SimplePhaseCompleted,
                (await Batch(multi.StatisticsItemId, 1)).Outcome);
            Assert.Equal(1, (await Batch(multi.StatisticsItemId, 1)).Scanned);
            Assert.Equal(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), (await Job(multi.StatisticsItemId)).LastEntryId);
        }
        var before = await Job(multi.StatisticsItemId);
        await using (var db = Db())
            await DecisionHandler(db, cache).OnHandle(DecisionMessage(first), CancellationToken.None);
        var scheduled = await Job(multi.StatisticsItemId);
        Assert.Equal(before.Generation + 1, scheduled.Generation);
        Assert.True(scheduled.IsPending);
        Assert.Equal(activeCursor, scheduled.MultiRescanPending);
        Assert.Equal(before.LastEntryId, scheduled.LastEntryId);
        Guid resultId;
        await using (var db = Db())
        {
            resultId = (await db.Set<MultiCreationDecision>().SingleAsync()).ResultEntryId!.Value;
            var result = await db.Set<StatisticsEntry>().SingleAsync(e => e.Id == resultId);
            Assert.True(result.IsValid); // receipt-order bounded creation excludes the later parent
            Assert.Equal(DateTimeKind.Utc, result.TimeStart.Kind);
            Assert.Equal(Start, result.TimeStart);
            Assert.Equal(Start.AddSeconds(10), result.TimeEnd);
        }
        Assert.Equal(StatisticsRevalidationBatchOutcome.Completed, (await Drain(multi.StatisticsItemId, 1))[^1].Outcome);
        await using (var db = Db())
        {
            var result = await db.Set<StatisticsEntry>().SingleAsync(e => e.Id == resultId);
            Assert.False(result.IsValid);
            Assert.Equal(StatisticsEntryInvalidReason.InvalidContributor, result.InvalidReason);
        }
        var completed = await Job(multi.StatisticsItemId);
        Assert.False(completed.IsPending);
        await using (var db = Db())
            await DecisionHandler(db, cache).OnHandle(DecisionMessage(first), CancellationToken.None);
        Assert.Equal(completed.Generation, (await Job(multi.StatisticsItemId)).Generation);
    }

    [PostgreSqlStatisticsFact]
    public async Task CompletedMultiReplayRejectsDeletedOutputAndDeletedReceipt()
    {
        var parent = Simple();
        var multi = ReplayMulti(parent);
        var first = DecisionParent(parent, Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddSeconds(10));
        var second = DecisionParent(parent, Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddSeconds(10));
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        await using (var db = Db())
        {
            db.AddRange(parent, multi, first, second);
            await db.SaveChangesAsync();
            await DecisionReceipt(db, first);
            await DecisionReceipt(db, second);
            await DecisionHandler(db, cache).OnHandle(DecisionMessage(first), CancellationToken.None);
            await DecisionHandler(db, cache).OnHandle(DecisionMessage(second), CancellationToken.None);
        }
        await using (var db = Db())
        {
            var decisions = await db.Set<MultiCreationDecision>().ToDictionaryAsync(d => d.ParentEntryId);
            var firstOutput = decisions[first.Id].ResultEntryId!.Value;
            var secondOutput = decisions[second.Id].ResultEntryId!.Value;
            await db.Set<StatisticsEntry>().Where(e => e.Id == firstOutput).ExecuteDeleteAsync();
            await db.Set<EntryCreatedOutbox>().Where(o => o.EntryId == secondOutput).ExecuteDeleteAsync();
        }
        foreach (var source in new[] { first, second })
        {
            await using var db = Db();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ReplayCommittedDecision(db, cache, source));
        }
        await using var verify = Db();
        Assert.Equal(2, await verify.Set<MultiCreationDecision>().CountAsync());
        Assert.Equal(3, await verify.Set<StatisticsEntry>().CountAsync());
        Assert.Equal(3, await verify.Set<EntryCreatedOutbox>().CountAsync());
    }

    private static async Task ReplayCommittedDecision(VeloTime.Module.Statistics.Storage.StatisticsDbContext db,
        HybridCache cache, StatisticsEntry source)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await new StatisticsService(db, cache).ProcessEntryAsync(source.TransponderId,
            source.StatisticsItemId, source.StatisticsItemConfigId, source.TimeStart, source.TimeEnd,
            entryId: source.Id);
    }
}
