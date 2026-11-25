using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    [PostgreSqlStatisticsFact]
    public async Task EntryOutboxMigrationPreservesUnknownHistoryAndGuardsRollback()
    {
        const string previous = "20261123135436_statistics_sample_complete_outbox";
        var entryId = Guid.NewGuid();
        await RecreateAtMigrationAsync(previous);
        await using var db = Db();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE statistics.smb_outbox (sentinel integer PRIMARY KEY);
            INSERT INTO statistics.smb_outbox VALUES (42);
            INSERT INTO statistics.sample_complete_outbox
                (sample_id, transponder_id, time_start, time_end, course_point_start, course_point_end, distance, delivered_at_utc)
            VALUES (gen_random_uuid(), gen_random_uuid(), now(), now(), gen_random_uuid(), gen_random_uuid(), 1, now());
            """);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeSampleActivation);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM statistics.sample_complete_outbox
            WHERE delivered_at_utc IS NOT NULL AND entries_created_at_utc IS NULL
            """).SingleAsync());
        Assert.Equal(42, await db.Database.SqlQueryRaw<int>("SELECT sentinel AS \"Value\" FROM statistics.smb_outbox").SingleAsync());
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO statistics.entry_created_outbox
                (entry_id, transponder_id, time_start, time_end, statistics_item_id, config_item_id, single)
            VALUES ({entryId}, {Guid.NewGuid()}, {Start}, {Start.AddSeconds(1)}, {Guid.NewGuid()}, {Guid.NewGuid()}, TRUE)
            """);
        foreach (var delivered in new[] { false, true })
        {
            if (delivered)
                await db.Database.ExecuteSqlAsync($"UPDATE statistics.entry_created_outbox SET delivered_at_utc = {DateTime.UtcNow} WHERE entry_id = {entryId}");
            var failure = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
            Assert.Contains("Cannot drop EntryCreated outbox", failure.MessageText);
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM statistics.entry_created_outbox""").SingleAsync());
        }
    }

    [PostgreSqlStatisticsFact]
    public async Task MultiDecisionMigrationUpgradesLegacyReceiptAndGuardsRetainedDecisions()
    {
        await RecreateAtMigrationAsync(BeforeSampleActivation);
        await using var db = Db();
        await AssertMigrationsPendingAfterEntryOutboxAsync(db);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(SampleActivationMigration);
        Assert.Equal(new[] { MultiDecisionMigration, SmbHeadMigration }, await db.Database.GetPendingMigrationsAsync());
        var entryId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO statistics.entry_created_outbox
                (entry_id, transponder_id, time_start, time_end, statistics_item_id, config_item_id, single)
            VALUES ({entryId}, {Guid.NewGuid()}, {Start}, {Start.AddSeconds(1)}, {Guid.NewGuid()}, {Guid.NewGuid()}, TRUE)
            """);
        await migrator.MigrateAsync(MultiDecisionMigration);
        Assert.Equal(new[] { SmbHeadMigration }, await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM statistics.entry_created_outbox
            WHERE multi_decision_version = 0 AND multi_decisions_created_at_utc IS NULL
            """).SingleAsync());
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO statistics.multi_creation_decision (parent_entry_id, multi_config_id, result_entry_id)
            VALUES ({entryId}, {configId}, NULL)
            """);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(SampleActivationMigration));
        Assert.Contains("Cannot remove retained Multi decisions", failure.MessageText);
        Assert.Contains(MultiDecisionMigration, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM statistics.multi_creation_decision WHERE result_entry_id IS NULL
            """).SingleAsync());
        await db.Database.ExecuteSqlRawAsync("DELETE FROM statistics.multi_creation_decision");
        await migrator.MigrateAsync(SampleActivationMigration);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM statistics.entry_created_outbox""").SingleAsync());
        await migrator.MigrateAsync(MultiDecisionMigration);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM statistics.entry_created_outbox
            WHERE multi_decision_version = 0 AND multi_decisions_created_at_utc IS NULL
            """).SingleAsync());
    }
}
