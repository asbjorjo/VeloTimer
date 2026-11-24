using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SlimMessageBus;
using VeloTime.Module.Statistics;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    [PostgreSqlStatisticsFact]
    public async Task RequiredLapPublicationFailureRetriesOriginalOutcomeAndMultiWithoutDuplicateRows()
    {
        var config = Simple();
        config.CoursePointEnd = config.CoursePointStart;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        config.RequiredCoursePointIds = [a, b];
        config.RequiredCourseLayoutId = Guid.NewGuid();
        var multiItem = new StatisticsItem { Name = "Required repeat", Distance = 250 };
        var multi = new MultiStatisticsItemConfig
        {
            StatisticsItem = multiItem, ParentConfig = config, ParentConfigId = config.Id, Repetitions = 1
        };
        var rider = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var rawStart = Start.AddTicks(7);
        Sample Edge(Guid from, Guid to, int start, int end) => new()
        {
            TransponderId = rider, FacilityId = facility,
            CoursePointStartId = from, CoursePointEndId = to,
            TimeStart = rawStart.AddSeconds(start), TimeEnd = rawStart.AddSeconds(end),
            Duration = TimeSpan.FromSeconds(end - start), Distance = 100, Speed = 10
        };
        await using var services = new ServiceCollection().AddMetrics().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        var bus = DispatchProxy.Create<IMessageBus, RequiredPointRetryBus>();
        var recorder = (RequiredPointRetryBus)(object)bus;
        var message = new SampleComplete(rider, rawStart.AddSeconds(20), rawStart.AddSeconds(30),
            b, config.CoursePointEnd, 100);
        SampleCompleteHandler Handler(VeloTime.Module.Statistics.Storage.StatisticsDbContext db) => new(
            new(db, cache), new(db, new RequiredPointEmptyOwnershipReader()),
            new Metrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()),
            NullLogger<SampleCompleteHandler>.Instance);
        await using (var db = Db())
        {
            db.AddRange(config, multi, Edge(config.CoursePointStart, a, 0, 10), Edge(b, config.CoursePointEnd, 20, 30),
                new SampleCompleteOutbox
                {
                    SampleId = Guid.NewGuid(), TransponderId = rider, TimeStart = message.TimeStart,
                    TimeEnd = message.TimeEnd, CoursePointStart = message.CoursePointStart,
                    CoursePointEnd = message.CoursePointEnd, Distance = message.Distance
                });
            await db.SaveChangesAsync();
            await Handler(db).OnHandle(message, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new EntryCreatedOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None));
        }
        // The row committed before publication failed. A newly delivered checkpoint cannot repair it.
        await using (var db = Db())
        {
            var entry = await db.Set<StatisticsEntry>().SingleAsync();
            Assert.False(entry.RequiredCoursePointsSatisfied);
            Assert.Equal(Start, entry.TimeStart);
            Assert.Equal(Start.AddSeconds(30), entry.TimeEnd);
            db.Add(Edge(a, b, 10, 20));
            await db.SaveChangesAsync();
            recorder.Fail = false;
            await Handler(db).OnHandle(message, CancellationToken.None);
            await new EntryCreatedOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None);
        }
        Assert.Equal(2, recorder.Events.Count);
        Assert.Equal(recorder.Events[0], recorder.Events[1]);
        var parentEvent = Assert.Single(recorder.Events[1]);
        await using (var db = Db())
        {
            var handler = new EntryCreatedHandler(new(db, cache), new(db, new RequiredPointEmptyOwnershipReader()),
                NullLogger<EntryCreatedHandler>.Instance);
            recorder.Fail = true;
            await handler.OnHandle(parentEvent, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new EntryCreatedOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None));
        }
        await using (var db = Db())
        {
            recorder.Fail = false;
            var handler = new EntryCreatedHandler(new(db, cache), new(db, new RequiredPointEmptyOwnershipReader()),
                NullLogger<EntryCreatedHandler>.Instance);
            await handler.OnHandle(parentEvent, CancellationToken.None);
            await new EntryCreatedOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None);
            var rows = await db.Set<StatisticsEntry>().AsNoTracking().ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.Equal(StatisticsEntryInvalidReason.RequiredCoursePointsNotSatisfied,
                Assert.Single(rows, e => e.StatisticsItemConfigId == config.Id).InvalidReason);
            Assert.Equal(StatisticsEntryInvalidReason.InvalidContributor,
                Assert.Single(rows, e => e.StatisticsItemConfigId != config.Id).InvalidReason);
        }
        Assert.Equal(recorder.Events[2], recorder.Events[3]);
        await Change(config.StatisticsItemId, TimeSpan.FromSeconds(60));
        await Drain(config.StatisticsItemId, 1);
        await Drain(multiItem.Id, 1);
        await Change(config.StatisticsItemId, null);
        await Drain(config.StatisticsItemId, 1);
        await Drain(multiItem.Id, 1);
        await using var verify = Db();
        Assert.All(await verify.Set<StatisticsEntry>().ToListAsync(), e => Assert.False(e.IsValid));
    }

    private sealed class RequiredPointEmptyOwnershipReader : ITransponderOwnershipReader
    {
        public Task<IReadOnlyList<TransponderOwnershipDTO>> ReadAsync(Guid transponderId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TransponderOwnershipDTO>>([]);
    }

    public class RequiredPointRetryBus : DispatchProxy
    {
        public bool Fail { get; set; } = true;
        public List<EntryCreated[]> Events { get; } = [];
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.Equal("Publish", targetMethod?.Name);
            Events.Add([Assert.IsType<EntryCreated>(args![0])]);
            return Fail ? Task.FromException(new InvalidOperationException("Injected publish failure")) : Task.CompletedTask;
        }
    }
}
