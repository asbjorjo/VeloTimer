using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SlimMessageBus;
using VeloTime.Module.Statistics;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    [PostgreSqlStatisticsFact]
    public async Task ConcurrentFixedAndMultiRedeliveriesCreateOneEntryAndNormalizeSourcePrecision()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        var config = Simple();
        var multi = ReplayMulti(config);
        var rider = Guid.NewGuid();
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        await using (var seed = Db())
        {
            seed.AddRange(config, multi, CompletionSample(config, rider));
            await seed.SaveChangesAsync(token);
        }

        var completions = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
        {
            await using var db = Db();
            var service = new StatisticsService(db, cache);
            return Assert.Single(await new StatisticsOwnershipService(db, new RequiredPointEmptyOwnershipReader())
                .CreateEntriesAsync(rider, ct => service.ProcessCompletedAsync(rider,
                    Start.AddTicks(index), Start.AddSeconds(10).AddTicks(index),
                    config.CoursePointStart, config.CoursePointEnd, ct), token));
        }));
        Assert.Single(completions.Select(e => e.Id).Distinct());
        var parent = completions[0];
        var aggregates = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
        {
            await using var db = Db();
            var service = new StatisticsService(db, cache);
            return Assert.Single(await new StatisticsOwnershipService(db, new RequiredPointEmptyOwnershipReader())
                .CreateEntriesAsync(rider, ct => service.ProcessEntryAsync(rider, parent.StatisticsItemId,
                    parent.StatisticsItemConfigId, parent.TimeStart.AddTicks(index), parent.TimeEnd.AddTicks(index), ct), token));
        }));
        Assert.Single(aggregates.Select(e => e.Id).Distinct());
        Assert.All(aggregates, e => Assert.True(e.IsValid));
        await using var verify = Db();
        Assert.Equal(2, await verify.Set<StatisticsEntry>().CountAsync(token));
        Assert.Equal(2, (await verify.Set<StatisticsValidityRevalidation>()
            .SingleAsync(j => j.StatisticsItemId == multi.StatisticsItemId, token)).Generation);
    }

    [PostgreSqlStatisticsFact]
    public async Task FixedAndMultiPublishFailuresRetryOriginalPayloadAfterLateEvidenceAndConfigChanges()
    {
        var config = Simple();
        var multi = ReplayMulti(config);
        var rider = Guid.NewGuid();
        var sample = CompletionSample(config, rider);
        await using var services = new ServiceCollection().AddMetrics().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        var bus = DispatchProxy.Create<IMessageBus, RequiredPointRetryBus>();
        var recorder = (RequiredPointRetryBus)(object)bus;
        var message = new SampleComplete(rider, Start, Start.AddSeconds(10),
            config.CoursePointStart, config.CoursePointEnd, 250);
        SampleCompleteHandler Handler(VeloTime.Module.Statistics.Storage.StatisticsDbContext db) => new(
            new(db, cache), new(db, new RequiredPointEmptyOwnershipReader()),
            new Metrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()),
            bus, NullLogger<SampleCompleteHandler>.Instance);
        await using (var db = Db())
        {
            db.AddRange(config, multi, sample);
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(db).OnHandle(message, CancellationToken.None));
        }
        await using (var db = Db())
        {
            var current = await db.Set<SimpleStatisticsItemConfig>().Include(c => c.StatisticsItem).SingleAsync();
            current.StatisticsItem.Distance = 999;
            current.RequiredCoursePointIds = [Guid.NewGuid()];
            await db.SaveChangesAsync();
            recorder.Fail = false;
            await Handler(db).OnHandle(message, CancellationToken.None);
        }
        Assert.Equal(recorder.Events[0], recorder.Events[1]);
        var parentEvent = Assert.Single(recorder.Events[1]);
        await using (var db = Db())
        {
            recorder.Fail = true;
            var handler = new EntryCreatedHandler(new(db, cache), new(db, new RequiredPointEmptyOwnershipReader()),
                bus, NullLogger<EntryCreatedHandler>.Instance);
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.OnHandle(parentEvent, CancellationToken.None));
        }
        await using (var db = Db())
        {
            // This would change the selected window, or suppress creation with the new repetition
            // count, if retry reran derivation instead of loading the original source-keyed row.
            db.Add(new StatisticsEntry
            {
                TransponderId = rider, StatisticsItemId = config.StatisticsItemId,
                StatisticsItemConfigId = config.Id, TimeStart = Start.AddSeconds(2), TimeEnd = Start.AddSeconds(9),
                FacilityId = sample.FacilityId, Duration = TimeSpan.FromSeconds(7), Speed = 250d / 7
            });
            (await db.Set<MultiStatisticsItemConfig>().SingleAsync()).Repetitions = 10;
            await db.SaveChangesAsync();
            recorder.Fail = false;
            var handler = new EntryCreatedHandler(new(db, cache), new(db, new RequiredPointEmptyOwnershipReader()),
                bus, NullLogger<EntryCreatedHandler>.Instance);
            await handler.OnHandle(parentEvent, CancellationToken.None);
            Assert.Equal(3, await db.Set<StatisticsEntry>().CountAsync());
            Assert.Single(await db.Set<StatisticsEntry>().Where(e => e.StatisticsItemConfigId == multi.Id).ToListAsync());
        }
        Assert.Equal(recorder.Events[2], recorder.Events[3]);
    }

    [PostgreSqlStatisticsFact]
    public async Task MultiCreationReloadsRepetitionsAfterWaitingForAdminDespiteWarmDefinitionCache()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var parent = Simple();
        var multi = ReplayMulti(parent);
        var rider = Guid.NewGuid();
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        await using (var seed = Db())
        {
            seed.AddRange(parent, multi, new StatisticsEntry
            {
                TransponderId = rider, StatisticsItemId = parent.StatisticsItemId, StatisticsItemConfigId = parent.Id,
                TimeStart = Start, TimeEnd = Start.AddSeconds(10), Duration = TimeSpan.FromSeconds(10),
                Speed = 25, FacilityId = Guid.NewGuid()
            });
            await seed.SaveChangesAsync(token);
        }
        await using var processor = Db();
        var service = new StatisticsService(processor, cache);
        Assert.Equal(1, Assert.Single(await service.GetMultiStatisticsItemConfigs(parent.Id, token)).Repetitions);
        await using var adminDb = Db();
        await using var adminTx = await adminDb.Database.BeginTransactionAsync(token);
        await new StatisticsValidityScheduler(adminDb).LockItemsAsync([multi.StatisticsItemId],
            StatisticsItemLockMode.Exclusive, token);
        (await adminDb.Set<MultiStatisticsItemConfig>().SingleAsync(token)).Repetitions = 2;
        await adminDb.SaveChangesAsync(token);
        await processor.Database.OpenConnectionAsync(token);
        var creation = new StatisticsOwnershipService(processor, new RequiredPointEmptyOwnershipReader())
            .CreateEntriesAsync(rider, ct => service.ProcessEntryAsync(rider, parent.StatisticsItemId,
                parent.Id, Start, Start.AddSeconds(10), ct), token);
        await WaitForCompletionLockWait(processor, token);
        Assert.False(creation.IsCompleted);
        await adminTx.CommitAsync(token);

        Assert.Empty(await creation); // Only one parent exists: the NEW repetition count wins.
        Assert.Single(await processor.Set<StatisticsEntry>().AsNoTracking().ToListAsync(token));
    }

    private static MultiStatisticsItemConfig ReplayMulti(SimpleStatisticsItemConfig parent) => new()
    {
        Id = Guid.NewGuid(), ParentConfig = parent, ParentConfigId = parent.Id, Repetitions = 1,
        StatisticsItem = new StatisticsItem { Id = Guid.NewGuid(), Name = "Replay Multi", Distance = 250 }
    };
}
