using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using VeloTime.Module.Common.Operations;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Service.Recompute;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Interface.Messages;
using StatisticsMetrics = VeloTime.Module.Statistics.Metrics;

namespace VeloTime.Workers.Test;

/// <summary>
/// Replay-safe plan Phase 5 against PostgreSQL: admin recompute jobs and their progress, live-first claiming,
/// the unresolved-passing backlog, and the backlog and outbox gauges.
/// </summary>
public sealed class StatisticsRecomputeOperationsPostgreSqlTests : IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Installation = Guid.NewGuid();
    private static readonly Guid Facility = Guid.NewGuid();
    private static readonly Guid FinishLoop = Guid.NewGuid();
    private static readonly Guid PursuitLoop = Guid.NewGuid();
    private static readonly Guid Finish = Guid.NewGuid();
    private static readonly Guid Pursuit = Guid.NewGuid();

    private readonly string database = $"statistics_operations_{Guid.NewGuid():N}";
    private readonly ServiceProvider services;
    private readonly Source source = new();
    private readonly ManualTime time = new(new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero));
    private string admin = "";
    private string connection = "";

    public StatisticsRecomputeOperationsPostgreSqlTests()
    {
        var collection = new ServiceCollection().AddMetrics();
        collection.AddHybridCache();
        collection.AddDbContext<StatisticsDbContext>(options => Configure(options));
        services = collection.BuildServiceProvider();
    }

    public async Task InitializeAsync()
    {
        admin = Environment.GetEnvironmentVariable("VELOTIME_TEST_STATISTICS_POSTGRES") ?? "";
        if (string.IsNullOrWhiteSpace(admin)) return;
        await Sql(admin, $"CREATE DATABASE \"{database}\"");
        connection = new NpgsqlConnectionStringBuilder(admin) { Database = database }.ConnectionString;
        await using var db = Db();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await services.DisposeAsync();
        if (connection.Length == 0) return;
        using var pool = new NpgsqlConnection(connection);
        NpgsqlConnection.ClearPool(pool);
        await Sql(admin, $"DROP DATABASE \"{database}\" WITH (FORCE)");
    }

    [PostgreSqlStatisticsFact]
    public async Task RangeJobSplitsPerTransponderReportsProgressAndCompletes()
    {
        await DefineLapAsync();
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());
        await RecordAsync(first, FinishLoop, 0, 20);
        await RecordAsync(second, FinishLoop, 5, 26);
        while ((await StepAsync()).Outcome != RecomputeStepOutcome.Idle) { }
        var before = await SnapshotAsync();

        var job = await CreateJobAsync();
        Assert.Equal(RecomputeJobStatus.Queued, (await JobAsync(job)).Status);

        Assert.Equal(RecomputeStepOutcome.Expanded, (await StepAsync()).Outcome);
        var split = await JobAsync(job);
        Assert.Equal((RecomputeJobStatus.Running, (int?)2, 2), (split.Status, split.TotalTransponders, split.RemainingTransponders));

        Assert.Equal(RecomputeStepOutcome.Recomputed, (await StepAsync()).Outcome);
        Assert.Equal(1, (await JobAsync(job)).RemainingTransponders);
        Assert.Equal(RecomputeStepOutcome.Recomputed, (await StepAsync()).Outcome);
        Assert.Equal(RecomputeStepOutcome.Idle, (await StepAsync()).Outcome);

        var completed = await JobAsync(job);
        Assert.Equal((RecomputeJobStatus.Completed, 0), (completed.Status, completed.RemainingTransponders));
        Assert.Equal(time.GetUtcNow(), completed.CompletedAtUtc);
        // A deliberate recompute of unchanged facts and config changes nothing.
        Assert.Equal(before, await SnapshotAsync());
    }

    [PostgreSqlStatisticsFact]
    public async Task RangeJobWithoutFactsCompletesWhenSplit()
    {
        var job = await CreateJobAsync(installation: Guid.NewGuid());
        Assert.Equal(RecomputeStepOutcome.Expanded, (await StepAsync()).Outcome);
        var result = await JobAsync(job);
        Assert.Equal((RecomputeJobStatus.Completed, (int?)0), (result.Status, result.TotalTransponders));
        Assert.NotNull(result.CompletedAtUtc);
    }

    [PostgreSqlStatisticsFact]
    public async Task LivePassingsAreRecomputedBeforeEarlierBulkWork()
    {
        await DefineLapAsync();
        var (bulk, live) = (Guid.NewGuid(), Guid.NewGuid());
        await RecordAsync(bulk, FinishLoop, 0, 20);
        while ((await StepAsync()).Outcome != RecomputeStepOutcome.Idle) { }

        await CreateJobAsync(transponder: bulk);
        await RecordAsync(live, FinishLoop, 100, 121);

        Assert.Equal(live, (await StepAsync()).TransponderId);
        Assert.Equal(bulk, (await StepAsync()).TransponderId);
    }

    [PostgreSqlStatisticsFact]
    public async Task UnresolvedPassingsFollowTheLatestRecompute()
    {
        await DefineLapAsync();
        var rider = Guid.NewGuid();
        source.PursuitAssociated = false;
        await RecordAsync(rider, FinishLoop, 0);
        await RecordAsync(rider, PursuitLoop, 10);
        await RecordAsync(rider, FinishLoop, 20);
        while ((await StepAsync()).Outcome != RecomputeStepOutcome.Idle) { }

        await using (var db = Db())
        {
            var unresolved = Assert.Single(await db.Set<UnresolvedPassing>().ToListAsync());
            Assert.Equal((rider, PursuitLoop, UnresolvedPassingReason.NoCoursePoint, (Guid?)Facility, time.GetUtcNow().UtcDateTime),
                (unresolved.TransponderId, unresolved.TimingPointId, unresolved.Reason, unresolved.FacilityId, unresolved.DetectedAtUtc));
            Assert.Equal(1, (await StatisticsOperations.ReadBacklogAsync(db, CancellationToken.None)).UnresolvedPassings);
        }

        // A later recompute that still cannot place it keeps the first detection time.
        time.Advance(TimeSpan.FromHours(1));
        await CreateJobAsync(transponder: rider);
        while ((await StepAsync()).Outcome != RecomputeStepOutcome.Idle) { }
        await using (var db = Db())
            Assert.Equal(time.GetUtcNow().UtcDateTime.AddHours(-1), (await db.Set<UnresolvedPassing>().SingleAsync()).DetectedAtUtc);

        source.PursuitAssociated = true;
        await CreateJobAsync(transponder: rider);
        while ((await StepAsync()).Outcome != RecomputeStepOutcome.Idle) { }
        await using (var db = Db())
            Assert.Empty(await db.Set<UnresolvedPassing>().ToListAsync());
    }

    [PostgreSqlStatisticsFact]
    public async Task BacklogMonitorPublishesDirtyBacklogAgesAndReviews()
    {
        await using (var db = Db())
        {
            db.AddRange(
                new RecomputeRequest { TransponderId = Guid.NewGuid(), Reason = RecomputeReason.Passing, CreatedAtUtc = time.GetUtcNow().UtcDateTime.AddMinutes(-10) },
                new RecomputeRequest { TransponderId = Guid.NewGuid(), Reason = RecomputeReason.Manual, CreatedAtUtc = time.GetUtcNow().UtcDateTime, Attempts = 1, LastError = "boom" },
                new RecomputeRequest { Reason = RecomputeReason.Configuration, CreatedAtUtc = time.GetUtcNow().UtcDateTime },
                new ManualDecisionReview { Id = Guid.NewGuid(), EntryId = Guid.NewGuid(), CreatedAtUtc = time.GetUtcNow().UtcDateTime.AddHours(-2) });
            await db.SaveChangesAsync();
        }
        var metrics = Metrics();
        var monitor = new StatisticsBacklogMonitor(services.GetRequiredService<IServiceScopeFactory>(), metrics,
            Microsoft.Extensions.Options.Options.Create(new StatisticsBacklogMonitorOptions()),
            NullLogger<StatisticsBacklogMonitor>.Instance, time);

        using var gauges = new GaugeReader("VeloTime.Module.Statistics");
        Assert.Empty(gauges.Read("velotime.module.statistics.recompute.backlog"));
        await monitor.SampleAsync(CancellationToken.None);

        var backlog = gauges.Read("velotime.module.statistics.recompute.backlog");
        Assert.Equal(2, backlog.Single(m => m.Tags["kind"] == "transponder").Value);
        Assert.Equal(1, backlog.Single(m => m.Tags["kind"] == "range").Value);
        Assert.Equal(1, Assert.Single(gauges.Read("velotime.module.statistics.recompute.failing_requests")).Value);
        Assert.Equal(600, Assert.Single(gauges.Read("velotime.module.statistics.recompute.oldest_request_age")).Value, 3);
        Assert.Equal(0, Assert.Single(gauges.Read("velotime.module.statistics.unresolved_passings.backlog")).Value);
        Assert.Equal(1, Assert.Single(gauges.Read("velotime.module.statistics.manual_decision_reviews.backlog")).Value);
        Assert.Equal(7200, Assert.Single(gauges.Read("velotime.module.statistics.manual_decision_reviews.oldest_age")).Value, 3);
    }

    [PostgreSqlStatisticsFact]
    public async Task OutboxMonitorReportsPendingOldestAndAbortedMessages()
    {
        await Sql(connection, """
            CREATE SCHEMA probe;
            CREATE TABLE probe.smb_outbox (id uuid PRIMARY KEY, timestamp timestamp with time zone NOT NULL,
                delivery_attempt integer NOT NULL, delivery_complete boolean NOT NULL, delivery_aborted boolean NOT NULL);
            INSERT INTO probe.smb_outbox VALUES
                (gen_random_uuid(), '2026-11-20T23:55:00Z', 3, false, false),
                (gen_random_uuid(), '2026-11-20T23:59:00Z', 0, false, false),
                (gen_random_uuid(), '2026-11-20T20:00:00Z', 1, true, false),
                (gen_random_uuid(), '2026-11-20T21:00:00Z', 9, false, true);
            """);
        var metrics = new MessagingMetrics(services.GetRequiredService<IMeterFactory>());
        var monitor = new OutboxMonitor<StatisticsDbContext>(services.GetRequiredService<IServiceScopeFactory>(), metrics,
            new OutboxMonitorTarget("probe", "probe", TimeSpan.FromSeconds(30)), NullLogger<OutboxMonitor<StatisticsDbContext>>.Instance, time);

        using var gauges = new GaugeReader(MessagingMetrics.MeterName);
        await monitor.SampleAsync(CancellationToken.None);

        Assert.Equal(2, Assert.Single(gauges.Read("velotime.messaging.outbox.pending")).Value);
        Assert.Equal(1, Assert.Single(gauges.Read("velotime.messaging.outbox.aborted")).Value);
        var age = Assert.Single(gauges.Read("velotime.messaging.outbox.oldest_age"));
        Assert.Equal(300, age.Value, 3);
        Assert.Equal("probe", age.Tags["outbox"]);

        await Sql(connection, "UPDATE probe.smb_outbox SET delivery_complete = true WHERE NOT delivery_aborted");
        await monitor.SampleAsync(CancellationToken.None);
        Assert.Equal(0, Assert.Single(gauges.Read("velotime.messaging.outbox.oldest_age")).Value);
    }

    private async Task<Guid> CreateJobAsync(Guid? transponder = null, Guid? installation = null)
    {
        await using var db = Db();
        var job = StatisticsOperations.CreateJob(db, transponder, installation, null, null, null, null, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private async Task<RecomputeJobDTO> JobAsync(Guid id)
    {
        await using var db = Db();
        return (await StatisticsOperations.ReadJobsAsync(db, 1, CancellationToken.None, id)).Single();
    }

    private async Task DefineLapAsync()
    {
        await using var db = Db();
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap", Distance = 250 };
        var config = new SimpleStatisticsItemConfig
        {
            Id = Guid.NewGuid(), StatisticsItem = item, StatisticsItemId = item.Id, CoursePointStart = Finish, CoursePointEnd = Finish
        };
        db.Add(config);
        db.Add(StatisticsConfigVersioning.Initial(item, config, null, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task RecordAsync(Guid rider, Guid loop, params int[] seconds)
    {
        foreach (var second in seconds)
        {
            await using var db = Db();
            await new PassingRecordedHandler(db, new StatisticsRecomputeSignal(), Metrics(), NullLogger<PassingRecordedHandler>.Instance, time)
                .OnHandle(new PassingRecordedV1(Guid.NewGuid(), Installation, loop, rider, T0.AddSeconds(second), false, false, false),
                    CancellationToken.None);
        }
    }

    private async Task<RecomputeStepResult> StepAsync()
    {
        await using var db = Db();
        var history = new StatisticsConfigurationHistory(services.GetRequiredService<HybridCache>(), source);
        await history.EvictAsync([Installation], [Facility]);
        return await new StatisticsRecomputeService(db, history, new StatisticsOwnershipService(db, new NoOwners()),
            Microsoft.Extensions.Options.Options.Create(new StatisticsRecomputeOptions()),
            Metrics(), NullLogger<StatisticsRecomputeService>.Instance, time).ProcessNextAsync(CancellationToken.None);
    }

    private async Task<string> SnapshotAsync()
    {
        await using var db = Db();
        var samples = await db.Set<Sample>().OrderBy(s => s.Id).Select(s => $"{s.Id}|{s.TimeStart:O}|{s.TimeEnd:O}|{s.Distance}").ToListAsync();
        var entries = await db.Set<StatisticsEntry>().OrderBy(e => e.Id)
            .Select(e => $"{e.Id}|{e.TimeStart:O}|{e.TimeEnd:O}|{e.Speed}|{e.IsValid}").ToListAsync();
        Assert.NotEmpty(entries);
        return string.Join("\n", samples.Concat(entries));
    }

    private StatisticsMetrics Metrics() => new(services.GetRequiredService<IMeterFactory>());

    private DbContextOptionsBuilder Configure(DbContextOptionsBuilder options) => options
        .UseNpgsql(connection, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "ef"))
        .UseSnakeCaseNamingConvention();

    private StatisticsDbContext Db() =>
        new((DbContextOptions<StatisticsDbContext>)Configure(new DbContextOptionsBuilder<StatisticsDbContext>()).Options);

    private static async Task Sql(string connectionString, string sql)
    {
        await using var raw = new NpgsqlConnection(connectionString);
        await raw.OpenAsync();
        await using var command = new NpgsqlCommand(sql, raw);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class Source : IStatisticsConfigurationSource
    {
        public bool PursuitAssociated { get; set; } = true;

        public Task<InstallationFacilityHistory> GetInstallationAsync(Guid installationId, CancellationToken cancellationToken) =>
            Task.FromResult(new InstallationFacilityHistory { InstallationId = installationId, Assignments = [new() { FacilityId = Facility }] });

        public Task<FacilityCourseHistory> GetFacilityCourseAsync(Guid facilityId, CancellationToken cancellationToken) =>
            Task.FromResult(new FacilityCourseHistory
            {
                FacilityId = facilityId,
                Associations = PursuitAssociated
                    ? [new() { TimingPointId = FinishLoop, CoursePointId = Finish }, new() { TimingPointId = PursuitLoop, CoursePointId = Pursuit }]
                    : [new() { TimingPointId = FinishLoop, CoursePointId = Finish }],
                Layouts =
                [
                    CourseLayoutVersion.Unbounded(Guid.Parse("50000000-0000-0000-0000-000000000001"),
                    [
                        new() { Order = 0, StartPointId = Finish, EndPointId = Pursuit, Length = 125 },
                        new() { Order = 1, StartPointId = Pursuit, EndPointId = Finish, Length = 125 }
                    ])
                ]
            });
    }

    private sealed class NoOwners : ITransponderOwnershipReader
    {
        public Task<IReadOnlyList<VeloTime.Module.Timing.Interface.Data.TransponderOwnershipDTO>> ReadAsync(
            Guid transponderId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VeloTime.Module.Timing.Interface.Data.TransponderOwnershipDTO>>([]);
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan by) => current += by;
    }
}
