using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    private const string BeforeSampleActivation = "20261123145848_statistics_entry_created_outbox";
    private const string SampleActivationMigration = "20261123151518_statistics_sample_identity_activation";
    private const string MultiDecisionMigration = "20261124095835_statistics_multi_creation_decisions";
    private const string SmbHeadMigration = "20261125075737_statistics_processor_smb_head";

    private static async Task AssertMigrationsPendingAfterEntryOutboxAsync(StatisticsDbContext db)
    {
        Assert.Equal(new[] { SampleActivationMigration, MultiDecisionMigration, SmbHeadMigration },
            await db.Database.GetPendingMigrationsAsync());
    }

    [PostgreSqlStatisticsFact]
    public async Task SampleActivationSucceedsOnFreshAndEmptyExistingDatabases()
    {
        await using (var fresh = Db())
        {
            Assert.Empty(await fresh.Database.GetPendingMigrationsAsync());
            Assert.Contains(SampleActivationMigration, await fresh.Database.GetAppliedMigrationsAsync());
        }
        await RecreateAtMigrationAsync(BeforeSampleActivation);
        await using var db = Db();
        await AssertMigrationsPendingAfterEntryOutboxAsync(db);
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [PostgreSqlStatisticsFact]
    public async Task SampleActivationResetsAllDerivedRowsAndCheckpointsAtomically()
    {
        await RecreateAtMigrationAsync("20261118125831_statistics_lap_distance_activation");
        await using var db = Db();
        var config = Simple();
        var multi = ReplayMulti(config);
        var untouched = Simple();
        var profile = new StatsProfile { UserId = Guid.NewGuid(), ProfileName = "Preserved" };
        var entry = Entry(config, 123);
        entry.StatsProfile = profile;
        entry.IsValid = false;
        entry.InvalidReason = StatisticsEntryInvalidReason.ManuallyInvalidated;
        entry.ManualInvalidationChangedByUserId = Guid.NewGuid();
        entry.ManualInvalidationChangedAtUtc = DateTimeOffset.UtcNow;
        entry.ManualInvalidationNote = "reset audit fixture";
        db.AddRange(config, multi, untouched, entry, new StatisticsEntry
        {
            Id = Guid.NewGuid(), StatisticsItemId = multi.StatisticsItem.Id,
            StatisticsItemConfigId = multi.Id, TransponderId = Guid.NewGuid(), FacilityId = Guid.NewGuid(),
            TimeStart = Start, TimeEnd = Start.AddSeconds(1), Duration = TimeSpan.FromSeconds(1), Speed = 1
        });
        await db.SaveChangesAsync();
        var item = config.StatisticsItemId;
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO statistics.sample
                (id, transponder_id, facility_id, course_point_start_id, course_point_end_id,
                 time_start, time_end, duration, distance, speed)
            VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()},
                    {Start}, {Start.AddSeconds(1)}, {TimeSpan.FromSeconds(1)}, 100, 100);
            UPDATE statistics.statistics_validity_revalidation
            SET generation = 7, is_pending = FALSE, phase = 1, last_entry_id = {entry.Id},
                multi_rescan_pending = TRUE WHERE statistics_item_id = {item};
            INSERT INTO statistics.statistics_validity_revalidation
                (statistics_item_id, generation, is_pending, phase, last_entry_id, multi_rescan_pending)
            VALUES ({item}, 7, FALSE, 1, {entry.Id}, TRUE)
            ON CONFLICT (statistics_item_id) DO NOTHING;
            INSERT INTO statistics.statistics_validity_revalidation
                (statistics_item_id, generation, is_pending, phase, last_entry_id, multi_rescan_pending)
            VALUES ({multi.StatisticsItemId}, 13, TRUE, 1, {entry.Id}, TRUE);
            CREATE TABLE statistics.smb_outbox (sentinel integer PRIMARY KEY);
            INSERT INTO statistics.smb_outbox VALUES (42);
            """);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeSampleActivation);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO statistics.sample
                (id, transponder_id, facility_id, course_point_start_id, course_point_end_id,
                 time_start, time_end, duration, distance, speed, timing_point_start_id, timing_point_end_id)
            VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()},
                    {Start}, {Start.AddSeconds(1)}, {TimeSpan.FromSeconds(1)}, 100, 100, {Guid.NewGuid()}, {Guid.NewGuid()});
            """);
        Assert.Equal(2, await db.Set<StatisticsEntry>().CountAsync());
        Assert.Equal(2, await db.Set<Sample>().CountAsync());
        await migrator.MigrateAsync();
        Assert.Empty(await db.Set<Sample>().AsNoTracking().ToListAsync());
        Assert.Empty(await db.Set<StatisticsEntry>().AsNoTracking().ToListAsync());
        Assert.Equal(1, await db.Set<StatsProfile>().CountAsync());
        Assert.Equal(3, await db.Set<StatisticsItemConfig>().CountAsync());
        Assert.Equal(42, await db.Database.SqlQueryRaw<int>("SELECT sentinel AS \"Value\" FROM statistics.smb_outbox").SingleAsync());
        var jobs = await db.Set<StatisticsValidityRevalidation>().AsNoTracking().ToListAsync();
        Assert.Equal(3, jobs.Count);
        var job = jobs.Single(j => j.StatisticsItemId == item);
        Assert.Equal(8, job.Generation);
        Assert.True(job.IsPending);
        Assert.Equal(StatisticsValidityRevalidationPhase.Simple, job.Phase);
        Assert.Null(job.LastEntryId);
        Assert.False(job.MultiRescanPending);
        var pending = jobs.Single(j => j.StatisticsItemId == multi.StatisticsItemId);
        Assert.Equal(14, pending.Generation);
        Assert.True(pending.IsPending);
        Assert.Equal(StatisticsValidityRevalidationPhase.Simple, pending.Phase);
        Assert.Null(pending.LastEntryId);
        Assert.False(pending.MultiRescanPending);
        var missing = jobs.Single(j => j.StatisticsItemId == untouched.StatisticsItemId);
        Assert.Equal(1, missing.Generation);
        Assert.True(missing.IsPending);
        Assert.Null(missing.LastEntryId);
        var legacy = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO statistics.sample
                (id, transponder_id, facility_id, course_point_start_id, course_point_end_id,
                 time_start, time_end, duration, distance, speed)
            VALUES (gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), gen_random_uuid(),
                    now(), now(), interval '1 second', 100, 100);
            """));
        Assert.Equal(PostgresErrorCodes.NotNullViolation, legacy.SqlState);
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.columns
            WHERE table_schema = 'statistics' AND table_name = 'sample'
                AND column_name IN ('timing_point_start_id', 'timing_point_end_id')
                AND is_nullable = 'NO' AND column_default IS NULL
            """).SingleAsync());
        var rollback = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(BeforeSampleActivation));
        Assert.Contains("cannot be rolled back safely", rollback.MessageText);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgreSqlStatisticsFact]
    public async Task SampleActivationRejectsEveryRetainedOutboxReceiptAndUnexpectedDependencies()
    {
        await RecreateAtMigrationAsync(BeforeSampleActivation);
        await using var db = Db();
        var migrator = db.GetService<IMigrator>();
        foreach (var table in new[] { "sample_complete_outbox", "entry_created_outbox" })
        foreach (var delivered in new[] { false, true })
        {
            var mark = delivered ? "now()" : "NULL";
            var insert = table == "sample_complete_outbox"
                ? $"""
                    INSERT INTO statistics.sample_complete_outbox
                    (sample_id, transponder_id, time_start, time_end, course_point_start, course_point_end, distance, delivered_at_utc)
                    VALUES (gen_random_uuid(), gen_random_uuid(), now(), now(), gen_random_uuid(), gen_random_uuid(), 1, {mark})
                    """
                : $"""
                    INSERT INTO statistics.entry_created_outbox
                    (entry_id, transponder_id, time_start, time_end, statistics_item_id, config_item_id, single, delivered_at_utc)
                    VALUES (gen_random_uuid(), gen_random_uuid(), now(), now(), gen_random_uuid(), gen_random_uuid(), TRUE, {mark})
                    """;
            await db.Database.ExecuteSqlRawAsync(insert);
            var failure = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
            Assert.Contains("both isolated outboxes", failure.MessageText);
            var countSql = table == "sample_complete_outbox"
                ? "SELECT count(*) AS \"Value\" FROM statistics.sample_complete_outbox"
                : "SELECT count(*) AS \"Value\" FROM statistics.entry_created_outbox";
            Assert.Equal(1L, await db.Database.SqlQueryRaw<long>(countSql).SingleAsync());
            await db.Database.ExecuteSqlRawAsync(table == "sample_complete_outbox"
                ? "DELETE FROM statistics.sample_complete_outbox"
                : "DELETE FROM statistics.entry_created_outbox");
        }
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE statistics.reset_dependency
            (sample_id uuid REFERENCES statistics.sample(id) ON DELETE CASCADE);
            """);
        var fk = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
        Assert.Contains("unexpected inbound foreign key", fk.MessageText);
        await db.Database.ExecuteSqlRawAsync("DROP TABLE statistics.reset_dependency");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION statistics.reset_trigger() RETURNS trigger LANGUAGE plpgsql AS
            $$ BEGIN RETURN OLD; END $$;
            CREATE TRIGGER reset_trigger BEFORE DELETE ON statistics.sample
            FOR EACH ROW EXECUTE FUNCTION statistics.reset_trigger();
            """);
        var trigger = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
        Assert.Contains("unexpected triggers", trigger.MessageText);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reset_trigger ON statistics.sample");
        await migrator.MigrateAsync();
    }

    [PostgreSqlStatisticsFact]
    public async Task SampleActivationRollsBackDeletesWhenCheckpointGenerationOverflows()
    {
        await RecreateAtMigrationAsync(BeforeSampleActivation);
        await using var db = Db();
        var config = Simple();
        db.AddRange(config, Entry(config, 1));
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO statistics.statistics_validity_revalidation
                (statistics_item_id, generation, is_pending, phase, last_entry_id, multi_rescan_pending)
            VALUES ({config.StatisticsItemId}, {long.MaxValue}, TRUE, 0, NULL, FALSE);
            """);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(SampleActivationMigration));
        Assert.Equal(PostgresErrorCodes.NumericValueOutOfRange, failure.SqlState);
        Assert.Equal(1, await db.Set<StatisticsEntry>().CountAsync());
        Assert.Equal(long.MaxValue, (await db.Set<StatisticsValidityRevalidation>().SingleAsync()).Generation);
        await AssertMigrationsPendingAfterEntryOutboxAsync(db);
    }

    [PostgreSqlStatisticsFact]
    public async Task SampleActivationWaitsForInFlightOutboxAndRejectsCommittedReceipt()
    {
        await RecreateAtMigrationAsync(BeforeSampleActivation);
        await using var writer = Db();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlRawAsync("""
            INSERT INTO statistics.sample_complete_outbox
                (sample_id, transponder_id, time_start, time_end, course_point_start, course_point_end, distance)
            VALUES (gen_random_uuid(), gen_random_uuid(), now(), now(), gen_random_uuid(), gen_random_uuid(), 1);
            """);
        await using var migration = Db();
        var task = migration.GetService<IMigrator>().MigrateAsync(SampleActivationMigration);
        await Task.Delay(300);
        Assert.False(task.IsCompleted);
        await transaction.CommitAsync();
        var failure = await Assert.ThrowsAsync<PostgresException>(() => task);
        Assert.Contains("both isolated outboxes", failure.MessageText);
        Assert.Equal(1, await writer.Set<SampleCompleteOutbox>().CountAsync());
        await AssertMigrationsPendingAfterEntryOutboxAsync(migration);
    }

    [PostgreSqlStatisticsFact]
    public async Task SampleActivationContinuesAfterInFlightOutboxTransactionAborts()
    {
        await RecreateAtMigrationAsync(BeforeSampleActivation);
        await using var writer = Db();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlRawAsync("""
            INSERT INTO statistics.entry_created_outbox
                (entry_id, transponder_id, time_start, time_end, statistics_item_id, config_item_id, single)
            VALUES (gen_random_uuid(), gen_random_uuid(), now(), now(), gen_random_uuid(), gen_random_uuid(), TRUE);
            """);
        await using var migration = Db();
        var task = migration.GetService<IMigrator>().MigrateAsync();
        await Task.Delay(300);
        Assert.False(task.IsCompleted);
        await transaction.RollbackAsync();
        await task;
        Assert.Empty(await migration.Database.GetPendingMigrationsAsync());
        Assert.Empty(await writer.Set<EntryCreatedOutbox>().ToListAsync());
    }
}
