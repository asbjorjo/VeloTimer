using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    private const string LapDistanceStorageMigration = "20261118121122_statistics_lap_distance_storage";
    private const string LapDistanceActivationMigration = "20261118125831_statistics_lap_distance_activation";

    // Activation intentionally has no safe Down. Historical migration tests must start
    // from a fresh, fixture-owned database, rather than trying to cross that boundary.
    private async Task RecreateAtMigrationAsync(string target)
    {
        await using var db = Db();
        await db.Database.EnsureDeletedAsync();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    [PostgreSqlStatisticsFact]
    public async Task LapDistanceActivationOnEmptyInstallLeavesItemsEmpty()
    {
        await using var db = Db();
        Assert.Contains(LapDistanceActivationMigration, await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Empty(await db.Set<StatisticsItem>().ToListAsync());
    }

    [PostgreSqlStatisticsFact]
    public async Task LapDistanceActivationBackfillsAtomicallyAndRefusesLossyRollback()
    {
        await RecreateAtMigrationAsync(LapDistanceStorageMigration);
        var lap = Simple();
        lap.CoursePointEnd = lap.CoursePointStart;
        lap.StatisticsItem.Name = "Lap";
        lap.StatisticsItem.Distance = 333;
        var other = Simple();
        other.StatisticsItem.Name = "Pursuit";
        other.StatisticsItem.Distance = 500;
        var rider = Guid.NewGuid();
        var facility = Guid.NewGuid();
        // Wrong legacy speed and invalid existing snapshot verify atomic backfill.
        var gap = MigrationEntry(lap, 1, null, 91);
        var snapshotted = MigrationEntry(lap, 2, -1, 17);
        var untouched = MigrationEntry(other, 3, null, 43);
        await using var db = Db();
        db.AddRange(lap, other, gap, snapshotted, untouched);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var migrator = db.GetService<IMigrator>();

        var refusal = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
        Assert.Contains("entry without a positive finite distance snapshot", refusal.MessageText);
        await AssertUnactivatedAsync(); // In particular, the null gap row was NOT partially backfilled.

        await db.Database.ExecuteSqlAsync($"UPDATE statistics.statistics_entry SET distance_metres = {400d} WHERE id = {snapshotted.Id}");
        await migrator.MigrateAsync();
        await migrator.MigrateAsync(); // Reapplication must not rewrite preserved snapshots.
        var persistedLap = await db.Set<StatisticsItem>().AsNoTracking().SingleAsync(i => i.Id == lap.StatisticsItemId);
        Assert.Equal(StatisticsDistanceMode.CourseLayout, persistedLap.DistanceMode);
        Assert.Equal(0, persistedLap.Distance);
        await AssertRowsAsync(333, 333 / gap.Duration.TotalSeconds, 400);
        Assert.Contains(LapDistanceActivationMigration, await db.Database.GetAppliedMigrationsAsync());

        refusal = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(LapDistanceStorageMigration));
        Assert.Contains("cannot be safely rolled back", refusal.MessageText);
        Assert.Contains(LapDistanceActivationMigration, await db.Database.GetAppliedMigrationsAsync());
        persistedLap = await db.Set<StatisticsItem>().AsNoTracking().SingleAsync(i => i.Id == lap.StatisticsItemId);
        Assert.Equal(StatisticsDistanceMode.CourseLayout, persistedLap.DistanceMode);
        Assert.Equal(0, persistedLap.Distance);
        await AssertRowsAsync(333, 333 / gap.Duration.TotalSeconds, 400);

        StatisticsEntry MigrationEntry(SimpleStatisticsItemConfig config, int index, double? distance, double speed) => new()
        {
            Id = Guid.NewGuid(), StatisticsItemId = config.StatisticsItemId,
            StatisticsItemConfigId = config.Id, TransponderId = rider, FacilityId = facility,
            TimeStart = Start.AddSeconds(index * 10), TimeEnd = Start.AddSeconds(index * 10 + 10),
            Duration = TimeSpan.FromSeconds(10), Speed = speed, DistanceMetres = distance
        };

        async Task AssertUnactivatedAsync()
        {
            Assert.DoesNotContain(LapDistanceActivationMigration, await db.Database.GetAppliedMigrationsAsync());
            var persisted = await db.Set<StatisticsItem>().AsNoTracking().SingleAsync(i => i.Id == lap.StatisticsItemId);
            Assert.Equal(StatisticsDistanceMode.Fixed, persisted.DistanceMode);
            Assert.Equal(333, persisted.Distance);
            await AssertRowsAsync(null, 91, -1);
        }

        async Task AssertRowsAsync(double? gapDistance, double gapSpeed, double snapshotDistance)
        {
            var rows = await db.Set<StatisticsEntry>().AsNoTracking().ToDictionaryAsync(e => e.Id);
            Assert.Equal(3, rows.Count);
            Assert.Equal(gapDistance, rows[gap.Id].DistanceMetres);
            Assert.Equal(gapSpeed, rows[gap.Id].Speed);
            Assert.Equal(snapshotDistance, rows[snapshotted.Id].DistanceMetres);
            Assert.Equal(17, rows[snapshotted.Id].Speed);
            Assert.Null(rows[untouched.Id].DistanceMetres);
            Assert.Equal(43, rows[untouched.Id].Speed);
            var otherItem = await db.Set<StatisticsItem>().AsNoTracking().SingleAsync(i => i.Id == other.StatisticsItemId);
            Assert.Equal(StatisticsDistanceMode.Fixed, otherItem.DistanceMode);
            Assert.Equal(500, otherItem.Distance);
            foreach (var original in new[] { gap, snapshotted, untouched })
            {
                var stored = rows[original.Id];
                Assert.Equal((original.TimeStart, original.TimeEnd, original.Duration, original.IsValid, original.InvalidReason,
                    original.TransponderId, original.FacilityId, original.StatisticsItemConfigId),
                    (stored.TimeStart, stored.TimeEnd, stored.Duration, stored.IsValid, stored.InvalidReason,
                    stored.TransponderId, stored.FacilityId, stored.StatisticsItemConfigId));
                Assert.Equal(DateTimeKind.Utc, stored.TimeStart.Kind);
                Assert.Equal(DateTimeKind.Utc, stored.TimeEnd.Kind);
            }
        }
    }
}
