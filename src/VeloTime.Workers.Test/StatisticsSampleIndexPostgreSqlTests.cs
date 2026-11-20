using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit.Abstractions;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    private readonly ITestOutputHelper output;

    public StatisticsValidityPostgreSqlTests(ITestOutputHelper output) => this.output = output;

    [PostgreSqlStatisticsFact]
    public async Task SampleIndexesPreserveRowsRoundTripConcurrentlyAndServeAttemptQueries()
    {
        const string previous = "20261116120249_statistics_required_course_points";
        await RecreateAtMigrationAsync(previous);
        await using var db = Db();
        db.Database.SetCommandTimeout(180);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(previous);

        // Interleave 1,000 riders across 1,000 time slots, three facilities and eight points.
        // Unlike a single-rider heap, the unindexed baseline must read unrelated history.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO statistics.sample
                (id, transponder_id, facility_id, course_point_start_id, course_point_end_id,
                 time_start, time_end, duration, distance, speed)
            SELECT md5(g::text)::uuid, md5((g%1000)::text)::uuid, md5((n%3)::text)::uuid,
                   md5((n%8)::text)::uuid, md5(((n+1)%8)::text)::uuid,
                   '2026-01-01'::timestamptz+n*interval '30 seconds',
                   '2026-01-01'::timestamptz+n*interval '30 seconds'+interval '10 seconds',
                   interval '10 seconds', 100, 10
            FROM (SELECT g, (g/1000)::int n FROM generate_series(0,999999) g) s;
            """);
        // VACUUM outside a transaction establishes visibility for index-only scan observations.
        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) statistics.sample");
        await using var sql = new NpgsqlConnection(connection);
        await sql.OpenAsync();
        var queries = SampleAttemptQueries();
        foreach (var (name, query, _) in queries)
        {
            var plan = await SamplePlan(sql, query);
            output.WriteLine($"BEFORE {name}\n{plan}");
            Assert.Contains("Seq Scan on sample", plan);
        }

        // Builds on existing data, not just an empty initial-install schema.
        var build = System.Diagnostics.Stopwatch.StartNew();
        await migrator.MigrateAsync(LapDistanceStorageMigration);
        output.WriteLine($"Concurrent Up: {build.Elapsed.TotalSeconds:F3} seconds");
        await using (var sizes = new NpgsqlCommand("""
            SELECT indexrelname, pg_size_pretty(pg_relation_size(indexrelid))
            FROM pg_stat_user_indexes WHERE schemaname = 'statistics' AND relname = 'sample'
            ORDER BY indexrelname
            """, sql))
        await using (var reader = await sizes.ExecuteReaderAsync())
            while (await reader.ReadAsync()) output.WriteLine($"{reader.GetString(0)}: {reader.GetString(1)}");
        foreach (var (name, query, expectedIndex) in queries)
        {
            var plan = await SamplePlan(sql, query);
            output.WriteLine($"AFTER {name}\n{plan}");
            Assert.Contains(expectedIndex, plan);
            Assert.DoesNotContain("Seq Scan on sample", plan);
        }
        await using (var indexes = new NpgsqlCommand("""
            SELECT count(*) FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'statistics' AND c.relname LIKE 'ix_sample_%'
                AND i.indisvalid AND i.indisready AND NOT i.indisunique
            """, sql))
            Assert.Equal(4L, await indexes.ExecuteScalarAsync());

        var generator = db.GetService<IMigrationsSqlGenerator>();
        var migration = db.GetService<IMigrationsAssembly>().CreateMigration(
            db.GetService<IMigrationsAssembly>().Migrations.Single(x => x.Key.EndsWith("_statistics_sample_attempt_indexes")).Value,
            db.Database.ProviderName!);
        foreach (var operations in new[] { migration.UpOperations, migration.DownOperations })
        {
            var commands = generator.Generate(operations, db.Model);
            Assert.Equal(4, commands.Count);
            Assert.All(commands, command =>
            {
                Assert.True(command.TransactionSuppressed);
                Assert.Contains("CONCURRENTLY", command.CommandText);
            });
        }
        await migrator.MigrateAsync(previous);
        await using (var indexes = new NpgsqlCommand("""
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'statistics' AND tablename = 'sample'
            """, sql))
            Assert.Equal(1L, await indexes.ExecuteScalarAsync()); // PK only; Down retains all rows.
        await using (var count = new NpgsqlCommand("SELECT count(*) FROM statistics.sample", sql))
            Assert.Equal(1_000_000L, await count.ExecuteScalarAsync());
        await migrator.MigrateAsync(LapDistanceStorageMigration);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static async Task<string> SamplePlan(NpgsqlConnection connection, string query)
    {
        await using var command = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS) " + query, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await reader.ReadAsync()) lines.Add(reader.GetString(0));
        return string.Join('\n', lines);
    }

    private static (string Name, string Query, string Index)[] SampleAttemptQueries()
    {
        // Exact SQL predicate/projection/order shapes from RequiredCoursePointAttemptReader.
        // Fixed UTC instants and IDs make runs reproducible; do not disable sequential scans.
        const string rider = " FROM statistics.sample WHERE transponder_id=md5('1')::uuid ";
        const string facility = " AND facility_id=md5('0')::uuid ";
        return
        [
            ("completion", "SELECT DISTINCT facility_id" + rider + """
                AND time_start='2026-01-01 08:19:30+00' AND time_end='2026-01-01 08:19:40+00'
                AND course_point_start_id=md5('7')::uuid AND course_point_end_id=md5('0')::uuid
                """, "ix_sample_transponder_completion"),
            ("departure", "SELECT time_start" + rider + facility + """
                AND course_point_start_id=md5('5')::uuid AND time_start<='2026-01-01 08:19:30+00'
                ORDER BY time_start DESC LIMIT 1
                """, "ix_sample_transponder_facility_start_time"),
            ("arrival", "SELECT time_end" + rider + facility + """
                AND course_point_end_id=md5('5')::uuid AND time_end<='2026-01-01 08:19:30+00'
                ORDER BY time_end DESC LIMIT 1
                """, "ix_sample_transponder_facility_end_time"),
            ("evidence", "SELECT time_start,time_end,course_point_start_id,course_point_end_id" + rider + facility + """
                AND ((time_start<='2026-01-01 08:19:40+00' AND time_end>='2026-01-01 08:18:10+00')
                  OR (time_end<='2026-01-01 08:19:40+00' AND time_start>='2026-01-01 08:18:10+00'
                      AND time_start<='2026-01-01 08:19:40+00'))
                """, "ix_sample_transponder_facility_attempt")
        ];
    }
}
