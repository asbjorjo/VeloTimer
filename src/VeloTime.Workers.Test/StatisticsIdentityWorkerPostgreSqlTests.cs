using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SlimMessageBus;
using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Statistics;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    [PostgreSqlStatisticsFact]
    public async Task WorkerIdentity_EqualPayloadsKeepOrderedSourcesAndFacilitiesWithoutDeadlock()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var rider = Guid.NewGuid();
        var config = Simple();
        var multi = ReplayMulti(config);
        var firstTiming = new TimingSampleComplete(Start, Start.AddSeconds(10), Guid.NewGuid(), Guid.NewGuid(), rider);
        var secondTiming = new TimingSampleComplete(Start, Start.AddSeconds(10), Guid.NewGuid(), Guid.NewGuid(), rider);
        var facilities = DispatchProxy.Create<IFacilitiesClient, TimingSampleEventTimeTests.FacilitiesProxy>();
        var firstFacility = Guid.NewGuid();
        var secondFacility = Guid.NewGuid();
        ((TimingSampleEventTimeTests.FacilitiesProxy)facilities).Resolve = (from, _, _, _, _) =>
            Task.FromResult(new CoursePointDistance
            {
                CoursePointStartId = config.CoursePointStart, CoursePointEndId = config.CoursePointEnd,
                Distance = 250, FacilityId = from == firstTiming.TimingPointStart ? firstFacility : secondFacility
            });
        await using var services = new ServiceCollection().AddMetrics().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        var metrics = new Metrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        await using (var seed = Db())
        {
            seed.AddRange(config, multi);
            await seed.SaveChangesAsync(ct);
        }
        foreach (var timing in new[] { firstTiming, secondTiming })
        {
            await using var db = Db();
            await new TimingSampleHandler(db, facilities, metrics, NullLogger<TimingSampleHandler>.Instance).OnHandle(timing, ct);
        }
        var bus = DispatchProxy.Create<IMessageBus, OutboxBus>();
        var published = (OutboxBus)(object)bus;
        published.Fail = false;
        async Task<bool> DispatchSample()
        {
            await using var db = Db();
            return await new SampleCompleteOutboxDispatcher(db, bus).DispatchOneAsync(ct);
        }
        async Task HandleSample(SampleComplete message)
        {
            await using var db = Db();
            await CompletionHandler(db, cache, metrics).OnHandle(message, ct);
        }
        await using var inspect = Db();
        var receipts = await inspect.Set<SampleCompleteOutbox>().OrderBy(x => x.Id).AsNoTracking().ToListAsync(ct);
        Assert.Equal(2, receipts.Count);
        var first = new SampleComplete(rider, Start, Start.AddSeconds(10), config.CoursePointStart, config.CoursePointEnd, 250, receipts[0].SampleId);
        var second = first with { SampleId = receipts[1].SampleId };
        Assert.NotEqual(first.SampleId, second.SampleId);
        Assert.True(await DispatchSample());
        Assert.Equal(first, Assert.Single(published.Messages));
        Assert.False(await DispatchSample());
        await Assert.ThrowsAsync<InvalidOperationException>(() => HandleSample(second));
        await Assert.ThrowsAsync<InvalidOperationException>(() => HandleSample(first with { SampleId = null }));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => HandleSample(first))).WaitAsync(ct);
        Assert.True(await DispatchSample());
        Assert.Equal(second, published.Messages.Last());
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => HandleSample(second))).WaitAsync(ct);

        var entryBus = DispatchProxy.Create<IMessageBus, EntryOutboxBus>();
        var entries = (EntryOutboxBus)(object)entryBus;
        for (var i = 0; i < 2; i++)
        {
            await using var db = Db();
            Assert.True(await new EntryCreatedOutboxDispatcher(db, entryBus).DispatchOneAsync(ct));
        }
        var parents = entries.Messages.ToArray();
        Assert.Equal(2, parents.Length);
        Assert.NotEqual(parents[0].EntryId, parents[1].EntryId);
        Assert.Equal(parents[0] with { EntryId = null }, parents[1] with { EntryId = null });
        async Task HandleParent(EntryCreated message)
        {
            await using var db = Db();
            await new EntryCreatedHandler(new(db, cache), new(db, new RequiredPointEmptyOwnershipReader()),
                NullLogger<EntryCreatedHandler>.Instance).OnHandle(message, ct);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => HandleParent(parents[0] with { EntryId = null }));
        foreach (var parent in parents)
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => HandleParent(parent))).WaitAsync(ct);
        await using var verify = Db();
        var simple = await verify.Set<StatisticsEntry>().Where(e => e.StatisticsItemConfigId == config.Id).ToListAsync(ct);
        var derived = await verify.Set<StatisticsEntry>().Where(e => e.StatisticsItemConfigId == multi.Id).ToListAsync(ct);
        Assert.Equal(2, simple.Count);
        Assert.Equal(2, derived.Count);
        Assert.Equal(firstFacility, simple.Single(e => e.Id == parents[0].EntryId).FacilityId);
        Assert.Equal(secondFacility, simple.Single(e => e.Id == parents[1].EntryId).FacilityId);
        Assert.Contains(derived, e => e.FacilityId == firstFacility);
        Assert.Contains(derived, e => e.FacilityId == secondFacility);
        Assert.Equal(4, await verify.Set<EntryCreatedOutbox>().CountAsync(ct));
    }

    [PostgreSqlStatisticsFact]
    public async Task WorkerIdentity_RejectsMissingEmptyAndConflictingSourcesAndAcceptsLegacyAliases()
    {
        var rider = Guid.NewGuid();
        var config = Simple();
        var multi = ReplayMulti(config);
        var sample = CompletionSample(config, rider);
        var completion = new SampleComplete(rider, Start, Start.AddSeconds(10), config.CoursePointStart,
            config.CoursePointEnd, sample.Distance, sample.Id);
        await using var services = new ServiceCollection().AddMetrics().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        var metrics = new Metrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        await using (var seed = Db())
        {
            seed.AddRange(config, multi, sample, new SampleCompleteOutbox
            {
                SampleId = sample.Id, TransponderId = rider, TimeStart = Start, TimeEnd = Start.AddSeconds(10),
                CoursePointStart = config.CoursePointStart, CoursePointEnd = config.CoursePointEnd, Distance = sample.Distance
            });
            await seed.SaveChangesAsync();
        }
        async Task Complete(SampleComplete m)
        {
            await using var db = Db();
            await CompletionHandler(db, cache, metrics).OnHandle(m, CancellationToken.None);
        }
        foreach (var invalid in new[] { completion with { SampleId = Guid.Empty }, completion with { SampleId = Guid.NewGuid() },
            completion with { Distance = 251 }, completion with { CoursePointStart = Guid.NewGuid() },
            completion with { TimeEnd = Start.AddSeconds(11) }, completion with { TransponderId = Guid.NewGuid() } })
            await Assert.ThrowsAsync<InvalidOperationException>(() => Complete(invalid));
        var unreceipted = new Sample
        {
            Id = Guid.NewGuid(), TransponderId = rider, FacilityId = Guid.NewGuid(),
            TimingPointStartId = Guid.NewGuid(), TimingPointEndId = Guid.NewGuid(),
            CoursePointStartId = config.CoursePointStart, CoursePointEndId = config.CoursePointEnd,
            TimeStart = Start, TimeEnd = Start.AddSeconds(10), Duration = TimeSpan.FromSeconds(10), Distance = 250, Speed = 25
        };
        await using (var db = Db())
        {
            db.Add(unreceipted);
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => Complete(completion with { SampleId = null }));
        await using (var db = Db())
            await db.Set<Sample>().Where(s => s.Id == unreceipted.Id).ExecuteDeleteAsync();
        await Complete(completion with { SampleId = null });
        await Complete(completion);
        EntryCreated parent;
        await using (var db = Db())
        {
            var row = await db.Set<EntryCreatedOutbox>().SingleAsync();
            parent = new(row.TransponderId, row.TimeStart, row.TimeEnd, row.StatisticsItemId, row.ConfigItemId, row.EntryId);
        }
        async Task Handle(EntryCreated m)
        {
            await using var db = Db();
            await new EntryCreatedHandler(new(db, cache), new(db, new RequiredPointEmptyOwnershipReader()),
                NullLogger<EntryCreatedHandler>.Instance).OnHandle(m, CancellationToken.None);
        }
        foreach (var invalid in new[] { parent with { EntryId = Guid.Empty }, parent with { EntryId = Guid.NewGuid() },
            parent with { StatisticsItemId = Guid.NewGuid() }, parent with { ConfigItemId = Guid.NewGuid() },
            parent with { TimeStart = Start.AddSeconds(1) }, parent with { TransponderId = Guid.NewGuid() } })
            await Assert.ThrowsAsync<InvalidOperationException>(() => Handle(invalid));
        await Handle(parent);
        await Handle(parent with { EntryId = null });
        await using (var verify = Db())
        {
            Assert.Equal(2, await verify.Set<StatisticsEntry>().CountAsync());
            Assert.Equal(2, await verify.Set<EntryCreatedOutbox>().CountAsync());
            await verify.Set<EntryCreatedOutbox>().Where(o => o.EntryId == parent.EntryId).ExecuteDeleteAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handle(parent));
        await using (var verify = Db())
            await verify.Set<Sample>().Where(s => s.Id == sample.Id).ExecuteDeleteAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Complete(completion));
    }
}
