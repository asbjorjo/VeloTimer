using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Statistics;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    [PostgreSqlStatisticsFact]
    public async Task ExternalCompletionWithoutReceiptFailsAndRetainedQueuedMessageRemainsReplayable()
    {
        var rider = Guid.NewGuid();
        var config = Simple();
        var timing = TimingEvent(rider, Start, Start.AddSeconds(10));
        var completion = Completion(timing, config);
        var facilities = Facilities(config);
        await using var services = new ServiceCollection().AddMetrics().AddHybridCache().Services.BuildServiceProvider();
        var metrics = new Metrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var cache = services.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>();

        await using (var seed = Db())
        {
            seed.Add(config);
            await seed.SaveChangesAsync();
        }

        await using (var db = Db())
        {
            var handler = CompletionHandler(db, cache, metrics);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                handler.OnHandle(completion, CancellationToken.None));
            Assert.Contains("no matching local outbox receipt", error.Message);
            Assert.Empty(await db.Set<StatisticsEntry>().ToListAsync());
            Assert.Empty(await db.Set<EntryCreatedOutbox>().ToListAsync());
        }

        // A pre-outbox sample cannot be silently acknowledged as a successful producer replay.
        var legacyTiming = TimingEvent(Guid.NewGuid(), Start, Start.AddSeconds(10));
        await using (var db = Db())
        {
            db.Add(new Sample
            {
                Id = Guid.NewGuid(), TransponderId = legacyTiming.TransponderId,
                TimingPointStartId = legacyTiming.TimingPointStart, TimingPointEndId = legacyTiming.TimingPointEnd,
                TimeStart = legacyTiming.TimeStart, TimeEnd = legacyTiming.TimeEnd,
                CoursePointStartId = config.CoursePointStart, CoursePointEndId = config.CoursePointEnd,
                Distance = config.StatisticsItem.Distance, Duration = legacyTiming.TimeEnd - legacyTiming.TimeStart,
                Speed = config.StatisticsItem.Distance / (legacyTiming.TimeEnd - legacyTiming.TimeStart).TotalSeconds,
                FacilityId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new TimingSampleHandler(db, facilities, metrics, NullLogger<TimingSampleHandler>.Instance, DomainHeads(db))
                    .OnHandle(legacyTiming, CancellationToken.None));
            Assert.Contains("has no local SampleComplete outbox receipt", error.Message);
        }

        // A queued message from a legitimate local producer remains processable once its
        // durable source receipt exists; at-least-once redelivery stays idempotent.
        await using (var db = Db())
            await new TimingSampleHandler(db, facilities, metrics, NullLogger<TimingSampleHandler>.Instance, DomainHeads(db))
                .OnHandle(timing, CancellationToken.None);
        await using (var db = Db())
        {
            var handler = CompletionHandler(db, cache, metrics);
            await handler.OnHandle(completion, CancellationToken.None);
            await handler.OnHandle(completion, CancellationToken.None);
        }

        await using var verify = Db();
        Assert.Single(await verify.Set<StatisticsEntry>().ToListAsync());
        Assert.Single(await verify.Set<EntryCreatedOutbox>().ToListAsync());
        Assert.NotNull((await verify.Set<SampleCompleteOutbox>().SingleAsync()).EntriesCreatedAtUtc);
    }

    [PostgreSqlStatisticsFact]
    public async Task ConcurrentReceiptInsertCannotLetLaterCompletionOvertakeEarlierSource()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var rider = Guid.NewGuid();
        var config = Simple();
        var firstTiming = TimingEvent(rider, Start, Start.AddSeconds(10));
        var laterTiming = TimingEvent(rider, Start.AddSeconds(10), Start.AddSeconds(20));
        var firstCompletion = Completion(firstTiming, config);
        var laterCompletion = Completion(laterTiming, config);
        var facilities = Facilities(config);
        var gate = new SampleInsertGate();
        await using var services = new ServiceCollection().AddMetrics().AddHybridCache().Services.BuildServiceProvider();
        var metrics = new Metrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var cache = services.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>();

        await using (var seed = Db())
        {
            seed.Add(config);
            await seed.SaveChangesAsync(token);
        }
        await using (var db = Db())
            await new TimingSampleHandler(db, facilities, metrics, NullLogger<TimingSampleHandler>.Instance, DomainHeads(db))
                .OnHandle(firstTiming, token);

        await using var inserting = Db(gate);
        var insert = new TimingSampleHandler(inserting, facilities, metrics, NullLogger<TimingSampleHandler>.Instance, DomainHeads(inserting))
            .OnHandle(laterTiming, token);
        try
        {
            await gate.SaveStarted.Task.WaitAsync(token); // TimingSampleHandler holds the rider advisory lock.
            await using var handling = Db();
            var later = CompletionHandler(handling, cache, metrics).OnHandle(laterCompletion, token);
            await WaitForAdvisoryWaitAsync(token);
            gate.ReleaseSave.TrySetResult();
            await insert;

            var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() => later);
            Assert.Contains("earlier unprocessed local sample", blocked.Message);
            Assert.Empty(await handling.Set<EntryCreatedOutbox>().ToListAsync(token));

            await CompletionHandler(handling, cache, metrics).OnHandle(firstCompletion, token);
            await CompletionHandler(handling, cache, metrics).OnHandle(laterCompletion, token);
        }
        finally
        {
            gate.ReleaseSave.TrySetResult();
            await insert;
        }

        await using var verify = Db();
        Assert.Equal(2, await verify.Set<StatisticsEntry>().CountAsync(token));
        var published = await verify.Set<EntryCreatedOutbox>().OrderBy(row => row.Id).ToListAsync(token);
        Assert.Equal(2, published.Count);
        Assert.Equal(firstCompletion.TimeStart, published[0].TimeStart);
        Assert.Equal(laterCompletion.TimeStart, published[1].TimeStart);
        Assert.Equal(2, await verify.Set<SampleCompleteOutbox>().CountAsync(row => row.EntriesCreatedAtUtc != null, token));
    }

    [PostgreSqlStatisticsFact]
    public async Task AmbiguousLocalCompletionReceiptsFailWithoutCreatingEntries()
    {
        var rider = Guid.NewGuid();
        var config = Simple();
        var timing = TimingEvent(rider, Start, Start.AddSeconds(10));
        var completion = Completion(timing, config);
        await using var services = new ServiceCollection().AddMetrics().AddHybridCache().Services.BuildServiceProvider();
        var metrics = new Metrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var cache = services.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>();
        await using (var db = Db())
        {
            db.Add(config);
            db.AddRange(Enumerable.Range(0, 2).Select(_ => new SampleCompleteOutbox
            {
                SampleId = Guid.NewGuid(), TransponderId = rider, TimeStart = completion.TimeStart,
                TimeEnd = completion.TimeEnd, CoursePointStart = completion.CoursePointStart,
                CoursePointEnd = completion.CoursePointEnd, Distance = completion.Distance
            }));
            await db.SaveChangesAsync();
        }

        await using (var db = Db())
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CompletionHandler(db, cache, metrics).OnHandle(completion, CancellationToken.None));
            Assert.Contains("matches multiple local outbox receipts", error.Message);
            Assert.Empty(await db.Set<StatisticsEntry>().ToListAsync());
            Assert.Empty(await db.Set<EntryCreatedOutbox>().ToListAsync());
        }
    }

    private static DomainSampleConsumer CompletionHandler(
        VeloTime.Module.Statistics.Storage.StatisticsDbContext db,
        Microsoft.Extensions.Caching.Hybrid.HybridCache cache, Metrics metrics) =>
        new(new(db, cache), new(db, new RequiredPointEmptyOwnershipReader(), DomainHeads(db)), metrics,
            NullLogger<SampleCompleteHandler>.Instance);

    private static IFacilitiesClient Facilities(SimpleStatisticsItemConfig config)
    {
        var proxy = DispatchProxy.Create<IFacilitiesClient, TimingSampleEventTimeTests.FacilitiesProxy>();
        ((TimingSampleEventTimeTests.FacilitiesProxy)proxy).Resolve = (_, _, _, _, _) =>
            Task.FromResult(new CoursePointDistance
            {
                CoursePointStartId = config.CoursePointStart, CoursePointEndId = config.CoursePointEnd,
                Distance = config.StatisticsItem.Distance, FacilityId = Guid.NewGuid()
            });
        return proxy;
    }

    private static TimingSampleComplete TimingEvent(Guid rider, DateTime start, DateTime end) =>
        new(start, end, Guid.NewGuid(), Guid.NewGuid(), rider);

    private static SampleComplete Completion(TimingSampleComplete timing, SimpleStatisticsItemConfig config) =>
        new(timing.TransponderId, timing.TimeStart, timing.TimeEnd, config.CoursePointStart,
            config.CoursePointEnd, config.StatisticsItem.Distance);

    private StatisticsDbContext Db(IInterceptor interceptor) => new(new DbContextOptionsBuilder<StatisticsDbContext>()
        .UseNpgsql(connection, options => options.MigrationsHistoryTable("__ef_migrations_history", "ef"))
        .UseSnakeCaseNamingConvention().AddInterceptors(capture, interceptor).Options);

    private async Task WaitForAdvisoryWaitAsync(CancellationToken cancellationToken)
    {
        await using var postgresConnection = new Npgsql.NpgsqlConnection(this.connection);
        await postgresConnection.OpenAsync(cancellationToken);
        while (true)
        {
            await using var command = new Npgsql.NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database() AND wait_event = 'advisory')",
                postgresConnection);
            if (await command.ExecuteScalarAsync(cancellationToken) is true) return;
            await Task.Delay(20, cancellationToken);
        }
    }

    private sealed class SampleInsertGate : SaveChangesInterceptor
    {
        public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            SaveStarted.TrySetResult();
            await ReleaseSave.Task.WaitAsync(cancellationToken);
            return result;
        }
    }
}
