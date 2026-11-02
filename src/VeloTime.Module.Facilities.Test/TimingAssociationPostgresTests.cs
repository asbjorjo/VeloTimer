using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Test;

public sealed class FacilitiesPostgresFactAttribute : FactAttribute
{
    public FacilitiesPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FacilitiesAssociationDatabase.ConfigurationKey)))
            Skip = $"Requires {FacilitiesAssociationDatabase.ConfigurationKey} targeting Database=postgres.";
    }
}

// Follows the repository's Profiles fixture: only internally generated disposable databases are
// migrated/deleted, never an existing application database supplied by the caller.
internal sealed class FacilitiesAssociationDatabase : IAsyncDisposable
{
    public const string ConfigurationKey = "FACILITIES_TEST_POSTGRES_ADMIN_CONNECTION";
    private readonly string name = "facilities_associations_" + Guid.NewGuid().ToString("N");
    private readonly string adminConnection;
    private bool created;
    public string ConnectionString { get; }

    public FacilitiesAssociationDatabase()
    {
        var admin = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(ConfigurationKey)) { Pooling = false };
        if (admin.Database != "postgres")
            throw new InvalidOperationException($"{ConfigurationKey} must target Database=postgres.");
        adminConnection = admin.ConnectionString;
        ConnectionString = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name }.ConnectionString;
    }

    public async Task InitializeAsync(bool legacy = false)
    {
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\" TEMPLATE template0", connection);
        await command.ExecuteNonQueryAsync();
        created = true;
        await using var storage = CreateContext();
        await storage.GetService<IMigrator>().MigrateAsync(legacy ? "20260222113645_facilities_history_schema" : null);
    }

    public FacilityDbContext CreateContext() => new(new DbContextOptionsBuilder<FacilityDbContext>()
        .UseNpgsql(ConnectionString, pg => pg.MigrationsHistoryTable("__ef_migrations_history", "ef"))
        .UseSnakeCaseNamingConvention().Options);

    public async ValueTask DisposeAsync()
    {
        if (!created) return;
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}

public class TimingAssociationPostgresTests
{
    private static Task<int> AddPoint(FacilityDbContext storage, Guid point) =>
        storage.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO facility.course_point (id) VALUES ({point})");

    private static Task<int> AddAssociation(FacilityDbContext storage, Guid point, Guid timing,
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default) =>
        storage.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO facility.course_point_timing_association
                (id, course_point_id, timing_point_id, valid_from_utc, valid_to_utc)
            VALUES ({Guid.NewGuid()}, {point}, {timing}, {from}, {to})
            """, cancellationToken);

    [FacilitiesPostgresFact]
    public async Task EfPersistence_NormalizesOffsetBoundsAndMaterializesUtc()
    {
        await using var database = new FacilitiesAssociationDatabase();
        await database.InitializeAsync();
        await using var storage = database.CreateContext();
        var from = DateTimeOffset.Parse("2026-01-01T12:00:00+02:00");
        var to = DateTimeOffset.Parse("2026-01-02T12:00:00-03:00");
        var association = new CoursePointTimingAssociation
        {
            CoursePoint = new CoursePoint { Id = Guid.NewGuid() },
            TimingPointId = Guid.NewGuid(), ValidFromUtc = from, ValidToUtc = to
        };
        Assert.Equal(TimeSpan.Zero, association.ValidFromUtc!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, association.ValidToUtc!.Value.Offset);
        storage.Add(association);
        await storage.SaveChangesAsync();
        storage.ChangeTracker.Clear();
        var persisted = await storage.Set<CoursePointTimingAssociation>().SingleAsync();
        Assert.Equal(from.ToUniversalTime(), persisted.ValidFromUtc);
        Assert.Equal(to.ToUniversalTime(), persisted.ValidToUtc);
        Assert.Equal(TimeSpan.Zero, persisted.ValidFromUtc!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, persisted.ValidToUtc!.Value.Offset);
    }

    [FacilitiesPostgresFact]
    public async Task Backfill_PreservesEveryMappedPointAsAllTimeAndLeavesUnmappedPoints()
    {
        await using var database = new FacilitiesAssociationDatabase();
        await database.InitializeAsync(legacy: true);
        await using var storage = database.CreateContext();
        var point = Guid.NewGuid();
        var timing = Guid.NewGuid();
        await storage.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO facility.course_point (id, timing_point) VALUES ({point}, {timing}), ({Guid.NewGuid()}, NULL)");
        await storage.Database.MigrateAsync();
        var association = await storage.Set<VeloTime.Module.Facilities.Model.CoursePointTimingAssociation>().SingleAsync();
        Assert.Equal(point, association.Id);
        Assert.Equal(point, association.CoursePointId);
        Assert.Equal(timing, association.TimingPointId);
        Assert.Null(association.ValidFromUtc);
        Assert.Null(association.ValidToUtc);
        Assert.Equal(2, await storage.Set<VeloTime.Module.Facilities.Model.CoursePoint>().CountAsync());
        Assert.Equal(0, await storage.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM information_schema.columns
            WHERE table_schema = 'facility' AND table_name = 'course_point' AND column_name = 'timing_point'
            """).SingleAsync());
    }

    [FacilitiesPostgresFact]
    public async Task DuplicateLegacyMapping_FailsExplicitlyAndRollsBackWithoutDeletingData()
    {
        await using var database = new FacilitiesAssociationDatabase();
        await database.InitializeAsync(legacy: true);
        await using var storage = database.CreateContext();
        var timing = Guid.NewGuid();
        await storage.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO facility.course_point (id, timing_point) VALUES ({Guid.NewGuid()}, {timing}), ({Guid.NewGuid()}, {timing})");
        var error = await Assert.ThrowsAsync<PostgresException>(() => storage.Database.MigrateAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Contains("mapped to multiple coursepoints", error.MessageText);
        Assert.Equal(2, await storage.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM facility.course_point WHERE timing_point = {timing}
            """).SingleAsync());
        Assert.Equal(2, (await storage.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(0, await storage.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM information_schema.tables
            WHERE table_schema = 'facility' AND table_name = 'course_point_timing_association'
            """).SingleAsync());
    }

    [FacilitiesPostgresFact]
    public async Task Exclusions_RejectBothIdentitiesAndAllowAdjacentHalfOpenRanges()
    {
        await using var database = new FacilitiesAssociationDatabase();
        await database.InitializeAsync();
        await using var storage = database.CreateContext();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var timing = Guid.NewGuid();
        var instant = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        await AddPoint(storage, first);
        await AddPoint(storage, second);
        await AddAssociation(storage, first, timing, null, instant);
        await AddAssociation(storage, second, timing, instant, null);
        var timingConflict = await Assert.ThrowsAsync<PostgresException>(() =>
            AddAssociation(storage, first, timing, instant.AddDays(-1), instant.AddDays(1)));
        Assert.Equal(PostgresErrorCodes.ExclusionViolation, timingConflict.SqlState);
        var courseConflict = await Assert.ThrowsAsync<PostgresException>(() =>
            AddAssociation(storage, first, Guid.NewGuid(), null, instant));
        Assert.Equal("ex_timing_association_course_point", courseConflict.ConstraintName);
        var allTimeConflict = await Assert.ThrowsAsync<PostgresException>(() =>
            AddAssociation(storage, second, Guid.NewGuid(), null, null));
        Assert.Equal(PostgresErrorCodes.ExclusionViolation, allTimeConflict.SqlState);
        await AddAssociation(storage, first, Guid.NewGuid(), instant, instant.AddDays(1));
    }

    [FacilitiesPostgresFact]
    public async Task Checks_RejectEmptyReversedAndExplicitInfiniteBounds()
    {
        await using var database = new FacilitiesAssociationDatabase();
        await database.InitializeAsync();
        await using var storage = database.CreateContext();
        var point = Guid.NewGuid();
        await AddPoint(storage, point);
        var instant = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        foreach (var end in new[] { instant, instant.AddDays(-1) })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                AddAssociation(storage, point, Guid.NewGuid(), instant, end));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
        var infinity = await Assert.ThrowsAsync<PostgresException>(() =>
            storage.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO facility.course_point_timing_association
                    (id, course_point_id, timing_point_id, valid_from_utc)
                VALUES ({Guid.NewGuid()}, {point}, {Guid.NewGuid()}, '-infinity'::timestamptz)
                """));
        Assert.Equal("ck_course_point_timing_association_finite_bounds", infinity.ConstraintName);
    }

    [FacilitiesPostgresFact]
    public async Task ForeignKeys_BlockOrphansAndHistoryDestroyingParentDeletes()
    {
        await using var database = new FacilitiesAssociationDatabase();
        await database.InitializeAsync();
        await using var storage = database.CreateContext();
        var orphan = await Assert.ThrowsAsync<PostgresException>(() =>
            AddAssociation(storage, Guid.NewGuid(), Guid.NewGuid(), null, null));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, orphan.SqlState);
        var point = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var layout = Guid.NewGuid();
        await AddPoint(storage, point);
        await AddAssociation(storage, point, Guid.NewGuid(), null, null);
        await storage.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO facility.facility (id, name) VALUES ({facility}, 'test')");
        await storage.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO facility.course_layout (id, facility_id) VALUES ({layout}, {facility})");
        await storage.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO facility.course_segment (id, course_layout_id, start_id, end_id, "order", length)
            VALUES ({Guid.NewGuid()}, {layout}, {point}, {point}, 0, 1)
            """);
        foreach (var sql in new[]
        {
            $"DELETE FROM facility.course_point WHERE id = '{point}'",
            $"DELETE FROM facility.course_layout WHERE id = '{layout}'",
            $"DELETE FROM facility.facility WHERE id = '{facility}'"
        })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => storage.Database.ExecuteSqlRawAsync(sql));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        }
        Assert.Equal(1, await storage.Set<VeloTime.Module.Facilities.Model.CoursePointTimingAssociation>().CountAsync());
    }

    [FacilitiesPostgresFact]
    public async Task ConcurrentOverlap_IsRejectedAfterFirstWriterCommits()
    {
        await using var database = new FacilitiesAssociationDatabase();
        await database.InitializeAsync();
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        await using var observer = database.CreateContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await second.Database.OpenConnectionAsync(timeout.Token);
        var secondPid = await second.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"")
            .SingleAsync(timeout.Token);
        var point = Guid.NewGuid();
        var other = Guid.NewGuid();
        var timing = Guid.NewGuid();
        await AddPoint(first, point);
        await AddPoint(first, other);
        await using var transaction = await first.Database.BeginTransactionAsync();
        await AddAssociation(first, point, timing, null, null);
        var competing = AddAssociation(second, other, timing, null, null, timeout.Token);
        // Observe the real competing INSERT waiting on PostgreSQL's lock, not merely two
        // sequential inserts that happen to use different contexts.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var blocked = false;
        while (!blocked && DateTime.UtcNow < deadline)
        {
            // Observe this exact connection outside the writer transaction. pg_stat_activity
            // can retain its first statistics snapshot for that entire transaction and miss
            // the INSERT starting between polls; pg_blocking_pids reads the live lock manager.
            blocked = await observer.Database.SqlQuery<int>($"""
                SELECT cardinality(pg_blocking_pids({secondPid})) AS "Value"
                """).SingleAsync(timeout.Token) > 0;
            if (!blocked) await Task.Delay(25);
        }
        Assert.True(blocked, "Expected the competing INSERT to wait on the uncommitted exclusion conflict.");
        Assert.False(competing.IsCompleted);
        await transaction.CommitAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(async () => await competing.WaitAsync(timeout.Token));
        Assert.Equal(PostgresErrorCodes.ExclusionViolation, error.SqlState);
    }

    [FacilitiesPostgresFact]
    public async Task DeferredReplacement_CanInsertBeforeClosingOldIntervalAtomically()
    {
        await using var database = new FacilitiesAssociationDatabase();
        await database.InitializeAsync();
        await using var storage = database.CreateContext();
        var point = Guid.NewGuid();
        var other = Guid.NewGuid();
        var timing = Guid.NewGuid();
        var instant = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        await AddPoint(storage, point);
        await AddPoint(storage, other);
        await AddAssociation(storage, point, timing, null, null);
        await using var transaction = await storage.Database.BeginTransactionAsync();
        await storage.Database.ExecuteSqlRawAsync(
            "SET CONSTRAINTS facility.ex_timing_association_timing_point, facility.ex_timing_association_course_point DEFERRED");
        await AddAssociation(storage, other, timing, instant, null);
        await storage.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE facility.course_point_timing_association SET valid_to_utc = {instant} WHERE course_point_id = {point}
            """);
        await transaction.CommitAsync();
        Assert.Equal(2, await storage.Set<VeloTime.Module.Facilities.Model.CoursePointTimingAssociation>().CountAsync());
    }
}
