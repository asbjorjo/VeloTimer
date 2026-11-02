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
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FACILITIES_TEST_POSTGRES_ADMIN_CONNECTION")))
            Skip = "Set FACILITIES_TEST_POSTGRES_ADMIN_CONNECTION to a disposable test-only PostgreSQL server; SQLite cannot prove interval constraints.";
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
    private readonly string? postgresAdmin = Environment.GetEnvironmentVariable("FACILITIES_TEST_POSTGRES_ADMIN_CONNECTION");
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
            if (new NpgsqlConnectionStringBuilder(postgresAdmin).Database != "postgres")
                throw new InvalidOperationException("The test-only PostgreSQL admin connection must use Database=postgres.");
            postgresDatabase = $"vt_facilities_confirm_{Guid.NewGuid():N}";
            await using var admin = new NpgsqlConnection(postgresAdmin);
            await admin.OpenAsync();
            await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{postgresDatabase}\" TEMPLATE template0", admin))
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
        using var pooledConnection = new NpgsqlConnection(postgres);
        NpgsqlConnection.ClearPool(pooledConnection);
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

    [TimingAssociationPostgresFact]
    public async Task FiniteReplacement_PreviewsExactWindowWithoutWritesAndRestoresBothMappings()
    {
        var request = Request(null) with { ValidToUtc = T0.AddDays(2) };
        var before = await RowsAsync();
        var rejected = await SetAsync(request);
        var preview = Assert.IsType<TimingAssociationConflictPreview>(rejected.ConflictPreview);
        Assert.Null(rejected.ChangeId);
        Assert.Empty(rejected.Changes);
        Assert.Empty(events);
        Assert.Equal(before, await RowsAsync());
        var conflict = Assert.Single(preview.Conflicts);
        Assert.Equal(new CoursePointTimingAssociationSnapshot(destinationTiming, occupant, T0, null), conflict.Association);
        Assert.Equal(T0.AddDays(1), conflict.OverlapValidFromUtc);
        Assert.Equal(T0.AddDays(2), conflict.OverlapValidToUtc);

        var accepted = await SetAsync(request with { ConfirmationToken = preview.ConfirmationToken });
        Assert.Null(accepted.ConflictPreview);
        var message = Assert.Single(events);
        Assert.NotEqual(Guid.Empty, message.EventId);
        Assert.NotEqual(Guid.Empty, message.ChangeId);
        Assert.Equal(accepted.ChangeId, message.ChangeId);
        Assert.Equal(facilityId, message.FacilityId);
        Assert.Equal(T0.AddDays(3), message.OccurredAtUtc);
        Assert.Equal(accepted.Changes, message.Changes);
        Assert.Equal(
            [new(new(destinationTiming, occupant, T0.AddDays(1), T0.AddDays(2)), null),
             new CoursePointTimingAssociationChange(new(sourceTiming, source, T0.AddDays(1), T0.AddDays(2)),
                 new(destinationTiming, source, T0.AddDays(1), T0.AddDays(2)))],
            message.Changes);
        Assert.All(message.Changes.SelectMany(delta => new[] { delta.Before, delta.After }).OfType<CoursePointTimingAssociationSnapshot>(),
            snapshot =>
            {
                Assert.Equal(TimeSpan.Zero, snapshot.ValidFromUtc!.Value.Offset);
                Assert.Equal(TimeSpan.Zero, snapshot.ValidToUtc!.Value.Offset);
            });
        await AssertHistoryAsync(source,
            new(sourceTiming, source, null, T0.AddDays(1)),
            new(destinationTiming, source, T0.AddDays(1), T0.AddDays(2)),
            new(sourceTiming, source, T0.AddDays(2), null));
        await AssertHistoryAsync(occupant,
            new(destinationTiming, occupant, T0, T0.AddDays(1)),
            new(destinationTiming, occupant, T0.AddDays(2), null));

        var ids = await RowsAsync();
        var retry = await SetAsync(request); // Occupant is now absent only within this interval.
        Assert.Null(retry.ChangeId);
        Assert.Empty(retry.Changes);
        Assert.Null(retry.ConflictPreview);
        Assert.Single(events);
        Assert.Equal(ids, await RowsAsync());
    }

    [TimingAssociationPostgresFact]
    public async Task Unlink_SplitsAllTimeHistoryAndRetryIsNoOp()
    {
        var request = new SetCoursePointTimingAssociationRequest
        {
            TimingPointId = null, ValidFromUtc = T0, ValidToUtc = T0.AddDays(1)
        };
        var result = await SetAsync(request);
        Assert.Equal(new CoursePointTimingAssociationChange(new(sourceTiming, source, T0, T0.AddDays(1)), null),
            Assert.Single(result.Changes));
        await AssertHistoryAsync(source,
            new(sourceTiming, source, null, T0), new(sourceTiming, source, T0.AddDays(1), null));
        var ids = await RowsAsync();
        Assert.Null((await SetAsync(request)).ChangeId);
        Assert.Single(events);
        Assert.Equal(ids, await RowsAsync());
    }

    [TimingAssociationPostgresFact]
    public async Task BoundariesAndOffsetEquivalentInputs_UsePersistedMicrosecondsAndNoMappingCache()
    {
        var replacement = Guid.NewGuid();
        var request = new SetCoursePointTimingAssociationRequest
        {
            TimingPointId = replacement,
            ValidFromUtc = T0.ToOffset(TimeSpan.FromHours(2)).AddTicks(9),
            ValidToUtc = T0.AddDays(1).ToOffset(TimeSpan.FromHours(-3)).AddTicks(9)
        };
        await SetAsync(request);
        await AssertHistoryAsync(source,
            new(sourceTiming, source, null, T0),
            new(replacement, source, T0, T0.AddDays(1)),
            new(sourceTiming, source, T0.AddDays(1), null));
        await using var storage = CreateStorage(usePostgres: true);
        var service = new TimingAssociationService(storage);
        foreach (var (at, timing) in new[]
        {
            (T0.AddTicks(-10), sourceTiming), (T0, replacement),
            (T0.AddDays(1).AddTicks(-10), replacement), (T0.AddDays(1), sourceTiming)
        })
        {
            Assert.Equal(timing, await service.ResolveTimingPointIdAsync(source, at, default));
            Assert.Equal(source, (await service.FindCoursePointByTimingPointIdAsync(timing, at, default))!.Id);
            Assert.Equal(timing, (await service.ResolveManyAsync([source], at, default))[source]);
        }
        Assert.All(Assert.Single(events).Changes, delta =>
        {
            Assert.Equal(T0, delta.Before!.ValidFromUtc);
            Assert.Equal(T0.AddDays(1), delta.After!.ValidToUtc);
        });
    }

    [TimingAssociationPostgresFact]
    public async Task Preview_IncludesEveryFutureOverlapButNotTouchingIntervals()
    {
        await using (var storage = CreateStorage(usePostgres: true))
        {
            storage.RemoveRange(storage.Set<CoursePointTimingAssociation>().Where(a => a.CoursePointId == occupant));
            storage.AddRange(
                Association(occupant, destinationTiming, null, T0),
                Association(occupant, destinationTiming, T0.AddDays(2), T0.AddDays(4)),
                Association(occupant, destinationTiming, T0.AddDays(6), null));
            await storage.SaveChangesAsync();
        }
        var before = await RowsAsync();
        var result = await SetAsync(Request(null) with { ValidFromUtc = T0, ValidToUtc = T0.AddDays(7) });
        var preview = Assert.IsType<TimingAssociationConflictPreview>(result.ConflictPreview);
        Assert.Equal([(T0.AddDays(2), T0.AddDays(4)), (T0.AddDays(6), T0.AddDays(7))],
            preview.Conflicts.Select(c => (c.OverlapValidFromUtc, c.OverlapValidToUtc)).ToArray());
        Assert.Equal(before, await RowsAsync());
        Assert.Empty(events);
        var confirmed = await SetAsync(Request(preview.ConfirmationToken) with
        {
            ValidFromUtc = T0, ValidToUtc = T0.AddDays(7)
        });
        Assert.Null(confirmed.ConflictPreview);
        Assert.Equal(
            [new(new(destinationTiming, occupant, T0.AddDays(2), T0.AddDays(4)), null),
             new(new(destinationTiming, occupant, T0.AddDays(6), T0.AddDays(7)), null),
             new CoursePointTimingAssociationChange(new(sourceTiming, source, T0, T0.AddDays(7)),
                 new(destinationTiming, source, T0, T0.AddDays(7)))],
            Assert.Single(events).Changes);
        await AssertHistoryAsync(occupant,
            new(destinationTiming, occupant, null, T0),
            new(destinationTiming, occupant, T0.AddDays(7), null));
        await AssertHistoryAsync(source,
            new(sourceTiming, source, null, T0),
            new(destinationTiming, source, T0, T0.AddDays(7)),
            new(sourceTiming, source, T0.AddDays(7), null));
    }

    [TimingAssociationPostgresFact]
    public async Task Confirmation_IsStaleWhenDestinationChangesAndConcurrentRetryCannotDisplaceNewState()
    {
        var preview = await PreviewAsync();
        var request = Request(preview.ConfirmationToken) with { ValidToUtc = null };
        var results = await Task.WhenAll(SetAsync(request), SetAsync(request));
        Assert.Single(results, result => result.ChangeId.HasValue);
        var stale = Assert.Single(results, result => result.ConflictPreview is not null);
        Assert.Equal("confirmation-stale", stale.ConflictPreview!.Code);
        Assert.Empty(stale.Changes);
        Assert.Single(events);
        await AssertHistoryAsync(source,
            new(sourceTiming, source, null, T0.AddDays(1)),
            new(destinationTiming, source, T0.AddDays(1), null));
        await AssertHistoryAsync(occupant, new CoursePointTimingAssociationSnapshot(destinationTiming, occupant, T0, T0.AddDays(1)));
    }

    [TimingAssociationPostgresFact]
    public async Task Confirmation_IsStaleAfterDestinationScheduleChanges()
    {
        var preview = await PreviewAsync();
        await using (var storage = CreateStorage(usePostgres: true))
        {
            var row = await storage.Set<CoursePointTimingAssociation>().SingleAsync(a => a.CoursePointId == occupant);
            row.ValidToUtc = T0.AddDays(2);
            await storage.SaveChangesAsync();
        }
        var before = await RowsAsync();
        var result = await SetAsync(Request(preview.ConfirmationToken));
        var fresh = Assert.IsType<TimingAssociationConflictPreview>(result.ConflictPreview);
        Assert.Equal("confirmation-stale", fresh.Code);
        Assert.Equal(T0.AddDays(2), Assert.Single(fresh.Conflicts).OverlapValidToUtc);
        Assert.Null(result.ChangeId);
        Assert.Empty(events);
        Assert.Equal(before, await RowsAsync());
    }

    [TimingAssociationPostgresFact]
    public async Task EmptyAfterPrecisionNormalization_IsRejectedWithoutEventOrWrite()
    {
        var before = await RowsAsync();
        await Assert.ThrowsAsync<TimingAssociationValidationException>(() => SetAsync(new()
        {
            TimingPointId = null, ValidFromUtc = T0.AddTicks(1), ValidToUtc = T0.AddTicks(9)
        }));
        Assert.Empty(events);
        Assert.Equal(before, await RowsAsync());
    }

    [TimingAssociationPostgresFact]
    public async Task EnqueueFailure_RollsBackSplitAndRestoration()
    {
        var before = await RowsAsync();
        await using var storage = CreateStorage(usePostgres: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TimingAssociationService(storage, protection).SetIntervalAsync(source,
                new() { TimingPointId = null, ValidFromUtc = T0, ValidToUtc = T0.AddDays(1) },
                T0, new SameFacilityReferences(facilityId),
                (_, _) => throw new InvalidOperationException("Test publisher outage"), default));
        Assert.Equal(before, await RowsAsync());
        Assert.Empty(events);
    }

    [TimingAssociationPostgresFact]
    public async Task UnboundedEdits_PreserveOnlyUnaffectedHistoryAndEmitExactNullBounds()
    {
        var first = await SetAsync(new()
        {
            TimingPointId = null, ValidFromUtc = null, ValidToUtc = T0
        });
        Assert.Equal(new CoursePointTimingAssociationChange(new(sourceTiming, source, null, T0), null),
            Assert.Single(first.Changes));
        await AssertHistoryAsync(source, new CoursePointTimingAssociationSnapshot(sourceTiming, source, T0, null));
        var second = await SetAsync(new()
        {
            TimingPointId = null, ValidFromUtc = T0.AddDays(1), ValidToUtc = null
        });
        Assert.Equal(new CoursePointTimingAssociationChange(new(sourceTiming, source, T0.AddDays(1), null), null),
            Assert.Single(second.Changes));
        await AssertHistoryAsync(source,
            new CoursePointTimingAssociationSnapshot(sourceTiming, source, T0, T0.AddDays(1)));
        var third = await SetAsync(new()
        {
            TimingPointId = null, ValidFromUtc = null, ValidToUtc = null
        });
        Assert.Equal(new CoursePointTimingAssociationChange(new(sourceTiming, source, T0, T0.AddDays(1)), null),
            Assert.Single(third.Changes));
        await AssertHistoryAsync(source);
        Assert.Null((await SetAsync(new()
        {
            TimingPointId = null, ValidFromUtc = null, ValidToUtc = null
        })).ChangeId);
        Assert.Equal(3, events.Count);
    }

    private async Task AssertHistoryAsync(Guid point, params CoursePointTimingAssociationSnapshot[] expected)
    {
        await using var storage = CreateStorage(usePostgres: true);
        Assert.Equal(expected, await new TimingAssociationService(storage).GetHistoryAsync(point, default));
    }

    private async Task<(Guid Id, Guid Point, Guid Timing, DateTimeOffset? From, DateTimeOffset? To)[]> RowsAsync()
    {
        await using var storage = CreateStorage(usePostgres: true);
        return (await storage.Set<CoursePointTimingAssociation>().AsNoTracking().OrderBy(a => a.Id).ToListAsync())
            .Select(a => (a.Id, a.CoursePointId, a.TimingPointId, a.ValidFromUtc, a.ValidToUtc)).ToArray();
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
