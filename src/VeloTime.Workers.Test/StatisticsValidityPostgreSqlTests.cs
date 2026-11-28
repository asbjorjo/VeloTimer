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
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Service.Recompute;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests : IAsyncLifetime
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
            await db.GetService<IMigrator>().MigrateAsync("20261103135802_statistics_validity_revalidation_index");
            var item = Guid.NewGuid();
            var checkpointlessItem = Guid.NewGuid();
            var config = Guid.NewGuid();
            var point = Guid.NewGuid();
            var entry = Guid.NewGuid();
            var contributor = Guid.NewGuid();
            var rider = Guid.NewGuid();
            var facility = Guid.NewGuid();
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO statistics.statistics_item (id, name, description, distance) VALUES ({item}, 'Legacy', '', 250);
                INSERT INTO statistics.statistics_item (id, name, description, distance) VALUES ({checkpointlessItem}, 'New', '', 500);
                INSERT INTO statistics.statistics_item_config
                (id, statistics_item_id, discriminator, course_point_start, course_point_end)
                VALUES ({config}, {item}, 'SimpleStatisticsItemConfig', {point}, {point});
                INSERT INTO statistics.statistics_entry
                (id, statistics_item_id, statistics_item_config_id, transponder_id, facility_id, time_start, time_end, duration, speed)
                VALUES ({entry}, {item}, {config}, {rider}, {facility}, {Start}, {Start}, {TimeSpan.Zero}, {double.PositiveInfinity});
                INSERT INTO statistics.statistics_entry
                (id, statistics_item_id, statistics_item_config_id, transponder_id, facility_id, time_start, time_end, duration, speed)
                VALUES ({contributor}, {item}, {config}, {rider}, {facility}, {Start}, {Start.AddSeconds(1)}, {TimeSpan.FromSeconds(1)}, 250);
                UPDATE statistics.statistics_entry SET has_complete_contributor_lineage = TRUE WHERE id = {entry};
                INSERT INTO statistics.statistics_entry_contributor
                (statistics_entry_id, position, contributor_entry_id) VALUES ({entry}, 0, {contributor});
                INSERT INTO statistics.statistics_validity_revalidation
                (statistics_item_id, generation, is_pending, phase, last_entry_id)
                VALUES ({item}, 7, FALSE, 1, {entry});
                """);
            // Stop before the replay-safe recompute migration, which deletes every derived entry.
            await db.GetService<IMigrator>().MigrateAsync(LapDistanceActivationMigration);
            await using (var legacy = PreVersionDb(upgradeConnection))
            {
                var persisted = await legacy.Set<StatisticsEntry>().SingleAsync(e => e.Id == entry);
                Assert.Equal(entry, persisted.Id);
                Assert.True(persisted.IsValid); // Migration is metadata only; worker classifies later.
                Assert.Null(persisted.InvalidReason);
                Assert.Equal(TimeSpan.Zero, persisted.Duration);
                Assert.True(double.IsPositiveInfinity(persisted.Speed));
                Assert.Equal(Start, persisted.TimeStart);
                Assert.Equal(DateTimeKind.Utc, persisted.TimeEnd.Kind);
                Assert.Equal(rider, persisted.TransponderId);
                Assert.Equal(facility, persisted.FacilityId);
                Assert.Null((await db.Set<StatisticsItem>().SingleAsync(i => i.Id == item)).MinDuration);
                Assert.Null((await db.Set<StatisticsItemConfig>().SingleAsync()).MaxDuration);
                Assert.Equal(2, await legacy.Set<StatisticsEntry>().CountAsync());
            }
            var removedColumns = await db.Database.SqlQueryRaw<string>("""
                SELECT column_name AS "Value" FROM information_schema.columns
                WHERE table_schema = 'statistics' AND table_name = 'statistics_entry'
                  AND column_name = 'has_complete_contributor_lineage'
                """).ToListAsync();
            Assert.Empty(removedColumns);
            Assert.False(await db.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS (SELECT 1 FROM information_schema.tables
                WHERE table_schema = 'statistics' AND table_name = 'statistics_entry_contributor') AS "Value"
                """).SingleAsync());
            Assert.False(db.Database.HasPendingModelChanges());

            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.Empty(await db.Set<StatisticsEntry>().ToListAsync()); // Derived data is rebuilt from passing facts.
            // Phase 4 folds threshold revalidation into the recompute worker; its job table is gone.
            Assert.False(await db.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS (SELECT 1 FROM information_schema.tables
                WHERE table_schema = 'statistics' AND table_name = 'statistics_validity_revalidation') AS "Value"
                """).SingleAsync());
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
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(db.Database.GetMigrations(), applied);
        Assert.Contains("20261116132807_statistics_sample_attempt_indexes", applied);
        Assert.Contains("20261118121122_statistics_lap_distance_storage", applied);
        Assert.True(Array.IndexOf(applied, "20261116132807_statistics_sample_attempt_indexes")
            < Array.IndexOf(applied, "20261118121122_statistics_lap_distance_storage"));
        Assert.False(await db.Database.SqlQueryRaw<bool>("""
            SELECT EXISTS (SELECT 1 FROM information_schema.tables
            WHERE table_schema = 'statistics' AND table_name = 'statistics_entry_contributor') AS "Value"
            """).SingleAsync());
        Assert.False(await db.Database.SqlQueryRaw<bool>("""
            SELECT EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'statistics' AND table_name = 'statistics_entry'
              AND column_name = 'has_complete_contributor_lineage') AS "Value"
            """).SingleAsync());
        Assert.Equal("_uuid", await db.Database.SqlQueryRaw<string>("""
            SELECT udt_name AS "Value" FROM information_schema.columns
            WHERE table_schema = 'statistics' AND table_name = 'statistics_item_config'
              AND column_name = 'required_course_point_ids'
            """).SingleAsync());
        Assert.Equal("uuid", await db.Database.SqlQueryRaw<string>("""
            SELECT udt_name AS "Value" FROM information_schema.columns
            WHERE table_schema = 'statistics' AND table_name = 'statistics_item_config'
              AND column_name = 'required_course_layout_id'
            """).SingleAsync());
        Assert.Equal("true", await db.Database.SqlQueryRaw<string>("""
            SELECT column_default AS "Value" FROM information_schema.columns
            WHERE table_schema = 'statistics' AND table_name = 'statistics_entry'
              AND column_name = 'required_course_points_satisfied' AND is_nullable = 'NO'
            """).SingleAsync());
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
            time_start, time_end, duration, is_valid, invalid_reason
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

    /// <summary>A context for schemas from before the config versions migration, which added the entry's version column.</summary>
    private static StatisticsDbContext PreVersionDb(string connectionString) => new(new DbContextOptionsBuilder<StatisticsDbContext>()
        .UseNpgsql(connectionString, options => options.MigrationsHistoryTable("__ef_migrations_history", "ef"))
        .UseSnakeCaseNamingConvention().ReplaceService<IModelCustomizer, PreVersionModelCustomizer>().Options);

    private StatisticsDbContext PreVersionDb() => PreVersionDb(connection);

    private sealed class PreVersionModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<StatisticsEntry>().Ignore(e => e.StatisticsConfigVersionId);
        }
    }
    /// <summary>
    /// Derives and stores the Multi window that the trigger parent closes, from the stored parents and
    /// live bounds, as the recompute worker does (EntryDerivation.Multi).
    /// </summary>
    private static async Task<StatisticsEntry> DeriveMultiAsync(StatisticsDbContext db, MultiStatisticsItemConfig multi, StatisticsEntry trigger)
    {
        var parents = await db.Set<StatisticsEntry>().AsNoTracking()
            .Where(e => e.StatisticsItemConfigId == multi.ParentConfigId && e.TransponderId == trigger.TransponderId)
            .ToListAsync();
        var config = await db.Set<MultiStatisticsItemConfig>().AsNoTracking().Include(c => c.StatisticsItem)
            .SingleAsync(c => c.Id == multi.Id);
        var bounds = await new StatisticsService(db, new ServiceCollection().AddHybridCache().Services.BuildServiceProvider()
            .GetRequiredService<HybridCache>()).ReadCurrentBoundsAsync([multi.Id]);
        var aggregate = Assert.Single(EntryDerivation.Multi(config, bounds[multi.Id], parents), e => e.TimeEnd == trigger.TimeEnd);
        db.Add(aggregate);
        await db.SaveChangesAsync();
        return aggregate;
    }
    private static SimpleStatisticsItemConfig Simple()
    {
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap", Distance = 250 };
        return new() { Id = Guid.NewGuid(), StatisticsItem = item, StatisticsItemId = item.Id,
            CoursePointStart = Guid.NewGuid(), CoursePointEnd = Guid.NewGuid() };
    }
    private static StatisticsEntry Entry(SimpleStatisticsItemConfig config, int i, Guid? rider = null,
        Guid? facility = null, DateTime? timeStart = null, DateTime? timeEnd = null)
    {
        return new()
        {
            Id = Guid.Parse($"80000000-0000-0000-0000-{i:D12}"), StatisticsItemId = config.StatisticsItemId,
            StatisticsItemConfigId = config.Id, TransponderId = rider ?? Guid.NewGuid(),
            FacilityId = facility ?? Guid.NewGuid(),
            TimeStart = timeStart ?? Start.AddSeconds(i * 10), TimeEnd = timeEnd ?? Start.AddSeconds(i * 10 + 10),
            Duration = (timeEnd ?? Start.AddSeconds(i * 10 + 10)) - (timeStart ?? Start.AddSeconds(i * 10)), Speed = 25
        };
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
        public List<CommandSnapshot> Statements { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            Statements.Add(new(command.CommandText, command.Parameters.Cast<DbParameter>()
                .Select(parameter => (parameter.ParameterName, parameter.Value)).ToArray()));
            return ValueTask.FromResult(result);
        }
    }
    private sealed record CommandSnapshot(string Text, (string Name, object? Value)[] Parameters);
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