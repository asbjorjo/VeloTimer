using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using VeloTime.Module.Statistics;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Workers.Test;

public sealed class StatisticsValidityPostgreSqlTests : IAsyncLifetime
{
    private readonly string database = $"threshold_worker_{Guid.NewGuid():N}";
    private string admin = "";
    private string connection = "";
    private readonly Capture capture = new();
    private static readonly DateTime Start = new(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);

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
        if (connection.Length == 0) return;
        using var pool = new NpgsqlConnection(connection);
        NpgsqlConnection.ClearPool(pool);
        await Sql(admin, $"DROP DATABASE \"{database}\" WITH (FORCE)");
    }

    [PostgreSqlStatisticsFact]
    public async Task CreationPrecisionSimpleBoundariesMatchReloadAndRemainStableAfterRevalidation()
    {
        var config = Simple();
        config.StatisticsItem.MaxDuration = TimeSpan.FromSeconds(10);
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var created = new List<StatisticsEntry>();
        await using (var db = Db())
        {
            db.Add(config);
            await db.SaveChangesAsync();
            foreach (var ticks in new long[] { 100_000_001, 100_000_009, 100_000_010, 1, 9, -1, -9 })
            {
                var rider = Guid.NewGuid();
                db.Add(new Sample
                {
                    Id = Guid.NewGuid(), TransponderId = rider, FacilityId = Guid.NewGuid(),
                    CoursePointStartId = config.CoursePointStart, CoursePointEndId = Guid.NewGuid(),
                    TimeStart = Start, TimeEnd = Start.AddSeconds(1), Duration = TimeSpan.FromSeconds(1),
                    Distance = 250, Speed = 250
                });
                await db.SaveChangesAsync();
                await using var tx = await db.Database.BeginTransactionAsync();
                var entry = Assert.Single(await new StatisticsService(db, services.GetRequiredService<HybridCache>())
                    .ProcessCompletedAsync(rider, Start.AddSeconds(2), Start.AddTicks(ticks),
                        config.CoursePointStart, config.CoursePointEnd));
                // Modern timestamps truncate their positive offset from the 2000 epoch.
                // An end just before Start therefore persists one microsecond before it;
                // this differs from truncating the negative elapsed interval toward zero.
                var expectedTicks = ticks < 0 ? -10 : ticks / 10 * 10;
                var expectedReason = expectedTicks <= 0 ? StatisticsEntryInvalidReason.NonPositiveDuration
                    : expectedTicks > 100_000_000 ? StatisticsEntryInvalidReason.AboveMaximumDuration
                    : (StatisticsEntryInvalidReason?)null;
                Assert.Equal(Start, entry.TimeStart);
                Assert.Equal(Start.AddTicks(expectedTicks), entry.TimeEnd);
                Assert.Equal(TimeSpan.FromTicks(expectedTicks), entry.Duration);
                Assert.Equal(250 / entry.Duration.TotalSeconds, entry.Speed);
                Assert.Equal(expectedReason, entry.InvalidReason);
                Assert.Equal(expectedReason is null, entry.IsValid);
                await tx.CommitAsync();
                created.Add(entry);
            }
        }
        await AssertReloadMatches(created);
        await Change(config.StatisticsItemId, null);
        Assert.All(await Drain(config.StatisticsItemId, 2), batch => Assert.Equal(0, batch.Reclassified));
        await AssertReloadMatches(created);
    }

    [PostgreSqlStatisticsFact]
    public async Task CreationPrecisionMultiAtExactMaximumRetainsFixedLineageAndStableMeasurements()
    {
        var parent = Simple();
        parent.StatisticsItem.MaxDuration = TimeSpan.FromSeconds(10);
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Double", Distance = 500,
            MaxDuration = TimeSpan.FromSeconds(20) };
        var multi = new MultiStatisticsItemConfig
        {
            Id = Guid.NewGuid(), StatisticsItem = item, StatisticsItemId = item.Id,
            ParentConfig = parent, ParentConfigId = parent.Id, Repetitions = 2
        };
        var rider = Guid.NewGuid();
        var created = new List<StatisticsEntry>();
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        await using (var db = Db())
        {
            db.AddRange(parent, multi);
            await db.SaveChangesAsync();
            for (var lap = 0; lap < 2; lap++)
            {
                var start = Start.AddSeconds(lap * 10);
                db.Add(new Sample
                {
                    Id = Guid.NewGuid(), TransponderId = rider, FacilityId = Guid.NewGuid(),
                    CoursePointStartId = parent.CoursePointStart, CoursePointEndId = Guid.NewGuid(),
                    TimeStart = start, TimeEnd = start.AddSeconds(1), Duration = TimeSpan.FromSeconds(1),
                    Distance = 250, Speed = 250
                });
                await db.SaveChangesAsync();
                await using var tx = await db.Database.BeginTransactionAsync();
                var entry = Assert.Single(await new StatisticsService(db, services.GetRequiredService<HybridCache>())
                    .ProcessCompletedAsync(rider, start.AddSeconds(2), start.AddSeconds(10).AddTicks(9),
                        parent.CoursePointStart, parent.CoursePointEnd));
                Assert.True(entry.IsValid);
                Assert.Equal(TimeSpan.FromSeconds(10), entry.Duration);
                created.Add(entry);
                await tx.CommitAsync();
            }
            await using var aggregateTx = await db.Database.BeginTransactionAsync();
            var aggregate = Assert.Single(await new StatisticsService(db, services.GetRequiredService<HybridCache>())
                .ProcessEntryAsync(rider, parent.StatisticsItemId, parent.Id, Start, Start.AddSeconds(20).AddTicks(9)));
            Assert.True(aggregate.IsValid);
            Assert.Null(aggregate.InvalidReason);
            Assert.Equal(Start, aggregate.TimeStart);
            Assert.Equal(Start.AddSeconds(20), aggregate.TimeEnd);
            Assert.Equal(TimeSpan.FromSeconds(20), aggregate.Duration);
            Assert.Equal(25, aggregate.Speed);
            Assert.True(aggregate.HasCompleteContributorLineage);
            Assert.Equal(created.AsEnumerable().Reverse().Select(e => e.Id),
                aggregate.Contributors.OrderBy(c => c.Position).Select(c => c.ContributorEntryId));
            created.Add(aggregate);
            await aggregateTx.CommitAsync();
        }
        await AssertReloadMatches(created);
        await Change(parent.StatisticsItemId, null);
        await Change(item.Id, null);
        Assert.All(await Drain(parent.StatisticsItemId, 2), batch => Assert.Equal(0, batch.Reclassified));
        Assert.All(await Drain(item.Id, 2), batch => Assert.Equal(0, batch.Reclassified));
        await AssertReloadMatches(created);
        await using var verify = Db();
        var links = await verify.Set<StatisticsEntryContributor>().OrderBy(c => c.Position).ToListAsync();
        Assert.Equal(created.Take(2).Reverse().Select(e => e.Id), links.Select(c => c.ContributorEntryId));
        Assert.Equal(new[] { 0, 1 }, links.Select(c => c.Position));
        Assert.All(links, link => Assert.Equal(created[2].Id, link.StatisticsEntryId));
    }

    [PostgreSqlStatisticsFact]
    public async Task CreationPrecisionHelpersAgreeWithNpgsqlOnBothSidesOfPostgresEpoch()
    {
        var epoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await using var raw = new NpgsqlConnection(connection);
        await raw.OpenAsync();
        foreach (var ticks in new long[] { -11, -10, -9, -1, 0, 1, 9, 10, 11 })
        {
            var timestamp = epoch.AddTicks(ticks);
            var interval = TimeSpan.FromTicks(ticks);
            await using var command = new NpgsqlCommand("SELECT @timestamp::timestamptz, @duration::interval", raw);
            command.Parameters.AddWithValue("timestamp", timestamp);
            command.Parameters.AddWithValue("duration", interval);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var storedTimestamp = reader.GetDateTime(0);
            var storedInterval = reader.GetFieldValue<TimeSpan>(1);
            Assert.Equal(epoch.AddTicks(ticks / 10 * 10), storedTimestamp);
            Assert.Equal(TimeSpan.FromTicks(ticks / 10 * 10), storedInterval);
            Assert.Equal(storedTimestamp, StatisticsStoragePrecision.Timestamp(timestamp));
            Assert.Equal(storedInterval, StatisticsStoragePrecision.Interval(interval));
            Assert.Equal(DateTimeKind.Utc, storedTimestamp.Kind);
            Assert.Equal(timestamp.Kind, StatisticsStoragePrecision.Timestamp(timestamp).Kind);
        }
    }

    private async Task AssertReloadMatches(IEnumerable<StatisticsEntry> entries)
    {
        await using var verify = Db();
        var stored = await verify.Set<StatisticsEntry>().AsNoTracking().ToDictionaryAsync(e => e.Id);
        foreach (var entry in entries)
        {
            var reload = stored[entry.Id];
            Assert.Equal((entry.TimeStart, entry.TimeEnd, entry.Duration, entry.Speed, entry.IsValid, entry.InvalidReason),
                (reload.TimeStart, reload.TimeEnd, reload.Duration, reload.Speed, reload.IsValid, reload.InvalidReason));
            Assert.Equal(entry.TimeEnd - entry.TimeStart, reload.Duration);
            Assert.Equal(DateTimeKind.Utc, reload.TimeStart.Kind);
            Assert.Equal(DateTimeKind.Utc, reload.TimeEnd.Kind);
        }
    }

    [PostgreSqlStatisticsFact]
    public async Task TightenLoosenBoundedRestartSupersedingEditAndDuplicateEventsWith4096Rows()
    {
        var config = Simple();
        await using (var db = Db())
        {
            db.Add(config);
            db.AddRange(Enumerable.Range(1, 4096).Select(i => Entry(config, i)));
            await db.SaveChangesAsync();
        }
        await Change(config.StatisticsItemId, TimeSpan.FromSeconds(11));
        capture.Commands.Clear();
        var first = await Batch(config.StatisticsItemId, 127);
        Assert.Equal(127, first.Scanned);
        Assert.Equal(127, first.Reclassified);
        Assert.Equal(StatisticsRevalidationBatchOutcome.Progressed, first.Outcome);
        var checkpoint = await Job(config.StatisticsItemId);
        Assert.NotNull(checkpoint.LastEntryId);
        await using (var db = Db())
        {
            Assert.Equal(127, await db.Set<StatisticsEntry>().CountAsync(e => !e.IsValid));
            Assert.Equal(3969, await db.Set<StatisticsEntry>().CountAsync(e => e.IsValid)); // Stored old values until processed.
            Assert.Empty(db.ChangeTracker.Entries());
            var handler = new StatisticsValidityThresholdsChangedHandler(db, new(db), new(),
                NullLogger<StatisticsValidityThresholdsChangedHandler>.Instance);
            var message = new StatisticsValidityThresholdsChangedV1(Guid.NewGuid(), config.StatisticsItemId, config.Id);
            await handler.OnHandle(message, CancellationToken.None);
            await handler.OnHandle(message, CancellationToken.None);
            await handler.OnHandle(message with { EventId = Guid.NewGuid() }, CancellationToken.None);
        }
        Assert.Equal(checkpoint.Generation, (await Job(config.StatisticsItemId)).Generation);
        Assert.Equal(checkpoint.LastEntryId, (await Job(config.StatisticsItemId)).LastEntryId);
        var second = await Batch(config.StatisticsItemId, 127); // New context resumes, not restarts.
        Assert.Equal(127, second.Reclassified);
        Assert.NotEqual(checkpoint.LastEntryId, (await Job(config.StatisticsItemId)).LastEntryId);
        await Change(config.StatisticsItemId, null); // Superseding loosen starts from beginning.
        var newer = await Job(config.StatisticsItemId);
        Assert.True(newer.Generation > checkpoint.Generation);
        Assert.Null(newer.LastEntryId);
        var batches = await Drain(config.StatisticsItemId, 127);
        Assert.All(batches, b => Assert.InRange(b.Scanned, 0, 127));
        Assert.Equal(4096, batches.Sum(b => b.Scanned));
        await using (var db = Db())
        {
            Assert.Equal(4096, await db.Set<StatisticsEntry>().CountAsync(e => e.IsValid));
            Assert.Equal(4096, await db.Set<StatisticsEntry>().CountAsync());
        }
        var pageSql = capture.Commands.Where(sql => sql.Contains("ORDER BY e.") && sql.Contains("LIMIT")).ToArray();
        Assert.NotEmpty(pageSql);
        Assert.Contains(pageSql, sql => sql.Contains("> @cursor"));
        Assert.All(pageSql, sql => Assert.DoesNotContain("OFFSET", sql, StringComparison.OrdinalIgnoreCase));
        Assert.All(pageSql, sql => Assert.Contains("statistics_item_id", sql));
        Console.WriteLine($"4096 rows, batch bound 127, {batches.Count} restart/loosen transactions, max scanned {batches.Max(b => b.Scanned)}.");
    }

    [PostgreSqlStatisticsFact]
    public async Task MultiUsesFixedInvalidConstituentsAndParentPropagationRestoresWithoutNewAggregates()
    {
        var parent = Simple();
        var multi = new MultiStatisticsItemConfig
        {
            Id = Guid.NewGuid(), ParentConfig = parent, ParentConfigId = parent.Id, Repetitions = 2,
            StatisticsItem = new StatisticsItem { Id = Guid.NewGuid(), Name = "Double", Distance = 500 }
        };
        multi.StatisticsItemId = multi.StatisticsItem.Id;
        var first = Entry(parent, 1);
        var second = Entry(parent, 2);
        // Same rider is necessary for fixed aggregation.
        first = Entry(parent, 1, second.TransponderId);
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        await using (var db = Db())
        {
            db.AddRange(parent, multi, first, second);
            await db.SaveChangesAsync();
            await using var tx = await db.Database.BeginTransactionAsync();
            var service = new StatisticsService(db, services.GetRequiredService<HybridCache>());
            var aggregate = Assert.Single(await service.ProcessEntryAsync(second.TransponderId, parent.StatisticsItemId,
                parent.Id, first.TimeStart, second.TimeEnd));
            Assert.True(aggregate.IsValid);
            Assert.True(aggregate.HasCompleteContributorLineage);
            Assert.Equal(new[] { second.Id, first.Id }, aggregate.Contributors.OrderBy(c => c.Position).Select(c => c.ContributorEntryId));
            await tx.CommitAsync();
        }
        await Change(parent.StatisticsItemId, TimeSpan.FromSeconds(11));
        await Drain(parent.StatisticsItemId, 1);
        Assert.True((await Job(multi.StatisticsItemId)).IsPending);
        await Drain(multi.StatisticsItemId, 1);
        await using (var db = Db())
        {
            var aggregate = await db.Set<StatisticsEntry>().Include(e => e.Contributors).SingleAsync(e => e.StatisticsItemConfigId == multi.Id);
            Assert.False(aggregate.IsValid);
            Assert.Equal(StatisticsEntryInvalidReason.InvalidContributor, aggregate.InvalidReason);
            Assert.Equal(new[] { second.Id, first.Id }, aggregate.Contributors.OrderBy(c => c.Position).Select(c => c.ContributorEntryId));
            Assert.Equal(3, await db.Set<StatisticsEntry>().CountAsync());
        }
        await Change(parent.StatisticsItemId, null);
        await Drain(parent.StatisticsItemId, 1);
        await Drain(multi.StatisticsItemId, 1);
        await using (var db = Db())
        {
            Assert.Equal(3, await db.Set<StatisticsEntry>().CountAsync(e => e.IsValid));
            Assert.Equal(2, await db.Set<StatisticsEntryContributor>().CountAsync());
        }
    }

    [PostgreSqlStatisticsFact]
    public async Task InvalidLapIsRetainedAndNeverSkippedToAssembleMulti()
    {
        var parent = Simple();
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Double", Distance = 500,
            MinDuration = TimeSpan.FromSeconds(1), MaxDuration = TimeSpan.FromSeconds(100) };
        var multi = new MultiStatisticsItemConfig { Id = Guid.NewGuid(), StatisticsItem = item, StatisticsItemId = item.Id,
            ParentConfig = parent, ParentConfigId = parent.Id, Repetitions = 2 };
        var rider = Guid.NewGuid();
        var older = Entry(parent, 1, rider);
        var rejected = Entry(parent, 2, rider);
        rejected.IsValid = false;
        rejected.InvalidReason = StatisticsEntryInvalidReason.AboveMaximumDuration;
        var newest = Entry(parent, 3, rider);
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        await using var db = Db();
        db.AddRange(parent, multi, older, rejected, newest);
        await db.SaveChangesAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var aggregate = Assert.Single(await new StatisticsService(db, services.GetRequiredService<HybridCache>())
            .ProcessEntryAsync(rider, parent.StatisticsItemId, parent.Id, rejected.TimeStart, newest.TimeEnd));
        Assert.False(aggregate.IsValid);
        Assert.Equal(StatisticsEntryInvalidReason.InvalidContributor, aggregate.InvalidReason);
        Assert.Equal(new[] { newest.Id, rejected.Id }, aggregate.Contributors.OrderBy(c => c.Position).Select(c => c.ContributorEntryId));
        Assert.DoesNotContain(aggregate.Contributors, c => c.ContributorEntryId == older.Id);
        await tx.CommitAsync();
        Assert.Equal(4, await db.Set<StatisticsEntry>().CountAsync());
    }

    [PostgreSqlStatisticsFact]
    public async Task AmbiguousLegacyContributorsRemainConservativelyInvalidAndUnchanged()
    {
        var parent = Simple();
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Legacy double", Distance = 500 };
        var multi = new MultiStatisticsItemConfig { Id = Guid.NewGuid(), StatisticsItem = item, StatisticsItemId = item.Id,
            ParentConfig = parent, ParentConfigId = parent.Id, Repetitions = 2 };
        var rider = Guid.NewGuid();
        var a = Entry(parent, 1, rider);
        var b = Entry(parent, 2, rider);
        var later = new StatisticsEntry
        {
            Id = Guid.NewGuid(), StatisticsItemId = parent.StatisticsItemId, StatisticsItemConfigId = parent.Id,
            TransponderId = rider, FacilityId = a.FacilityId, TimeStart = a.TimeStart,
            TimeEnd = a.TimeEnd.AddSeconds(5), Duration = TimeSpan.FromSeconds(15), Speed = 250 / 15d
        };
        var legacy = new StatisticsEntry
        {
            Id = Guid.NewGuid(), StatisticsItemId = item.Id, StatisticsItemConfigId = multi.Id,
            TransponderId = rider, FacilityId = a.FacilityId, TimeStart = a.TimeStart,
            TimeEnd = b.TimeEnd, Duration = b.TimeEnd - a.TimeStart, Speed = 25
        };
        await using (var db = Db())
        {
            db.AddRange(parent, multi, a, b, later, legacy);
            await db.SaveChangesAsync();
        }
        await Change(item.Id, null);
        var results = await Drain(item.Id, 2);
        Assert.Equal(1, results.Sum(r => r.UnprovenLegacyLineage));
        await using var verify = Db();
        var persisted = await verify.Set<StatisticsEntry>().Include(e => e.Contributors).SingleAsync(e => e.Id == legacy.Id);
        Assert.False(persisted.IsValid);
        Assert.Equal(StatisticsEntryInvalidReason.InvalidContributor, persisted.InvalidReason);
        Assert.False(persisted.HasCompleteContributorLineage);
        Assert.Empty(persisted.Contributors);
        Assert.Equal(legacy.TimeStart, persisted.TimeStart);
        Assert.Equal(legacy.TimeEnd, persisted.TimeEnd);
        Assert.Equal(4, await verify.Set<StatisticsEntry>().CountAsync());
    }

    [PostgreSqlStatisticsFact]
    public async Task SupersedingEditWaitsForLockedBatchAndWinsAfterItsCommit()
    {
        var config = Simple();
        await using (var db = Db())
        {
            db.Add(config);
            db.AddRange(Enumerable.Range(1, 3).Select(i => Entry(config, i)));
            await db.SaveChangesAsync();
        }
        await Change(config.StatisticsItemId, TimeSpan.FromSeconds(11));
        var generation = (await Job(config.StatisticsItemId)).Generation;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var gate = new PageGate();
        using var services = new ServiceCollection().AddMetrics().AddSingleton<Metrics>().BuildServiceProvider();
        await using var batchStorage = new StatisticsDbContext(new DbContextOptionsBuilder<StatisticsDbContext>()
            .UseNpgsql(connection).UseSnakeCaseNamingConvention().AddInterceptors(gate).Options);
        var batch = new StatisticsRevalidationService(batchStorage, new(batchStorage), services.GetRequiredService<Metrics>(),
            NullLogger<StatisticsRevalidationService>.Instance).ProcessBatchAsync(config.StatisticsItemId, 2, timeout.Token);
        await gate.Entered.Task.WaitAsync(timeout.Token);
        var save = Change(config.StatisticsItemId, null);
        try
        {
            await using var probe = Db();
            while (await probe.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock'
                """).SingleAsync(timeout.Token) == 0)
                await Task.Delay(20, timeout.Token);
            Assert.False(save.IsCompleted);
        }
        finally { gate.Release.TrySetResult(); }
        Assert.Equal(2, (await batch).Reclassified);
        await save;
        var superseding = await Job(config.StatisticsItemId);
        Assert.True(superseding.Generation > generation);
        Assert.Null(superseding.LastEntryId);
        await Drain(config.StatisticsItemId, 2);
        await using var verify = Db();
        Assert.Equal(3, await verify.Set<StatisticsEntry>().CountAsync(e => e.IsValid));
    }

    [PostgreSqlStatisticsFact]
    public async Task FailedBatchCommitRollsBackValidityAndCheckpointAndRestartRetries()
    {
        var config = Simple();
        await using (var db = Db())
        {
            db.Add(config);
            db.AddRange(Enumerable.Range(1, 3).Select(i => Entry(config, i)));
            await db.SaveChangesAsync();
        }
        await Change(config.StatisticsItemId, TimeSpan.FromSeconds(11));
        var original = await Job(config.StatisticsItemId);
        using var services = new ServiceCollection().AddMetrics().AddSingleton<Metrics>().BuildServiceProvider();
        await using (var failing = new StatisticsDbContext(new DbContextOptionsBuilder<StatisticsDbContext>()
            .UseNpgsql(connection).UseSnakeCaseNamingConvention().AddInterceptors(new FailCommit()).Options))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new StatisticsRevalidationService(failing, new(failing),
                services.GetRequiredService<Metrics>(), NullLogger<StatisticsRevalidationService>.Instance)
                .ProcessBatchAsync(config.StatisticsItemId, 2, CancellationToken.None));
        }
        var rolledBack = await Job(config.StatisticsItemId);
        Assert.Equal(original.Generation, rolledBack.Generation);
        Assert.Equal(original.LastEntryId, rolledBack.LastEntryId);
        await using (var verify = Db()) Assert.Equal(3, await verify.Set<StatisticsEntry>().CountAsync(e => e.IsValid));
        Assert.Equal(2, (await Batch(config.StatisticsItemId, 2)).Reclassified);
        await Drain(config.StatisticsItemId, 2);
        await using var final = Db();
        Assert.Equal(3, await final.Set<StatisticsEntry>().CountAsync(e => !e.IsValid));
    }

    [PostgreSqlStatisticsFact]
    public async Task LateSimpleCreationBehindCompletedCursorUsesLiveBoundsDespiteCachedDefinition()
    {
        var config = Simple();
        var rider = Guid.NewGuid();
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        await using (var db = Db())
        {
            db.Add(config);
            db.Add(new Sample
            {
                Id = Guid.NewGuid(), TimeStart = Start, TimeEnd = Start.AddSeconds(1), Duration = TimeSpan.FromSeconds(1),
                Distance = 25, Speed = 25, CoursePointStartId = config.CoursePointStart, CoursePointEndId = Guid.NewGuid(),
                FacilityId = Guid.NewGuid(), TransponderId = rider
            });
            await db.SaveChangesAsync();
            await new StatisticsService(db, services.GetRequiredService<HybridCache>()).GetStatisticsItemConfigByEndCoursePoint(config.CoursePointEnd);
        }
        await Change(config.StatisticsItemId, TimeSpan.FromSeconds(11));
        await Drain(config.StatisticsItemId, 1);
        await using (var db = Db())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var entry = Assert.Single(await new StatisticsService(db, services.GetRequiredService<HybridCache>())
                .ProcessCompletedAsync(rider, Start.AddSeconds(2), Start.AddSeconds(10), config.CoursePointStart, config.CoursePointEnd));
            Assert.False(entry.IsValid);
            Assert.Equal(StatisticsEntryInvalidReason.BelowMinimumDuration, entry.InvalidReason);
            Assert.Equal(TimeSpan.FromSeconds(10), entry.Duration);
            Assert.Equal(25, entry.Speed);
            await tx.CommitAsync();
        }
        await using var verify = Db();
        var persisted = await verify.Set<StatisticsEntry>().SingleAsync();
        Assert.False(persisted.IsValid);
        Assert.Equal(DateTimeKind.Utc, persisted.TimeStart.Kind);
        Assert.Equal(DateTimeKind.Utc, persisted.TimeEnd.Kind);
        Assert.False((await Job(config.StatisticsItemId)).IsPending);
    }

    [PostgreSqlStatisticsFact]
    public async Task UpgradeRetainsLegacyNonpositiveMeasurementsAndDefaultsWithoutHistoricalReclassification()
    {
        var upgradeDatabase = $"threshold_upgrade_{Guid.NewGuid():N}";
        await Sql(admin, $"CREATE DATABASE \"{upgradeDatabase}\"");
        var upgradeConnection = new NpgsqlConnectionStringBuilder(admin) { Database = upgradeDatabase }.ConnectionString;
        try
        {
            await using var db = new StatisticsDbContext(new DbContextOptionsBuilder<StatisticsDbContext>()
                .UseNpgsql(upgradeConnection, o => o.MigrationsHistoryTable("__ef_migrations_history", "ef"))
                .UseSnakeCaseNamingConvention().Options);
            await db.GetService<IMigrator>().MigrateAsync("20261031085933_statistics_public_indexes");
            var item = Guid.NewGuid();
            var config = Guid.NewGuid();
            var point = Guid.NewGuid();
            var entry = Guid.NewGuid();
            var rider = Guid.NewGuid();
            var facility = Guid.NewGuid();
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO statistics.statistics_item (id, name, description, distance) VALUES ({item}, 'Legacy', '', 250);
                INSERT INTO statistics.statistics_item_config
                (id, statistics_item_id, discriminator, course_point_start, course_point_end)
                VALUES ({config}, {item}, 'SimpleStatisticsItemConfig', {point}, {point});
                INSERT INTO statistics.statistics_entry
                (id, statistics_item_id, statistics_item_config_id, transponder_id, facility_id, time_start, time_end, duration, speed)
                VALUES ({entry}, {item}, {config}, {rider}, {facility}, {Start}, {Start}, {TimeSpan.Zero}, {double.PositiveInfinity});
                """);
            await db.Database.MigrateAsync();
            var persisted = await db.Set<StatisticsEntry>().SingleAsync();
            Assert.Equal(entry, persisted.Id);
            Assert.True(persisted.IsValid); // Migration is metadata only; worker classifies later.
            Assert.Null(persisted.InvalidReason);
            Assert.False(persisted.HasCompleteContributorLineage);
            Assert.Equal(TimeSpan.Zero, persisted.Duration);
            Assert.True(double.IsPositiveInfinity(persisted.Speed));
            Assert.Equal(Start, persisted.TimeStart);
            Assert.Equal(DateTimeKind.Utc, persisted.TimeEnd.Kind);
            Assert.Equal(rider, persisted.TransponderId);
            Assert.Equal(facility, persisted.FacilityId);
            Assert.Null((await db.Set<StatisticsItem>().SingleAsync()).MinDuration);
            Assert.Null((await db.Set<StatisticsItemConfig>().SingleAsync()).MaxDuration);
            Assert.Empty(await db.Set<StatisticsEntryContributor>().ToListAsync());
            Assert.Empty(await db.Set<StatisticsValidityRevalidation>().ToListAsync());
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        }
        finally
        {
            using var pool = new NpgsqlConnection(upgradeConnection);
            NpgsqlConnection.ClearPool(pool);
            await Sql(admin, $"DROP DATABASE \"{upgradeDatabase}\" WITH (FORCE)");
        }
    }

    [PostgreSqlStatisticsFact]
    public async Task MigrationModelConstraintsAndActualKeysetIndexAgree()
    {
        await using var db = Db();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(9, (await db.Database.GetAppliedMigrationsAsync()).Count());
        var config = Simple();
        db.Add(config);
        db.AddRange(Enumerable.Range(1, 127).Select(i => Entry(config, i)));
        // A one-item table can legitimately favor the PK. Include other item scopes to
        // exercise the item-equality selectivity that the composite worker index serves.
        foreach (var other in Enumerable.Range(1, 32).Select(_ => Simple()))
        {
            db.Add(other);
            db.AddRange(Enumerable.Range(1, 512).Select(i => new StatisticsEntry
            {
                Id = Guid.NewGuid(), StatisticsItemId = other.StatisticsItemId, StatisticsItemConfigId = other.Id,
                TransponderId = Guid.NewGuid(), FacilityId = Guid.NewGuid(), TimeStart = Start,
                TimeEnd = Start.AddSeconds(10), Duration = TimeSpan.FromSeconds(10), Speed = 25
            }));
        }
        await db.SaveChangesAsync();
        var index = await db.Database.SqlQueryRaw<string>("""
            SELECT indexdef AS "Value" FROM pg_indexes
            WHERE schemaname = 'statistics' AND indexname = 'ix_statistics_entry_statistics_item_id_id'
            """).SingleAsync();
        Assert.Contains("(statistics_item_id, id)", index);
        var validated = await db.Database.SqlQueryRaw<bool>("""
            SELECT convalidated AS "Value" FROM pg_constraint WHERE conname = 'ck_statistics_entry_validity'
            """).SingleAsync();
        Assert.False(validated);
        await db.Database.ExecuteSqlRawAsync("ANALYZE statistics.statistics_entry");
        await using var raw = new NpgsqlConnection(connection);
        await raw.OpenAsync();
        await using var command = new NpgsqlCommand("""
            EXPLAIN (ANALYZE, FORMAT TEXT) SELECT id, statistics_item_config_id, transponder_id,
            time_start, time_end, duration, is_valid, invalid_reason, has_complete_contributor_lineage
            FROM statistics.statistics_entry
            WHERE statistics_item_id = @item AND id > @cursor ORDER BY id LIMIT 16
            """, raw);
        command.Parameters.AddWithValue("item", config.StatisticsItemId);
        command.Parameters.AddWithValue("cursor", Guid.Empty);
        await using var reader = await command.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await reader.ReadAsync()) lines.Add(reader.GetString(0));
        var plan = string.Join("\n", lines);
        Console.WriteLine(plan);
        Assert.Contains("ix_statistics_entry_statistics_item_id_id", plan);
        Assert.DoesNotContain("Seq Scan", plan);
        Assert.DoesNotContain("Sort", plan);
    }

    private StatisticsDbContext Db() => new(new DbContextOptionsBuilder<StatisticsDbContext>()
        .UseNpgsql(connection, options => options.MigrationsHistoryTable("__ef_migrations_history", "ef"))
        .UseSnakeCaseNamingConvention().AddInterceptors(capture).Options);
    private static SimpleStatisticsItemConfig Simple()
    {
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap", Distance = 250 };
        return new() { Id = Guid.NewGuid(), StatisticsItem = item, StatisticsItemId = item.Id,
            CoursePointStart = Guid.NewGuid(), CoursePointEnd = Guid.NewGuid() };
    }
    private static StatisticsEntry Entry(SimpleStatisticsItemConfig config, int i, Guid? rider = null) => new()
    {
        Id = Guid.Parse($"80000000-0000-0000-0000-{i:D12}"), StatisticsItemId = config.StatisticsItemId,
        StatisticsItemConfigId = config.Id, TransponderId = rider ?? Guid.NewGuid(), FacilityId = Guid.NewGuid(),
        TimeStart = Start.AddSeconds(i * 10), TimeEnd = Start.AddSeconds(i * 10 + 10),
        Duration = TimeSpan.FromSeconds(10), Speed = 25
    };
    private async Task Change(Guid item, TimeSpan? min)
    {
        await using var db = Db();
        await using var tx = await db.Database.BeginTransactionAsync();
        var scheduler = new StatisticsValidityScheduler(db);
        await scheduler.LockThresholdScopeAsync(item, true);
        var definition = await db.Set<StatisticsItem>().SingleAsync(i => i.Id == item);
        definition.MinDuration = min;
        await db.SaveChangesAsync();
        await scheduler.ScheduleThresholdChangeAsync(item, true);
        await tx.CommitAsync();
    }
    private async Task<StatisticsValidityRevalidation> Job(Guid item)
    {
        await using var db = Db();
        return await db.Set<StatisticsValidityRevalidation>().AsNoTracking().SingleAsync(j => j.StatisticsItemId == item);
    }
    private async Task<StatisticsRevalidationBatchResult> Batch(Guid item, int size)
    {
        await using var db = Db();
        using var services = new ServiceCollection().AddMetrics().AddSingleton<Metrics>().BuildServiceProvider();
        var result = await new StatisticsRevalidationService(db, new(db), services.GetRequiredService<Metrics>(),
            NullLogger<StatisticsRevalidationService>.Instance).ProcessBatchAsync(item, size, CancellationToken.None);
        Assert.Empty(db.ChangeTracker.Entries<StatisticsEntry>());
        Assert.Null(db.Database.CurrentTransaction);
        return result;
    }
    private async Task<List<StatisticsRevalidationBatchResult>> Drain(Guid item, int size)
    {
        var batches = new List<StatisticsRevalidationBatchResult>();
        for (var i = 0; i < 100; i++)
        {
            var result = await Batch(item, size);
            batches.Add(result);
            if (result.Outcome is StatisticsRevalidationBatchOutcome.Completed or StatisticsRevalidationBatchOutcome.NotPending) return batches;
        }
        throw new Xunit.Sdk.XunitException("Job did not complete within bounded test sweep.");
    }
    private static async Task Sql(string connectionString, string sql)
    {
        await using var raw = new NpgsqlConnection(connectionString);
        await raw.OpenAsync();
        await using var command = new NpgsqlCommand(sql, raw);
        await command.ExecuteNonQueryAsync();
    }
    private sealed class Capture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
    private sealed class FailCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("injected batch commit failure");
    }
    private sealed class PageGate : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("ORDER BY e.") && command.CommandText.Contains("LIMIT @limit"))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
