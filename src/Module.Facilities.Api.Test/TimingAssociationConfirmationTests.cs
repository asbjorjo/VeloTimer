using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeloTime.Module.Common;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Interface.Messages;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Service;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Api.Test;

public sealed class TimingAssociationPostgresFactAttribute : FactAttribute
{
    public TimingAssociationPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOTIME_TEST_POSTGRES")))
            Skip = "Set VELOTIME_TEST_POSTGRES to run interval confirmation tests; SQLite cannot compare DateTimeOffset bounds.";
    }
}

/// <summary>
/// Service regressions for schedule ordering and stale-safe confirmations. History ordering runs on
/// SQLite; interval writes use PostgreSQL (transaction + advisory lock path) when configured.
/// </summary>
public sealed class TimingAssociationConfirmationTests : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly string? postgresAdmin = Environment.GetEnvironmentVariable("VELOTIME_TEST_POSTGRES");
    private string? postgresDatabase;
    private string? postgres;
    private readonly IDataProtectionProvider protection = new EphemeralDataProtectionProvider();
    private readonly Guid facilityId = Guid.NewGuid();
    private readonly Guid source = Guid.NewGuid();
    private readonly Guid occupant = Guid.NewGuid();
    private readonly Guid sourceTiming = Guid.NewGuid();
    private readonly Guid destinationTiming = Guid.NewGuid();
    private readonly List<CoursePointTimingAssociationsChangedV1> events = [];

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        if (!string.IsNullOrWhiteSpace(postgresAdmin))
        {
            postgresDatabase = $"vt_facilities_confirm_{Guid.NewGuid():N}";
            await using var admin = new NpgsqlConnection(postgresAdmin);
            await admin.OpenAsync();
            await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{postgresDatabase}\"", admin))
                await create.ExecuteNonQueryAsync();
            postgres = new NpgsqlConnectionStringBuilder(postgresAdmin) { Database = postgresDatabase }.ConnectionString;
            await using var pg = CreateStorage(usePostgres: true);
            await pg.Database.MigrateAsync(); // includes deferrable exclusion constraints
            await SeedAsync(pg);
        }
        await using var storage = CreateStorage(usePostgres: false);
        await storage.Database.EnsureCreatedAsync();
        await SeedAsync(storage);
    }

    private async Task SeedAsync(FacilityDbContext storage)
    {
        var start = new CoursePoint { Id = source };
        var end = new CoursePoint { Id = occupant };
        storage.Add(new Facility { Id = facilityId, Name = "Track" });
        storage.Add(new CourseLayout
        {
            Id = Guid.NewGuid(), FacilityId = facilityId,
            Segments = [new CourseSegment { Id = Guid.NewGuid(), Order = 0, Start = start, End = end, Length = 250 }]
        });
        storage.AddRange(
            Association(source, sourceTiming, null, null),
            Association(occupant, destinationTiming, T0, null));
        await storage.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await connection.DisposeAsync();
        if (postgresDatabase is null) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(postgresAdmin);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{postgresDatabase}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task GetHistory_OrdersLowerNullFirstThenUpperAscendingWithNullLast()
    {
        await using (var storage = CreateStorage(usePostgres: false))
        {
            storage.RemoveRange(storage.Set<CoursePointTimingAssociation>().Where(a => a.CoursePointId == source));
            // SQLite has no exclusion constraint, so an equal lower bound can pin the
            // upper-bound tie-break: an unbounded (null) upper bound must sort last.
            storage.AddRange(
                Association(source, Guid.NewGuid(), T0, null),
                Association(source, Guid.NewGuid(), null, T0.AddDays(-10)),
                Association(source, Guid.NewGuid(), T0, T0.AddDays(5)),
                Association(source, Guid.NewGuid(), T0.AddDays(-10), T0));
            await storage.SaveChangesAsync();
        }

        await using var read = CreateStorage(usePostgres: false);
        var history = await new TimingAssociationService(read, protection).GetHistoryAsync(source, default);

        Assert.Equal(
            [(null, T0.AddDays(-10)), (T0.AddDays(-10), T0), (T0, T0.AddDays(5)), (T0, null)],
            history.Select(h => (h.ValidFromUtc, h.ValidToUtc)).ToArray());
    }

    [TimingAssociationPostgresFact]
    public async Task Confirmation_SucceedsWhenSourceAndDestinationAreUnchanged()
    {
        var preview = await PreviewAsync();

        var result = await SetAsync(Request(preview.ConfirmationToken));

        Assert.Null(result.ConflictPreview);
        Assert.NotNull(result.ChangeId);
        Assert.Single(events);
    }

    [TimingAssociationPostgresFact]
    public async Task Confirmation_IsStaleWhenSourceMappingChangedAfterPreview()
    {
        var preview = await PreviewAsync();
        await using (var storage = CreateStorage(usePostgres: true))
        {
            // Destination occupant is untouched; only the requested coursepoint's source row changes.
            var row = await storage.Set<CoursePointTimingAssociation>().SingleAsync(a => a.CoursePointId == source);
            storage.Remove(row);
            storage.AddRange(
                Association(source, sourceTiming, null, T0.AddDays(2)),
                Association(source, Guid.NewGuid(), T0.AddDays(2), null));
            await storage.SaveChangesAsync();
        }

        var result = await SetAsync(Request(preview.ConfirmationToken));

        Assert.Null(result.ChangeId);
        Assert.Empty(events);
        var fresh = Assert.IsType<TimingAssociationConflictPreview>(result.ConflictPreview);
        Assert.Equal("confirmation-stale", fresh.Code);
        Assert.Equal(preview.Conflicts, fresh.Conflicts);
        Assert.NotEqual(preview.ConfirmationToken, fresh.ConfirmationToken);
        await using var verify = CreateStorage(usePostgres: true);
        Assert.Equal(3, await verify.Set<CoursePointTimingAssociation>().CountAsync());
        Assert.True(await verify.Set<CoursePointTimingAssociation>()
            .AnyAsync(a => a.CoursePointId == occupant && a.TimingPointId == destinationTiming));

        // The fresh token binds the new source state and can be confirmed.
        var confirmed = await SetAsync(Request(fresh.ConfirmationToken));
        Assert.Null(confirmed.ConflictPreview);
        Assert.Single(events);
    }

    private async Task<TimingAssociationConflictPreview> PreviewAsync()
    {
        var result = await SetAsync(Request(null));
        var preview = Assert.IsType<TimingAssociationConflictPreview>(result.ConflictPreview);
        Assert.Equal("destination-occupied", preview.Code);
        Assert.Empty(events);
        return preview;
    }

    private SetCoursePointTimingAssociationRequest Request(string? token) => new()
    {
        TimingPointId = destinationTiming,
        ValidFromUtc = T0.AddDays(1),
        ValidToUtc = null,
        ConfirmationToken = token
    };

    private async Task<TimingAssociationEditResult> SetAsync(SetCoursePointTimingAssociationRequest request)
    {
        await using var storage = CreateStorage(usePostgres: true);
        return await new TimingAssociationService(storage, protection).SetIntervalAsync(
            source, request, T0.AddDays(3), new SameFacilityReferences(facilityId),
            (message, _) => { events.Add(message); return Task.CompletedTask; }, default);
    }

    private FacilityDbContext CreateStorage(bool usePostgres) => usePostgres
        ? new(new DbContextOptionsBuilder<FacilityDbContext>()
            .UseNpgsql(postgres!, pg => pg.MigrationsHistoryTable("__ef_migrations_history", "ef"))
            .UseSnakeCaseNamingConvention().Options)
        : new(new DbContextOptionsBuilder<FacilityDbContext>().UseSqlite(connection).Options);

    private static CoursePointTimingAssociation Association(Guid coursePoint, Guid timingPoint,
        DateTimeOffset? from, DateTimeOffset? to) => new()
    {
        Id = Guid.NewGuid(), CoursePointId = coursePoint, TimingPointId = timingPoint,
        ValidFromUtc = from, ValidToUtc = to
    };

    private sealed class SameFacilityReferences(Guid facilityId) : IAdministrationReferences
    {
        public Task<EntityReference?> GetAsync(string module, string kind, Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<EntityReference?>(new EntityReference(id, facilityId));

        public Task<DependencySummary> GetDependenciesAsync(string module, string kind, Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
