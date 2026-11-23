using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    [PostgreSqlStatisticsFact]
    public async Task SampleIdentityExpandPreservesLegacyAndEnforcesKeyedUniqueness()
    {
        const string previous = "20261118125831_statistics_lap_distance_activation";
        await RecreateAtMigrationAsync(previous);
        await using var db = Db();
        var rider = Guid.NewGuid();
        var startPoint = Guid.NewGuid();
        var endPoint = Guid.NewGuid();
        var legacy = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO statistics.sample
                (id, transponder_id, facility_id, course_point_start_id, course_point_end_id,
                 time_start, time_end, duration, distance, speed)
            VALUES ({legacy}, {rider}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()},
                    {Start}, {Start.AddSeconds(1)}, {TimeSpan.FromSeconds(1)}, {100d}, {100d});
            """);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM statistics.sample
            WHERE timing_point_start_id IS NULL AND timing_point_end_id IS NULL
            """).SingleAsync());

        async Task Insert(Guid? first, Guid? last, DateTime start, DateTime end)
        {
            await using var context = Db();
            await context.Database.ExecuteSqlAsync($"""
                INSERT INTO statistics.sample
                    (id, transponder_id, facility_id, course_point_start_id, course_point_end_id,
                     time_start, time_end, duration, distance, speed, timing_point_start_id, timing_point_end_id)
                VALUES ({Guid.NewGuid()}, {rider}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()},
                        {start}, {end}, {TimeSpan.FromSeconds(1)}, {100d}, {100d}, {first}, {last});
                """);
        }

        await Insert(null, null, Start, Start.AddSeconds(1)); // Existing writers remain compatible.
        var check = await Assert.ThrowsAsync<PostgresException>(() => Insert(startPoint, null, Start, Start.AddSeconds(1)));
        Assert.Equal("ck_sample_timing_points_pair", check.ConstraintName);
        await Insert(startPoint, endPoint, Start, Start.AddSeconds(1));
        // PostgreSQL discards sub-microsecond ticks: this is the same persisted identity.
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            Insert(startPoint, endPoint, Start.AddTicks(9), Start.AddSeconds(1).AddTicks(9)));
        Assert.Equal("ux_sample_timing_identity", duplicate.ConstraintName);
        await Insert(Guid.NewGuid(), endPoint, Start, Start.AddSeconds(1));
        await Insert(startPoint, endPoint, Start.AddTicks(10), Start.AddSeconds(1));
        var beforeEpoch = new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        await Insert(startPoint, endPoint, beforeEpoch.AddTicks(1), beforeEpoch.AddSeconds(1));
        duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            Insert(startPoint, endPoint, beforeEpoch.AddTicks(9), beforeEpoch.AddSeconds(1)));
        Assert.Equal("ux_sample_timing_identity", duplicate.ConstraintName);
        var rollback = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
        Assert.Contains("Cannot downgrade sample timing identity", rollback.MessageText);
        Assert.Equal(6, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM statistics.sample").SingleAsync());
        // Test-only cleanup in this fixture-owned database allows the non-lossy Down path.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM statistics.sample WHERE timing_point_start_id IS NOT NULL");
        await migrator.MigrateAsync(previous);
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM statistics.sample").SingleAsync());
    }

    [PostgreSqlStatisticsFact]
    public async Task ConcurrentKeyedInsertsHaveOneWinner()
    {
        await using var first = Db();
        await using var second = Db();
        await using var tx = await first.Database.BeginTransactionAsync();
        var rider = Guid.NewGuid();
        var startPoint = Guid.NewGuid();
        var endPoint = Guid.NewGuid();
        async Task Insert(VeloTime.Module.Statistics.Storage.StatisticsDbContext db)
        {
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO statistics.sample
                    (id, transponder_id, facility_id, course_point_start_id, course_point_end_id,
                     time_start, time_end, duration, distance, speed, timing_point_start_id, timing_point_end_id)
                VALUES ({Guid.NewGuid()}, {rider}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()},
                        {Start}, {Start.AddSeconds(1)}, {TimeSpan.FromSeconds(1)}, {100d}, {100d}, {startPoint}, {endPoint});
                """);
        }
        await Insert(first);
        var competing = Insert(second);
        await tx.CommitAsync();
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => competing);
        Assert.Equal("ux_sample_timing_identity", duplicate.ConstraintName);
        Assert.Equal(1, await second.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM statistics.sample").SingleAsync());
    }
}
