using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Test;

public sealed class StatisticsSmbHeadMigrationTests
{
    private const string Previous = "20261124095835_statistics_multi_creation_decisions";
    private const string Current = "20261125075737_statistics_processor_smb_head";

    [MultiDecisionPostgreSqlFact]
    public async Task UpgradeRejectsUnresolvedLegacyStatesAndPreservesCompletedReceiptsAndApiOutbox()
    {
        await using var fixture = await Database.Create();
        await fixture.Migrate(Previous);
        var rider = Guid.NewGuid();
        await fixture.Sql($"""
            CREATE TABLE statistics.smb_outbox (id integer PRIMARY KEY);
            INSERT INTO statistics.smb_outbox VALUES (7);
            INSERT INTO statistics.sample_complete_outbox
                (sample_id, transponder_id, time_start, time_end, course_point_start, course_point_end,
                 distance, delivered_at_utc, entries_created_at_utc)
            VALUES ('{Guid.NewGuid()}', '{rider}', now(), now(), '{Guid.NewGuid()}', '{Guid.NewGuid()}',
                    10, now(), now());
            INSERT INTO statistics.entry_created_outbox
                (entry_id, transponder_id, time_start, time_end, statistics_item_id, config_item_id,
                 single, delivered_at_utc, multi_decision_version, multi_decisions_created_at_utc)
            VALUES ('{Guid.NewGuid()}', '{rider}', now(), now(), '{Guid.NewGuid()}', '{Guid.NewGuid()}',
                    true, now(), 1, now());
            """);

        foreach (var (bad, restore) in new[]
        {
            ("UPDATE statistics.sample_complete_outbox SET delivered_at_utc=NULL",
             "UPDATE statistics.sample_complete_outbox SET delivered_at_utc=now()"),
            ("UPDATE statistics.sample_complete_outbox SET entries_created_at_utc=NULL",
             "UPDATE statistics.sample_complete_outbox SET entries_created_at_utc=now()"),
            ("UPDATE statistics.entry_created_outbox SET delivered_at_utc=NULL",
             "UPDATE statistics.entry_created_outbox SET delivered_at_utc=now()"),
            ("UPDATE statistics.entry_created_outbox SET multi_decisions_created_at_utc=NULL",
             "UPDATE statistics.entry_created_outbox SET multi_decisions_created_at_utc=now()"),
            ("UPDATE statistics.entry_created_outbox SET multi_decision_version=0, multi_decisions_created_at_utc=NULL",
             "UPDATE statistics.entry_created_outbox SET multi_decision_version=1, multi_decisions_created_at_utc=now()")
        })
        {
            await fixture.Sql(bad);
            var failure = await Assert.ThrowsAsync<PostgresException>(() => fixture.Migrate(Current));
            Assert.Contains("unreconciled legacy receipts", failure.MessageText);
            Assert.Null(await fixture.Value("SELECT to_regnamespace('statistics_processor')::text"));
            Assert.Equal(0L, await fixture.Value("""
                SELECT count(*) FROM information_schema.columns
                WHERE table_schema='statistics' AND column_name='smb_enqueued_at_utc'
                """));
            await fixture.Sql(restore);
        }

        // A pre-existing processor schema is ambiguous and must not be adopted.
        await fixture.Sql("CREATE SCHEMA statistics_processor; CREATE TABLE statistics_processor.unknown (id int)");
        await Assert.ThrowsAsync<PostgresException>(() => fixture.Migrate(Current));
        await fixture.Sql("DROP TABLE statistics_processor.unknown; DROP SCHEMA statistics_processor");
        await fixture.Migrate(Current);
        Assert.Equal(7, await fixture.Value("SELECT id FROM statistics.smb_outbox"));
        Assert.Equal(1L, await fixture.Value("SELECT count(*) FROM statistics.sample_complete_outbox WHERE entries_created_at_utc IS NOT NULL AND smb_enqueued_at_utc IS NULL"));
        Assert.Equal(1L, await fixture.Value("SELECT count(*) FROM statistics.entry_created_outbox WHERE multi_decisions_created_at_utc IS NOT NULL AND smb_enqueued_at_utc IS NULL"));
        Assert.NotNull(await fixture.Value("SELECT to_regnamespace('statistics_processor')::text"));
        var blocked = await Assert.ThrowsAsync<PostgresException>(() => fixture.Migrate(Previous));
        Assert.Contains("retained receipts", blocked.MessageText);
    }

    [MultiDecisionPostgreSqlFact]
    public async Task FreshChainEnforcesOneActiveHeadAndRollsBackMarkerWithSmbInsert()
    {
        await using var fixture = await Database.Create();
        await fixture.Migrate(null);
        await fixture.Sql("CREATE TABLE statistics_processor.smb_outbox (id integer PRIMARY KEY)");
        var rider = Guid.NewGuid();
        await fixture.Sql($"""
            INSERT INTO statistics.sample_complete_outbox
                (sample_id, transponder_id, time_start, time_end, course_point_start, course_point_end, distance)
            SELECT gen_random_uuid(), '{rider}', now(), now(), gen_random_uuid(), gen_random_uuid(), 1
            FROM generate_series(1,2);
            INSERT INTO statistics.entry_created_outbox
                (entry_id, transponder_id, time_start, time_end, statistics_item_id, config_item_id, single, multi_decision_version)
            SELECT gen_random_uuid(), '{rider}', now(), now(), gen_random_uuid(), gen_random_uuid(), false, 1
            FROM generate_series(1,2);
            """);

        foreach (var (table, completion) in new[]
        {
            ("sample_complete_outbox", "entries_created_at_utc"),
            ("entry_created_outbox", "multi_decisions_created_at_utc")
        })
        {
            await using (var tx = await fixture.Connection.BeginTransactionAsync())
            {
                await fixture.Sql($"UPDATE statistics.{table} SET smb_enqueued_at_utc=now() WHERE id=(SELECT min(id) FROM statistics.{table})");
                await fixture.Sql("INSERT INTO statistics_processor.smb_outbox VALUES (1)");
                await tx.RollbackAsync();
            }
            Assert.Equal(0L, await fixture.Value($"SELECT count(*) FROM statistics.{table} WHERE smb_enqueued_at_utc IS NOT NULL"));
            Assert.Equal(0L, await fixture.Value("SELECT count(*) FROM statistics_processor.smb_outbox"));
            await fixture.Sql($"UPDATE statistics.{table} SET smb_enqueued_at_utc=now() WHERE id=(SELECT min(id) FROM statistics.{table})");
            var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
                fixture.Sql($"UPDATE statistics.{table} SET smb_enqueued_at_utc=now() WHERE id=(SELECT max(id) FROM statistics.{table})"));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
            await fixture.Sql($"UPDATE statistics.{table} SET {completion}=now() WHERE id=(SELECT min(id) FROM statistics.{table})");
            await fixture.Sql($"UPDATE statistics.{table} SET smb_enqueued_at_utc=now() WHERE id=(SELECT max(id) FROM statistics.{table})");
            Assert.Equal(2L, await fixture.Value($"SELECT count(*) FROM statistics.{table} WHERE smb_enqueued_at_utc IS NOT NULL"));
        }

        await Assert.ThrowsAsync<PostgresException>(() => fixture.Migrate(Previous));
        // Fixture-only cleanup: a non-empty SMB-owned schema independently prevents Down.
        await fixture.Sql("DELETE FROM statistics.sample_complete_outbox; DELETE FROM statistics.entry_created_outbox");
        var nonempty = await Assert.ThrowsAsync<PostgresException>(() => fixture.Migrate(Previous));
        Assert.Equal(PostgresErrorCodes.DependentObjectsStillExist, nonempty.SqlState);
        await fixture.Sql("DROP TABLE statistics_processor.smb_outbox");
        await fixture.Migrate(Previous);
        Assert.Null(await fixture.Value("SELECT to_regnamespace('statistics_processor')::text"));
    }

    private sealed class Database : IAsyncDisposable
    {
        private readonly NpgsqlConnection admin;
        private readonly string name;
        private readonly string connectionString;
        public NpgsqlConnection Connection { get; }

        private Database(NpgsqlConnection admin, string name, string connectionString, NpgsqlConnection connection)
            => (this.admin, this.name, this.connectionString, Connection) = (admin, name, connectionString, connection);

        public static async Task<Database> Create()
        {
            var adminString = Environment.GetEnvironmentVariable("VELOTIME_TEST_STATISTICS_POSTGRES")!;
            var admin = new NpgsqlConnection(adminString);
            await admin.OpenAsync();
            var name = "statistics_smb_head_" + Guid.NewGuid().ToString("N");
            await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin))
                await command.ExecuteNonQueryAsync();
            var connectionString = new NpgsqlConnectionStringBuilder(adminString) { Database = name, Pooling = false }.ConnectionString;
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            return new Database(admin, name, connectionString, connection);
        }

        public async Task Migrate(string? target)
        {
            await using var db = new StatisticsDbContext(new DbContextOptionsBuilder<StatisticsDbContext>()
                .UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", "ef"))
                .UseSnakeCaseNamingConvention().Options);
            await db.GetService<IMigrator>().MigrateAsync(target);
        }

        public async Task Sql(string sql)
        {
            await using var command = new NpgsqlCommand(sql, Connection);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<object?> Value(string sql)
        {
            await using var command = new NpgsqlCommand(sql, Connection);
            var value = await command.ExecuteScalarAsync();
            return value is DBNull ? null : value;
        }

        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", admin);
            await command.ExecuteNonQueryAsync();
            await admin.DisposeAsync();
        }
    }
}
