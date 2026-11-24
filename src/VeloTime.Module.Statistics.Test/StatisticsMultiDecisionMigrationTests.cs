using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using VeloTime.Module.Statistics.Migrations;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Test;

public sealed class StatisticsMultiDecisionMigrationTests
{
    [MultiDecisionPostgreSqlFact]
    public async Task MigrationRetainsNoResultAndLegacyStateWithTransactionalConstraintsAndGuardedDown()
    {
        var adminString = Environment.GetEnvironmentVariable("VELOTIME_TEST_STATISTICS_POSTGRES")!;
        var database = "statistics_multi_decision_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(adminString);
        await admin.OpenAsync();
        await Execute(admin, $"CREATE DATABASE \"{database}\"");
        var settings = new NpgsqlConnectionStringBuilder(adminString) { Database = database, Pooling = false };
        try
        {
            await using var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync();
            await using var db = new StatisticsDbContext(new DbContextOptionsBuilder<StatisticsDbContext>()
                .UseNpgsql(settings.ConnectionString).UseSnakeCaseNamingConvention().Options);
            var generator = db.GetService<IMigrationsSqlGenerator>();
            await Execute(connection, "CREATE SCHEMA statistics");
            var original = new statistics_entry_created_outbox { ActiveProvider = db.Database.ProviderName! };
            var receiptOperations = original.UpOperations.Where(op =>
                op is CreateTableOperation { Name: "entry_created_outbox" } ||
                op is CreateIndexOperation { Table: "entry_created_outbox" }).ToList();
            foreach (var command in generator.Generate(receiptOperations))
                await Execute(connection, command.CommandText);
            var legacy = Guid.NewGuid();
            var rider = Guid.NewGuid();
            await InsertReceipt(connection, legacy, rider, null);
            var migration = new statistics_multi_creation_decisions { ActiveProvider = db.Database.ProviderName! };
            var up = generator.Generate(migration.UpOperations);
            var down = generator.Generate(migration.DownOperations);
            Assert.All(up.Concat(down), command => Assert.False(command.TransactionSuppressed));
            await InTransaction(connection, up);
            Assert.Equal(0, await Scalar(connection, "SELECT multi_decision_version FROM statistics.entry_created_outbox"));
            Assert.Null(await Scalar(connection, "SELECT multi_decisions_created_at_utc FROM statistics.entry_created_outbox"));
            Assert.Equal(0L, await Scalar(connection, "SELECT count(*) FROM statistics.multi_creation_decision"));
            Assert.Equal(0L, await Scalar(connection,
                "SELECT count(*) FROM pg_constraint WHERE conrelid = 'statistics.multi_creation_decision'::regclass AND contype = 'f'"));

            var parent = Guid.NewGuid();
            var config = Guid.NewGuid();
            await InsertReceipt(connection, parent, rider, 1);
            var decisionSql = $"INSERT INTO statistics.multi_creation_decision VALUES ('{parent}', '{config}', NULL)";
            // A no-result row and completion mark roll back together.
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                await Execute(connection, decisionSql);
                await Execute(connection, $"UPDATE statistics.entry_created_outbox SET multi_decisions_created_at_utc=now() WHERE entry_id='{parent}'");
                await transaction.RollbackAsync();
            }
            Assert.Equal(0L, await Scalar(connection, "SELECT count(*) FROM statistics.multi_creation_decision"));
            Assert.Null(await Scalar(connection, $"SELECT multi_decisions_created_at_utc FROM statistics.entry_created_outbox WHERE entry_id='{parent}'"));
            await Execute(connection, decisionSql);
            var duplicate = await Assert.ThrowsAsync<PostgresException>(() => Execute(connection, decisionSql));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
            var invalidVersion = await Assert.ThrowsAsync<PostgresException>(() => Execute(connection,
                $"UPDATE statistics.entry_created_outbox SET multi_decision_version=2 WHERE entry_id='{parent}'"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, invalidVersion.SqlState);
            var legacyMark = await Assert.ThrowsAsync<PostgresException>(() => Execute(connection,
                $"UPDATE statistics.entry_created_outbox SET multi_decisions_created_at_utc=now() WHERE entry_id='{legacy}'"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, legacyMark.SqlState);

            // Domain IDs are retained scalars, not cascading relationships.
            await Execute(connection, "CREATE TABLE statistics.test_config (id uuid PRIMARY KEY)");
            await Execute(connection, $"INSERT INTO statistics.test_config VALUES ('{config}'); DELETE FROM statistics.test_config");
            await Execute(connection, $"DELETE FROM statistics.entry_created_outbox WHERE entry_id='{parent}'");
            Assert.Equal(1L, await Scalar(connection, "SELECT count(*) FROM statistics.multi_creation_decision WHERE result_entry_id IS NULL"));
            var blocked = await Assert.ThrowsAsync<PostgresException>(() => InTransaction(connection, down));
            Assert.Contains("Cannot remove retained Multi decisions", blocked.MessageText);
            Assert.Equal(1L, await Scalar(connection, "SELECT count(*) FROM statistics.multi_creation_decision"));
            // Removing only this fixture's ledger still cannot downgrade a protocol-aware receipt.
            await Execute(connection, "DELETE FROM statistics.multi_creation_decision");
            await InsertReceipt(connection, parent, rider, 1);
            await Assert.ThrowsAsync<PostgresException>(() => InTransaction(connection, down));

            // Delivered rows must participate in the unfiltered creation-order index.
            await Execute(connection, "UPDATE statistics.entry_created_outbox SET delivered_at_utc=now(); SET enable_seqscan=off");
            await using (var explain = new NpgsqlCommand(
                $"EXPLAIN SELECT id FROM statistics.entry_created_outbox WHERE transponder_id='{rider}' AND id <= 100 ORDER BY id DESC LIMIT 1", connection))
            await using (var reader = await explain.ExecuteReaderAsync())
            {
                var plan = new List<string>();
                while (await reader.ReadAsync()) plan.Add(reader.GetString(0));
                Assert.Contains(plan, line => line.Contains("ix_entry_created_outbox_transponder_creation_order", StringComparison.Ordinal));
            }
            var firstId = (long)(await Scalar(connection, $"SELECT id FROM statistics.entry_created_outbox WHERE entry_id='{legacy}'"))!;
            var lastId = (long)(await Scalar(connection, $"SELECT id FROM statistics.entry_created_outbox WHERE entry_id='{parent}'"))!;
            Assert.True(firstId < lastId); // Equal payload times are ordered by receipt identity.
            Assert.Equal(1L, await Scalar(connection,
                $"SELECT count(*) FROM statistics.entry_created_outbox WHERE transponder_id='{rider}' AND id <= {firstId}"));

            db.Add(new MultiCreationDecision { ParentEntryId = parent, MultiConfigId = config });
            await db.SaveChangesAsync();
            var retained = await db.Set<MultiCreationDecision>().SingleAsync();
            db.Entry(retained).Property(x => x.ResultEntryId).CurrentValue = Guid.NewGuid();
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            await Execute(connection, "DELETE FROM statistics.multi_creation_decision");
            await Execute(connection, $"DELETE FROM statistics.entry_created_outbox WHERE entry_id='{parent}'");
            await InTransaction(connection, down);
            Assert.Null(await Scalar(connection, "SELECT to_regclass('statistics.multi_creation_decision')::text"));
            Assert.Equal(1L, await Scalar(connection, "SELECT count(*) FROM statistics.entry_created_outbox"));
            await InTransaction(connection, up);
            Assert.Equal(0, await Scalar(connection, "SELECT multi_decision_version FROM statistics.entry_created_outbox"));
        }
        finally
        {
            await Execute(admin, $"DROP DATABASE \"{database}\" WITH (FORCE)");
        }
    }

    private static async Task InsertReceipt(NpgsqlConnection connection, Guid entry, Guid rider, int? version)
    {
        var columns = version.HasValue ? ", multi_decision_version" : "";
        var values = version.HasValue ? $", {version.Value}" : "";
        await Execute(connection, $"""
            INSERT INTO statistics.entry_created_outbox
                (entry_id, transponder_id, time_start, time_end, statistics_item_id, config_item_id, single{columns})
            VALUES ('{entry}', '{rider}', '2026-11-24 10:00:00+00', '2026-11-24 10:00:10+00',
                '{Guid.NewGuid()}', '{Guid.NewGuid()}', TRUE{values})
            """);
    }

    private static async Task InTransaction(NpgsqlConnection connection, IReadOnlyList<MigrationCommand> commands)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var command in commands) await Execute(connection, command.CommandText);
        await transaction.CommitAsync();
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> Scalar(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }
}

public sealed class MultiDecisionPostgreSqlFactAttribute : FactAttribute
{
    public MultiDecisionPostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOTIME_TEST_STATISTICS_POSTGRES")))
            Skip = "Requires disposable PostgreSQL via VELOTIME_TEST_STATISTICS_POSTGRES.";
    }
}
