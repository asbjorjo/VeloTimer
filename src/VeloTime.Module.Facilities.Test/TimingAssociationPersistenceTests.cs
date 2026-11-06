using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Test;

public class TimingAssociationPersistenceTests
{
    [Fact]
    public void Model_HasAuthoritativeAssociationsAndRestrictiveRelationships()
    {
        using var storage = new FacilityDbContext(new DbContextOptionsBuilder<FacilityDbContext>()
            .UseNpgsql("Host=localhost;Database=unused").UseSnakeCaseNamingConvention().Options);
        var entity = storage.Model.FindEntityType(typeof(CoursePointTimingAssociation))!;
        Assert.False(entity.FindProperty(nameof(CoursePointTimingAssociation.CoursePointId))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(CoursePointTimingAssociation.TimingPointId))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(CoursePointTimingAssociation.ValidFromUtc))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(CoursePointTimingAssociation.ValidToUtc))!.IsNullable);
        Assert.Equal("timestamp with time zone", entity.FindProperty(nameof(CoursePointTimingAssociation.ValidFromUtc))!.GetColumnType());
        Assert.Null(storage.Model.FindEntityType(typeof(CoursePoint))!.FindProperty("TimingPoint"));
        Assert.Equal(nameof(CoursePoint.TimingAssociations), entity.GetForeignKeys().Single().PrincipalToDependent!.Name);
        Assert.All(storage.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()),
            fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.Equal(2, entity.GetIndexes().Count());
        Assert.False(storage.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Migration_PreflightsThenBackfillsThenDropsLegacyColumn()
    {
        using var storage = new FacilityDbContext(new DbContextOptionsBuilder<FacilityDbContext>()
            .UseNpgsql("Host=localhost;Database=unused",
                pg => pg.MigrationsHistoryTable("__ef_migrations_history", "ef"))
            .UseSnakeCaseNamingConvention().Options);
        var script = storage.GetService<IMigrator>().GenerateScript(
            "20260222113645_facilities_history_schema");
        Assert.Contains("CREATE EXTENSION IF NOT EXISTS btree_gist SCHEMA public", script);
        Assert.Contains("multiple coursepoints", script);
        Assert.Contains("DEFERRABLE INITIALLY IMMEDIATE", script);
        Assert.Contains("ex_timing_association_timing_point", script);
        Assert.Contains("ex_timing_association_course_point", script);
        var preflight = script.IndexOf("LOCK TABLE", StringComparison.Ordinal);
        var backfill = script.IndexOf("INSERT INTO facility.course_point_timing_association", StringComparison.Ordinal);
        var drop = script.IndexOf("DROP COLUMN timing_point", StringComparison.Ordinal);
        Assert.True(preflight >= 0 && preflight < backfill && backfill < drop);
        Assert.Throws<NotSupportedException>(() => storage.GetService<IMigrator>().GenerateScript(
            "20261102111211_facilities_time_bound_timing_associations",
            "20260222113645_facilities_history_schema"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("2026-01-01T12:00:00+02:00", null)]
    [InlineData(null, "2026-01-02T12:00:00-03:00")]
    [InlineData("2026-01-01T12:00:00+02:00", "2026-01-02T12:00:00-03:00")]
    public async Task Sqlite_PersistsNullableBoundsAndNormalizesInstants(string? from, string? to)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var storage = new FacilityDbContext(new DbContextOptionsBuilder<FacilityDbContext>()
            .UseSqlite(connection).Options);
        await storage.Database.EnsureCreatedAsync();
        var association = new CoursePointTimingAssociation
        {
            Id = Guid.NewGuid(),
            CoursePoint = new CoursePoint { Id = Guid.NewGuid() },
            TimingPointId = Guid.NewGuid(),
            ValidFromUtc = from is null ? null : DateTimeOffset.Parse(from),
            ValidToUtc = to is null ? null : DateTimeOffset.Parse(to)
        };
        Assert.Equal(from is null ? null : TimeSpan.Zero, association.ValidFromUtc?.Offset);
        Assert.Equal(to is null ? null : TimeSpan.Zero, association.ValidToUtc?.Offset);
        storage.Add(association);
        await storage.SaveChangesAsync();
        storage.ChangeTracker.Clear();
        var persisted = await storage.Set<CoursePointTimingAssociation>().Include(a => a.CoursePoint).SingleAsync();
        Assert.Equal(association.ValidFromUtc, persisted.ValidFromUtc);
        Assert.Equal(association.ValidToUtc, persisted.ValidToUtc);
        Assert.Equal(association.CoursePointId, persisted.CoursePoint.Id);
        Assert.Equal(association.Id, Assert.Single(persisted.CoursePoint.TimingAssociations).Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Sqlite_RejectsEmptyOrReversedFiniteInterval(int endDays)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var storage = new FacilityDbContext(new DbContextOptionsBuilder<FacilityDbContext>()
            .UseSqlite(connection).Options);
        await storage.Database.EnsureCreatedAsync();
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        storage.Add(new CoursePointTimingAssociation
        {
            CoursePoint = new CoursePoint { Id = Guid.NewGuid() },
            TimingPointId = Guid.NewGuid(), ValidFromUtc = from, ValidToUtc = from.AddDays(endDays)
        });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => storage.SaveChangesAsync());
        Assert.Contains("ck_course_point_timing_association_interval", error.InnerException!.Message);
    }

    [Fact]
    public void LegacyProperty_IsNotASilentMutableMapping()
    {
        var point = new CoursePoint { Id = Guid.NewGuid() };
#pragma warning disable CS0618
        Assert.Throws<NotSupportedException>(() => point.TimingPoint);
        Assert.Throws<NotSupportedException>(() => point.TimingPoint = Guid.NewGuid());
#pragma warning restore CS0618
    }
}
