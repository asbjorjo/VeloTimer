using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VeloTime.Module.Statistics;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Service.Recompute;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Workers.Test;

public sealed class StatisticsOwnershipTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, 10, true)]
    [InlineData(-1, 10, false)]
    [InlineData(9, 10, true)]
    [InlineData(9, 11, false)]
    [InlineData(10, 10, false)]
    [InlineData(11, 12, false)]
    public async Task ApplyOwners_MatchesWholeEntryAndExactEndBoundary(int start, int end, bool matches)
    {
        await using var database = await Database.CreateAsync();
        var owner = database.Owner(0, 10);
        database.Reader.Owners = [owner];
        var entry = database.Entry(start, end);
        database.Storage.Add(entry);

        await database.Service.ApplyOwnersAsync(database.TransponderId, [entry], CancellationToken.None);
        await database.Storage.SaveChangesAsync();

        database.Storage.ChangeTracker.Clear();
        var saved = await database.Storage.Set<StatisticsEntry>().SingleAsync();
        Assert.Equal(matches ? owner.OwnerId : (Guid?)null, saved.StatsProfileUserId);
        if (matches)
        {
            var profile = await database.Storage.Set<StatsProfile>().SingleAsync();
            Assert.Equal($"Rider {owner.OwnerId:D}", profile.ProfileName);
            Assert.True(profile.IsPublic);
        }
        else Assert.Empty(await database.Storage.Set<StatsProfile>().ToListAsync());
    }

    [Fact]
    public async Task OwnershipChange_EvictsHistoryThenMarksEveryChangedPeriodDirty()
    {
        await using var database = await Database.CreateAsync();
        var before = database.Owner(0, null);
        var after = before with { OwnerId = Guid.NewGuid(), OwnedTo = new DateTimeOffset(Start.AddSeconds(10)) };
        var later = database.Owner(20, null);
        var cache = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider().GetRequiredService<HybridCache>();
        var history = new StatisticsConfigurationHistory(cache, new NoConfiguration());
        // A cached ownership history from before the change must not survive it.
        await history.GetOwnershipAsync(database.TransponderId, (_, _) => Task.FromResult(new List<TransponderOwnershipDTO> { before }),
            CancellationToken.None);

        var handler = new TransponderOwnershipChangedHandler(database.Storage, history, new StatisticsRecomputeSignal(),
            NullLogger<TransponderOwnershipChangedHandler>.Instance);
        await handler.OnHandle(new(Guid.NewGuid(), database.TransponderId, [new(before, after), new(null, later)]), CancellationToken.None);

        var requests = await database.Storage.Set<RecomputeRequest>().OrderBy(r => r.FromUtc).ToListAsync();
        Assert.All(requests, r => Assert.Equal(database.TransponderId, r.TransponderId));
        Assert.Equal([(Start, (DateTime?)null), (Start, Start.AddSeconds(10)), (Start.AddSeconds(20), null)],
            requests.Select(r => (r.FromUtc!.Value, r.ToUtc)).Order().ToList());
        var reread = await history.GetOwnershipAsync(database.TransponderId,
            (_, _) => Task.FromResult(new List<TransponderOwnershipDTO> { after }), CancellationToken.None);
        Assert.Equal([after], reread);
    }

    [Fact]
    public async Task LookupFailureAndCancellationAssignNoOwner()
    {
        await using var database = await Database.CreateAsync();
        var derived = database.Entry(6, 8);
        database.Reader.Failure = new HttpRequestException("Timing unavailable");
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            database.Service.ApplyOwnersAsync(database.TransponderId, [derived], CancellationToken.None));
        Assert.Null(derived.StatsProfileUserId);
        database.Reader.Failure = null;
        database.Reader.Owners = [database.Owner(0, null)];
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.Service.ApplyOwnersAsync(database.TransponderId, [derived], cancellation.Token));
        Assert.Null(derived.StatsProfileUserId);
    }

    [Fact]
    public async Task ConflictingOwnersFailRecomputeOwnershipBeforeAnythingIsSaved()
    {
        await using var database = await Database.CreateAsync();
        var owner = database.Owner(0, null);
        database.Reader.Owners = [owner, owner with { Id = Guid.NewGuid(), OwnerId = Guid.NewGuid() }];
        var entry = database.Entry(1, 5);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Service.ApplyOwnersAsync(database.TransponderId, [entry], CancellationToken.None));

        database.Storage.ChangeTracker.Clear();
        Assert.Empty(await database.Storage.Set<StatisticsEntry>().ToListAsync());
        Assert.Empty(await database.Storage.Set<StatsProfile>().ToListAsync());
    }

    [Fact]
    public async Task RecomputeOwnershipAssignsEveryDerivedEntry()
    {
        await using var database = await Database.CreateAsync();
        var owner = database.Owner(-100, null);
        database.Reader.Owners = [owner];
        var entries = new[] { database.Entry(1, 5), database.Entry(6, 10) };
        database.Storage.AddRange(entries);

        await database.Service.ApplyOwnersAsync(database.TransponderId, entries, CancellationToken.None);
        await database.Storage.SaveChangesAsync();

        database.Storage.ChangeTracker.Clear();
        var stored = await database.Storage.Set<StatisticsEntry>().ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.All(stored, e => Assert.Equal(owner.OwnerId, e.StatsProfileUserId));
        Assert.Single(await database.Storage.Set<StatsProfile>().ToListAsync());
    }

    private sealed class NoConfiguration : IStatisticsConfigurationSource
    {
        public Task<InstallationFacilityHistory> GetInstallationAsync(Guid installationId, CancellationToken cancellationToken) =>
            Task.FromResult(new InstallationFacilityHistory { InstallationId = installationId });
        public Task<FacilityCourseHistory> GetFacilityCourseAsync(Guid facilityId, CancellationToken cancellationToken) =>
            Task.FromResult(new FacilityCourseHistory { FacilityId = facilityId });
    }

    internal sealed class Reader : ITransponderOwnershipReader
    {
        public IReadOnlyList<TransponderOwnershipDTO> Owners { get; set; } = [];
        public Exception? Failure { get; set; }
        public Task<IReadOnlyList<TransponderOwnershipDTO>> ReadAsync(Guid transponderId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure is null ? Task.FromResult(Owners) : Task.FromException<IReadOnlyList<TransponderOwnershipDTO>>(Failure);
        }
    }

    private sealed class Database(SqliteConnection connection, StatisticsDbContext storage) : IAsyncDisposable
    {
        public Guid TransponderId { get; } = Guid.NewGuid();
        public Reader Reader { get; } = new();
        public StatisticsDbContext Storage { get; } = storage;
        public StatisticsOwnershipService Service => new(Storage, Reader);
        public SimpleStatisticsItemConfig Config { get; } = new()
        {
            Id = Guid.NewGuid(), StatisticsItem = new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap", Distance = 100 }
        };

        public TransponderOwnershipDTO Owner(int start, int? end) => new(Guid.NewGuid(), TransponderId, Guid.NewGuid(),
            new DateTimeOffset(Start.AddSeconds(start)), end.HasValue ? new DateTimeOffset(Start.AddSeconds(end.Value)) : null);

        public TransponderOwnershipChanged Change(TransponderOwnershipDTO? before, TransponderOwnershipDTO? after) =>
            new(Guid.NewGuid(), TransponderId, [new(before, after)]);

        public StatisticsEntry Entry(int start, int end, Guid? transponderId = null) => new()
        {
            Id = Guid.NewGuid(), TransponderId = transponderId ?? TransponderId, StatisticsItemConfig = Config,
            StatisticsItemConfigId = Config.Id, StatisticsItem = Config.StatisticsItem, StatisticsItemId = Config.StatisticsItem.Id,
            TimeStart = Start.AddSeconds(start), TimeEnd = Start.AddSeconds(end), Duration = TimeSpan.FromSeconds(end - start),
            Speed = end == start ? 0 : 100d / (end - start), FacilityId = Guid.NewGuid()
        };

        public static async Task<Database> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var storage = new StatisticsDbContext(new DbContextOptionsBuilder<StatisticsDbContext>().UseSqlite(connection).Options);
            await storage.Database.EnsureCreatedAsync();
            return new Database(connection, storage);
        }

        public async ValueTask DisposeAsync()
        {
            await Storage.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
