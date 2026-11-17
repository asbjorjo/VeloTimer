using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    [PostgreSqlStatisticsFact]
    public async Task ManualSimpleAndMultiStatesSurviveThresholdSweepsAndRestoredParentAllowsReclassification()
    {
        var parent = Simple();
        var multiItem = new StatisticsItem { Id = Guid.NewGuid(), Name = "Double", Distance = 500 };
        var multi = new MultiStatisticsItemConfig { Id = Guid.NewGuid(), StatisticsItem = multiItem,
            StatisticsItemId = multiItem.Id, ParentConfig = parent, ParentConfigId = parent.Id, Repetitions = 2 };
        var rider = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var first = Entry(parent, 1, rider, facility);
        var second = Entry(parent, 2, rider, facility);
        var actor = Guid.NewGuid();
        var auditTime = new DateTimeOffset(Start);
        first.IsValid = false;
        first.InvalidReason = StatisticsEntryInvalidReason.ManuallyInvalidated;
        first.ManualInvalidationChangedByUserId = actor;
        first.ManualInvalidationChangedAtUtc = auditTime;
        first.ManualInvalidationNote = "Manual parent";
        var aggregate = new StatisticsEntry
        {
            Id = Guid.NewGuid(), StatisticsItem = multiItem, StatisticsItemId = multiItem.Id,
            StatisticsItemConfig = multi, StatisticsItemConfigId = multi.Id, TransponderId = rider,
            FacilityId = facility, TimeStart = first.TimeStart, TimeEnd = second.TimeEnd,
            Duration = TimeSpan.FromSeconds(20), Speed = 25
        };
        var manualAggregate = new StatisticsEntry
        {
            Id = Guid.NewGuid(), StatisticsItem = multiItem, StatisticsItemId = multiItem.Id,
            StatisticsItemConfig = multi, StatisticsItemConfigId = multi.Id, TransponderId = rider,
            FacilityId = facility, TimeStart = first.TimeStart, TimeEnd = second.TimeEnd,
            Duration = TimeSpan.FromSeconds(20), Speed = 25,
            IsValid = false, InvalidReason = StatisticsEntryInvalidReason.ManuallyInvalidated,
            ManualInvalidationChangedByUserId = actor, ManualInvalidationChangedAtUtc = auditTime,
            ManualInvalidationNote = "Manual aggregate"
        };
        await using (var db = Db())
        {
            db.AddRange(first, second, aggregate, manualAggregate);
            await db.SaveChangesAsync();
        }
        foreach (var minimum in new TimeSpan?[] { TimeSpan.FromSeconds(11), null, TimeSpan.FromSeconds(5) })
        {
            await Change(parent.StatisticsItemId, minimum);
            var parentBatches = await Drain(parent.StatisticsItemId, 1);
            Assert.Contains(parentBatches, batch => batch.Scanned == 1);
            await Change(multiItem.Id, minimum);
            await Drain(multiItem.Id, 1);
            await using var verify = Db();
            var rows = await verify.Set<StatisticsEntry>().AsNoTracking().ToDictionaryAsync(row => row.Id);
            Assert.Equal(4, rows.Count);
            AssertManual(rows[first.Id], "Manual parent");
            AssertManual(rows[manualAggregate.Id], "Manual aggregate");
            Assert.False(rows[aggregate.Id].IsValid);
            Assert.Equal(StatisticsEntryInvalidReason.InvalidContributor, rows[aggregate.Id].InvalidReason);
            Assert.Equal(minimum == TimeSpan.FromSeconds(11) ? StatisticsEntryInvalidReason.BelowMinimumDuration : (StatisticsEntryInvalidReason?)null,
                rows[second.Id].InvalidReason);
            Assert.Equal(first.TimeStart, rows[first.Id].TimeStart);
            Assert.Equal(first.TimeEnd, rows[first.Id].TimeEnd);
            Assert.Equal(first.Duration, rows[first.Id].Duration);
            Assert.Equal(first.Speed, rows[first.Id].Speed);
        }
        // Persist the post-restore fixture state and schedule its dependent job. The HTTP restore
        // and its scheduling are verified independently in StatisticsManualInvalidationTests.
        await using (var db = Db())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var scheduler = new StatisticsValidityScheduler(db);
            await scheduler.LockThresholdScopeAsync(parent.StatisticsItemId, true);
            var restored = await db.Set<StatisticsEntry>().SingleAsync(row => row.Id == first.Id);
            Assert.True(StatisticsValidityEvaluator.Apply(restored, null, allowManualInvalidationOverride: true));
            restored.ManualInvalidationNote = "Restored parent";
            await db.SaveChangesAsync();
            await scheduler.ScheduleDependentMultiRevalidationAsync([multiItem.Id]);
            await tx.CommitAsync();
        }
        await Drain(multiItem.Id, 1);
        await using (var verify = Db())
        {
            var rows = await verify.Set<StatisticsEntry>().AsNoTracking().ToDictionaryAsync(row => row.Id);
            Assert.Equal(4, rows.Count);
            Assert.True(rows[aggregate.Id].IsValid);
            Assert.Null(rows[aggregate.Id].InvalidReason);
            AssertManual(rows[manualAggregate.Id], "Manual aggregate");
            Assert.Equal("Restored parent", rows[first.Id].ManualInvalidationNote);
            Assert.Equal(actor, rows[first.Id].ManualInvalidationChangedByUserId);
            Assert.Equal(auditTime, rows[first.Id].ManualInvalidationChangedAtUtc);
        }

        void AssertManual(StatisticsEntry entry, string note)
        {
            Assert.False(entry.IsValid);
            Assert.Equal(StatisticsEntryInvalidReason.ManuallyInvalidated, entry.InvalidReason);
            Assert.Equal(actor, entry.ManualInvalidationChangedByUserId);
            Assert.Equal(auditTime, entry.ManualInvalidationChangedAtUtc);
            Assert.Equal(TimeSpan.Zero, entry.ManualInvalidationChangedAtUtc!.Value.Offset);
            Assert.Equal(note, entry.ManualInvalidationNote);
        }
    }

    [PostgreSqlStatisticsFact]
    public async Task ManualInvalidationMigrationPreservesLegacyCheckpointReasonAndGuardsRetainedAudit()
    {
        await using var db = Db();
        var migrator = db.GetService<IMigrator>();
        // Pin this migration boundary so later migrations do not change the scenario.
        const string predecessor = "20261116132807_statistics_sample_attempt_indexes";
        const string target = "20261117081217_statistics_manual_invalidation";
        await migrator.MigrateAsync(predecessor);
        var item = Guid.NewGuid();
        var config = Guid.NewGuid();
        var entry = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO statistics.statistics_item (id, name, description, distance)
            VALUES ({item}, $$Legacy$$, $$ $$, 250);
            INSERT INTO statistics.statistics_item_config
                (id, statistics_item_id, discriminator, course_point_start, course_point_end)
            VALUES ({config}, {item}, $$SimpleStatisticsItemConfig$$, {Guid.NewGuid()}, {Guid.NewGuid()});
            INSERT INTO statistics.statistics_entry
                (id, statistics_item_id, statistics_item_config_id, transponder_id, facility_id,
                 time_start, time_end, duration, speed, is_valid, invalid_reason, required_course_points_satisfied)
            VALUES ({entry}, {item}, {config}, {Guid.NewGuid()}, {Guid.NewGuid()},
                {Start}, {Start.AddSeconds(10)}, {TimeSpan.FromSeconds(10)}, {25d}, FALSE, 4, FALSE);
            """);
        await migrator.MigrateAsync(target);
        var legacy = await db.Set<StatisticsEntry>().AsNoTracking().SingleAsync();
        Assert.Equal(StatisticsEntryInvalidReason.RequiredCoursePointsNotSatisfied, legacy.InvalidReason);
        Assert.False(legacy.RequiredCoursePointsSatisfied);
        Assert.Null(legacy.ManualInvalidationChangedByUserId);
        Assert.Null(legacy.ManualInvalidationChangedAtUtc);
        Assert.Null(legacy.ManualInvalidationNote);
        Assert.Equal(DateTimeKind.Utc, legacy.TimeEnd.Kind);
        Assert.Equal(25d, legacy.Speed);
        var missingAudit = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"""
            UPDATE statistics.statistics_entry SET invalid_reason = 5 WHERE id = {entry}
            """));
        Assert.Equal("ck_statistics_entry_validity", missingAudit.ConstraintName);
        var actor = Guid.NewGuid();
        var time = new DateTimeOffset(Start);
        await db.Database.ExecuteSqlAsync($"""
            UPDATE statistics.statistics_entry SET invalid_reason = 5,
                manual_invalidation_changed_by_user_id = {actor}, manual_invalidation_changed_at_utc = {time},
                manual_invalidation_note = {new string('n', 500)} WHERE id = {entry}
            """);
        var oversized = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"""
            UPDATE statistics.statistics_entry SET manual_invalidation_note = {new string('n', 501)} WHERE id = {entry}
            """));
        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, oversized.SqlState);
        await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(predecessor));
        await db.Database.ExecuteSqlAsync($"""
            UPDATE statistics.statistics_entry SET invalid_reason = 4 WHERE id = {entry}
            """);
        // Clearing active state does not permit silently discarding the retained latest audit.
        await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(predecessor));
        var restored = await db.Set<StatisticsEntry>().AsNoTracking().SingleAsync();
        Assert.Equal(actor, restored.ManualInvalidationChangedByUserId);
        Assert.Equal(time, restored.ManualInvalidationChangedAtUtc);
        Assert.Equal(500, restored.ManualInvalidationNote!.Length);
        await db.Database.ExecuteSqlAsync($"""
            UPDATE statistics.statistics_entry SET manual_invalidation_changed_by_user_id = NULL,
                manual_invalidation_changed_at_utc = NULL, manual_invalidation_note = NULL WHERE id = {entry}
            """);
        await migrator.MigrateAsync(predecessor);
        await migrator.MigrateAsync(target);
        Assert.Equal(StatisticsEntryInvalidReason.RequiredCoursePointsNotSatisfied,
            (await db.Set<StatisticsEntry>().AsNoTracking().SingleAsync()).InvalidReason);
    }
}
