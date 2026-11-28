using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Metadata;
using VeloTime.Module.Statistics.Model;
using Npgsql;
using VeloTime.Module.Statistics.Migrations;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Test;

public sealed class StatisticsAdminIndexMigrationTests
{
    [StatisticsIndexPostgreSqlFact]
    public async Task ConcurrentMigrationBuildsUsableBidirectionalIndexesAndRollsBackOutsideTransactions()
    {
        var adminString = Environment.GetEnvironmentVariable("VELOTIMER_PUBLIC_STATISTICS_POSTGRES")!;
        var database = "velotimer_index_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(adminString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin)) await create.ExecuteNonQueryAsync();
        var settings = new NpgsqlConnectionStringBuilder(adminString) { Database = database, Pooling = false };
        try
        {
            await using var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync();
            // Only the pre-existing table shape is needed: do not use EnsureCreated,
            // which would bypass the migration under test and pre-create its indexes.
            await Execute(connection, "CREATE SCHEMA statistics; CREATE TABLE statistics.statistics_entry (id uuid PRIMARY KEY, duration interval NOT NULL, speed double precision NOT NULL)");
            await Execute(connection, "INSERT INTO statistics.statistics_entry SELECT md5(i::text)::uuid, (i / 4) * interval '1 second', (i / 4)::double precision FROM generate_series(1, 2000) i; ANALYZE statistics.statistics_entry; SET enable_seqscan=off");
            await using var db = new StatisticsDbContext(new DbContextOptionsBuilder<StatisticsDbContext>().UseNpgsql(settings.ConnectionString).UseSnakeCaseNamingConvention().Options);
            var migration = new statistics_entry_admin_duration_speed_indexes { ActiveProvider = db.Database.ProviderName! };
            var model = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(StatisticsEntry))!;
            foreach (var operation in migration.UpOperations.OfType<CreateIndexOperation>())
            {
                var index = model.GetIndexes().Single(index => index.GetDatabaseName() == operation.Name);
                var table = StoreObjectIdentifier.Table(operation.Table, operation.Schema);
                Assert.Equal(operation.Columns, index.Properties.Select(property => property.GetColumnName(table)));
                Assert.Equal(index.FindAnnotation("Npgsql:CreatedConcurrently")?.Value,
                    operation.FindAnnotation("Npgsql:CreatedConcurrently")?.Value);
            }
            var generator = db.GetService<IMigrationsSqlGenerator>();
            var up = generator.Generate(migration.UpOperations);
            var down = generator.Generate(migration.DownOperations);
            Assert.All(up.Concat(down), command => Assert.True(command.TransactionSuppressed));
            // Reapply after rollback, checking actual PostgreSQL validity and planner usage.
            for (var cycle = 0; cycle < 2; cycle++)
            {
                foreach (var command in up) await Execute(connection, command.CommandText);
                foreach (var column in new[] { "duration", "speed" })
                {
                    var name = $"ix_statistics_entry_{column}_id";
                    await using var catalog = new NpgsqlCommand("SELECT i.indisvalid AND i.indisready AND i.indpred IS NULL, pg_get_indexdef(i.indexrelid) FROM pg_index i WHERE i.indexrelid = @name::regclass", connection);
                    catalog.Parameters.AddWithValue("name", "statistics." + name);
                    await using (var reader = await catalog.ExecuteReaderAsync())
                    {
                        Assert.True(await reader.ReadAsync());
                        Assert.True(reader.GetBoolean(0));
                        Assert.Contains($"USING btree ({column}, id)", reader.GetString(1));
                    }
                    foreach (var order in new[] { "ASC", "DESC" })
                    {
                        await using var explain = new NpgsqlCommand($"EXPLAIN SELECT id FROM statistics.statistics_entry ORDER BY {column} {order}, id {order} LIMIT 3", connection);
                        await using var reader = await explain.ExecuteReaderAsync();
                        var plan = new List<string>();
                        while (await reader.ReadAsync()) plan.Add(reader.GetString(0));
                        Assert.Contains(plan, line => line.Contains(name, StringComparison.Ordinal));
                        Assert.DoesNotContain(plan, line => line.Contains("Sort ", StringComparison.Ordinal));
                    }
                }
                foreach (var command in down) await Execute(connection, command.CommandText);
                await using var absent = new NpgsqlCommand("SELECT count(*) FROM pg_indexes WHERE schemaname = 'statistics' AND indexname LIKE 'ix_statistics_entry_%_id'", connection);
                Assert.Equal(0L, await absent.ExecuteScalarAsync());
                // The concurrent down migration is intentionally safe to repeat.
                foreach (var command in down) await Execute(connection, command.CommandText);
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class StatisticsIndexPostgreSqlFactAttribute : FactAttribute
{
    public StatisticsIndexPostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOTIMER_PUBLIC_STATISTICS_POSTGRES")))
            Skip = "Requires disposable PostgreSQL via VELOTIMER_PUBLIC_STATISTICS_POSTGRES; SQLite cannot validate concurrent indexes.";
    }
}
