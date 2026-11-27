using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Service.Recompute;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Interface.Messages;
using StatisticsMetrics = VeloTime.Module.Statistics.Metrics;

namespace VeloTime.Workers.Test;

/// <summary>The recompute worker's step against PostgreSQL: claiming, derivation, idempotence and failure backoff.</summary>
public sealed class StatisticsRecomputePostgreSqlTests : IAsyncLifetime
{
    // Sub-microsecond instants: PostgreSQL keeps microseconds, so every derived time must come from the stored fact.
    private static readonly DateTime T0 = new DateTime(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc).AddTicks(3);
    private static readonly Guid Installation = Guid.NewGuid();
    private static readonly Guid Facility = Guid.NewGuid();
    private static readonly Guid FinishLoop = Guid.NewGuid();
    private static readonly Guid Finish = Guid.NewGuid();
    private static readonly Guid Pursuit = Guid.NewGuid();

    private readonly string database = $"statistics_recompute_{Guid.NewGuid():N}";
    private readonly ServiceProvider services;
    private readonly Source source = new();
    private readonly ManualTime time = new(new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero));
    private string admin = "";
    private string connection = "";

    public StatisticsRecomputePostgreSqlTests()
    {
        var collection = new ServiceCollection().AddMetrics();
        collection.AddHybridCache();
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
    public async Task PassingsDeriveLapsAndAFullReplayChangesNoRowOrId()
    {
        var rider = Guid.NewGuid();
        var lap = await DefineLapAsync();
        await RecordAsync(rider, 0, 20, 41);

        Assert.Equal(RecomputeStepOutcome.Recomputed, (await StepAsync()).Outcome);

        await using (var db = Db())
        {
            Assert.Empty(await db.Set<RecomputeRequest>().ToListAsync());
            var facts = await db.Set<PassingFact>().OrderBy(f => f.Time).ToListAsync();
            var entries = await db.Set<StatisticsEntry>().OrderBy(e => e.TimeEnd).ToListAsync();
            Assert.Equal(2, entries.Count);
            Assert.All(entries, e => Assert.Equal(lap.Id, e.StatisticsItemConfigId));
            Assert.Equal(facts[0].Time, entries[0].TimeStart);
            Assert.Equal(facts[2].Time, entries[1].TimeEnd);
            Assert.Equal(DeterministicId.SimpleEntry(lap.Id, facts[1].PassingId, facts[2].PassingId), entries[1].Id);
            Assert.Equal(2, await db.Set<Sample>().CountAsync());
        }
        var before = await SnapshotAsync();

        // A full-range replay (as a definition change requests) splits per transponder and rebuilds identically.
        await using (var db = Db())
        {
            StatisticsRecomputeScheduler.All(db, RecomputeReason.Manual, null);
            await db.SaveChangesAsync();
        }
        Assert.Equal(RecomputeStepOutcome.Expanded, (await StepAsync()).Outcome);
        Assert.Equal(rider, (await StepAsync()).TransponderId);
        Assert.Equal(RecomputeStepOutcome.Idle, (await StepAsync()).Outcome);
        Assert.Equal(before, await SnapshotAsync());
    }

    [PostgreSqlStatisticsFact]
    public async Task LatePassingInsideASessionRebuildsOnlyThatSession()
    {
        var rider = Guid.NewGuid();
        await DefineLapAsync();
        await RecordAsync(rider, 0, 40);
        await StepAsync();
        await using (var db = Db())
            Assert.Equal(TimeSpan.FromSeconds(40), Assert.Single(await db.Set<StatisticsEntry>().ToListAsync()).Duration);

        await RecordAsync(rider, 19);
        Assert.Equal(RecomputeStepOutcome.Recomputed, (await StepAsync()).Outcome);

        await using var verify = Db();
        var entries = await verify.Set<StatisticsEntry>().OrderBy(e => e.TimeEnd).ToListAsync();
        Assert.Equal([TimeSpan.FromSeconds(19), TimeSpan.FromSeconds(21)], entries.Select(e => e.Duration));
        Assert.Equal(2, await verify.Set<Sample>().CountAsync());
    }

    [PostgreSqlStatisticsFact]
    public async Task FailureRollsBackAndBacksOffUntilDue()
    {
        var rider = Guid.NewGuid();
        await DefineLapAsync();
        await RecordAsync(rider, 0, 20);
        source.Failure = new HttpRequestException("Timing unavailable");

        var failed = await StepAsync();

        Assert.Equal(RecomputeStepOutcome.Failed, failed.Outcome);
        Assert.Equal(rider, failed.TransponderId);
        await using (var db = Db())
        {
            Assert.Empty(await db.Set<Sample>().ToListAsync());
            var requests = await db.Set<RecomputeRequest>().ToListAsync();
            Assert.Equal(2, requests.Count);
            Assert.All(requests, r =>
            {
                Assert.Equal(1, r.Attempts);
                Assert.Equal(time.GetUtcNow().UtcDateTime + TimeSpan.FromSeconds(5), r.NotBeforeUtc);
                Assert.Contains("Timing unavailable", r.LastError);
            });
        }
        Assert.Equal(RecomputeStepOutcome.Idle, (await StepAsync()).Outcome);

        source.Failure = null;
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(RecomputeStepOutcome.Recomputed, (await StepAsync()).Outcome);
        await using var verify = Db();
        Assert.Single(await verify.Set<StatisticsEntry>().ToListAsync());
        Assert.Empty(await verify.Set<RecomputeRequest>().ToListAsync());
    }

    private async Task<SimpleStatisticsItemConfig> DefineLapAsync()
    {
        await using var db = Db();
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap", Distance = 250 };
        var config = new SimpleStatisticsItemConfig
        {
            Id = Guid.NewGuid(), StatisticsItem = item, StatisticsItemId = item.Id, CoursePointStart = Finish, CoursePointEnd = Finish
        };
        db.Add(config);
        await db.SaveChangesAsync();
        return config;
    }

    private async Task RecordAsync(Guid rider, params int[] seconds)
    {
        foreach (var second in seconds)
        {
            await using var db = Db();
            await new PassingRecordedHandler(db, new StatisticsRecomputeSignal(), Metrics(), NullLogger<PassingRecordedHandler>.Instance, time)
                .OnHandle(new PassingRecordedV1(Guid.NewGuid(), Installation, FinishLoop, rider,
                    T0.AddSeconds(second), false, false, false), CancellationToken.None);
        }
    }

    private async Task<RecomputeStepResult> StepAsync()
    {
        await using var db = Db();
        var history = new StatisticsConfigurationHistory(services.GetRequiredService<HybridCache>(), source);
        await history.EvictAsync([Installation], [Facility]);
        return await new StatisticsRecomputeService(db, history, new StatisticsOwnershipService(db, new NoOwners()),
            new StatisticsValidityScheduler(db), Microsoft.Extensions.Options.Options.Create(new StatisticsRecomputeOptions()),
            Metrics(), NullLogger<StatisticsRecomputeService>.Instance, time).ProcessNextAsync(CancellationToken.None);
    }

    private async Task<string> SnapshotAsync()
    {
        await using var db = Db();
        var samples = await db.Set<Sample>().OrderBy(s => s.Id).Select(s => $"{s.Id}|{s.TimeStart:O}|{s.TimeEnd:O}|{s.Distance}").ToListAsync();
        var entries = await db.Set<StatisticsEntry>().OrderBy(e => e.Id)
            .Select(e => $"{e.Id}|{e.TimeStart:O}|{e.TimeEnd:O}|{e.Speed}|{e.IsValid}").ToListAsync();
        return string.Join("\n", samples.Concat(entries));
    }

    private StatisticsMetrics Metrics() => new(services.GetRequiredService<IMeterFactory>());

    private StatisticsDbContext Db() => new(new DbContextOptionsBuilder<StatisticsDbContext>()
        .UseNpgsql(connection, options => options.MigrationsHistoryTable("__ef_migrations_history", "ef"))
        .UseSnakeCaseNamingConvention().Options);

    private static async Task Sql(string connectionString, string sql)
    {
        await using var raw = new NpgsqlConnection(connectionString);
        await raw.OpenAsync();
        await using var command = new NpgsqlCommand(sql, raw);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class Source : IStatisticsConfigurationSource
    {
        public Exception? Failure { get; set; }

        public Task<InstallationFacilityHistory> GetInstallationAsync(Guid installationId, CancellationToken cancellationToken) =>
            Failure is { } failure ? Task.FromException<InstallationFacilityHistory>(failure)
                : Task.FromResult(new InstallationFacilityHistory { InstallationId = installationId, FacilityId = Facility });

        public Task<FacilityCourseHistory> GetFacilityCourseAsync(Guid facilityId, CancellationToken cancellationToken) =>
            Task.FromResult(new FacilityCourseHistory
            {
                FacilityId = facilityId,
                Associations = [new() { TimingPointId = FinishLoop, CoursePointId = Finish }],
                Layouts =
                [
                    new()
                    {
                        LayoutId = Guid.NewGuid(),
                        Segments =
                        [
                            new() { Order = 0, StartPointId = Finish, EndPointId = Pursuit, Length = 125 },
                            new() { Order = 1, StartPointId = Pursuit, EndPointId = Finish, Length = 125 }
                        ]
                    }
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
