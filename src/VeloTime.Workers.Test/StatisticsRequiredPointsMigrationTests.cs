using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    [PostgreSqlStatisticsFact]
    public async Task RequiredCoursePointsMigrationPreservesLegacyRowsAndGuardsLossyDowngrade()
    {
        const string previousMigration = "20261104085927_statistics_remove_contributor_lineage";
        await using var db = Db();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(previousMigration);

        var itemId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var startPoint = Guid.NewGuid();
        var endPoint = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO statistics.statistics_item (id, name, description, distance)
            VALUES ({itemId}, $$Legacy$$, $$ $$, 100);
            INSERT INTO statistics.statistics_item_config
                (id, statistics_item_id, discriminator, course_point_start, course_point_end)
            VALUES ({configId}, {itemId}, $$SimpleStatisticsItemConfig$$, {startPoint}, {endPoint});
            INSERT INTO statistics.statistics_entry
                (id, statistics_item_id, statistics_item_config_id, transponder_id, facility_id,
                 time_start, time_end, duration, speed)
            VALUES ({entryId}, {itemId}, {configId}, {Guid.NewGuid()}, {Guid.NewGuid()},
                    {Start}, {Start.AddMinutes(1)}, {TimeSpan.FromMinutes(1)}, {100d / 60});
            """);

        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        var config = await db.Set<SimpleStatisticsItemConfig>().SingleAsync(x => x.Id == configId);
        var entry = await db.Set<StatisticsEntry>().SingleAsync(x => x.Id == entryId);
        Assert.Null(config.RequiredCourseLayoutId);
        Assert.Empty(config.RequiredCoursePointIds);
        Assert.True(entry.RequiredCoursePointsSatisfied);
        Assert.True(entry.IsValid);
        Assert.Equal(Start, entry.TimeStart);
        Assert.Equal(Start.AddMinutes(1), entry.TimeEnd);
        Assert.Null(entry.InvalidReason);

        var layoutId = Guid.NewGuid();
        var firstRequiredPoint = Guid.NewGuid();
        var secondRequiredPoint = Guid.NewGuid();
        config.RequiredCourseLayoutId = layoutId;
        config.RequiredCoursePointIds = [firstRequiredPoint, secondRequiredPoint];
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        config = await db.Set<SimpleStatisticsItemConfig>().SingleAsync(x => x.Id == configId);
        Assert.Equal(layoutId, config.RequiredCourseLayoutId);
        Assert.Equal(new[] { firstRequiredPoint, secondRequiredPoint }, config.RequiredCoursePointIds);

        var invalidReason = (int)StatisticsEntryInvalidReason.BelowMinimumDuration;
        var unsupportedReason = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlAsync($"""
                UPDATE statistics.statistics_entry
                SET is_valid = FALSE, invalid_reason = 5
                WHERE id = {entryId}
                """));
        Assert.Equal("ck_statistics_entry_validity", unsupportedReason.ConstraintName);

        config.RequiredCourseLayoutId = null;
        config.RequiredCoursePointIds = [];
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlAsync($"""
            UPDATE statistics.statistics_entry
            SET is_valid = FALSE, invalid_reason = {invalidReason}, required_course_points_satisfied = FALSE
            WHERE id = {entryId}
            """);
        var guard = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previousMigration));
        Assert.Contains("Cannot downgrade required course points", guard.MessageText);
        // EF commits each successful Down migration separately. Restore the later schema
        // before querying with the current model after the guarded downgrade fails.
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        entry = await db.Set<StatisticsEntry>().SingleAsync(x => x.Id == entryId);
        Assert.False(entry.RequiredCoursePointsSatisfied);
        Assert.Equal(StatisticsEntryInvalidReason.BelowMinimumDuration, entry.InvalidReason);

        config = await db.Set<SimpleStatisticsItemConfig>().SingleAsync(x => x.Id == configId);
        config.RequiredCourseLayoutId = layoutId;
        config.RequiredCoursePointIds = [firstRequiredPoint];
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlAsync($"""
            UPDATE statistics.statistics_entry
            SET is_valid = TRUE, invalid_reason = NULL, required_course_points_satisfied = TRUE
            WHERE id = {entryId}
            """);
        guard = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previousMigration));
        Assert.Contains("Cannot downgrade required course points", guard.MessageText);
        await migrator.MigrateAsync();

        config.RequiredCourseLayoutId = null;
        config.RequiredCoursePointIds = [];
        await db.SaveChangesAsync();
        await migrator.MigrateAsync(previousMigration);
        var legacyConstraint = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlAsync($"""
                UPDATE statistics.statistics_entry
                SET is_valid = FALSE, invalid_reason = 4
                WHERE id = {entryId}
                """));
        Assert.Equal("ck_statistics_entry_validity", legacyConstraint.ConstraintName);

        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        config = await db.Set<SimpleStatisticsItemConfig>().SingleAsync(x => x.Id == configId);
        entry = await db.Set<StatisticsEntry>().SingleAsync(x => x.Id == entryId);
        Assert.Empty(config.RequiredCoursePointIds);
        Assert.True(entry.RequiredCoursePointsSatisfied);
        Assert.True(entry.IsValid);
        Assert.Null(entry.InvalidReason);
    }
}
