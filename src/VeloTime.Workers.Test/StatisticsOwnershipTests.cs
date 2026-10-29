using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VeloTime.Module.Statistics;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
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
    public async Task Reconcile_MatchesWholeEntryAndExactEndBoundary(int start, int end, bool matches)
    {
        await using var database = await Database.CreateAsync();
        var owner = database.Owner(0, 10);
        database.Reader.Owners = [owner];
        var entry = database.Entry(start, end);
        database.Storage.Add(entry);
        await database.Storage.SaveChangesAsync();

        await database.Service.ReconcileAsync(database.Change(null, owner), CancellationToken.None);

        database.Storage.ChangeTracker.Clear();
        var saved = await database.Storage.Set<StatisticsEntry>().SingleAsync();
        Assert.Equal(matches ? owner.OwnerId : (Guid?)null, saved.StatsProfileUserId);
        Assert.Equal(entry.Duration, saved.Duration);
        Assert.Equal(entry.Speed, saved.Speed);
        Assert.Equal(entry.FacilityId, saved.FacilityId);
        if (matches)
        {
            var profile = await database.Storage.Set<StatsProfile>().SingleAsync();
            Assert.Equal($"Rider {owner.OwnerId:D}", profile.ProfileName);
            Assert.True(profile.IsPublic);
        }
        else Assert.Empty(await database.Storage.Set<StatsProfile>().ToListAsync());
    }

    [Fact]
    public async Task Reconcile_ReassignsAndClearsUsingCurrentStateIncludingReplay()
    {
        await using var database = await Database.CreateAsync();
        var before = database.Owner(0, null);
        var after = before with { OwnerId = Guid.NewGuid(), OwnedTo = new DateTimeOffset(Start.AddSeconds(10)) };
        var entry = database.Entry(1, 5);
        var removed = database.Entry(11, 15);
        var unrelated = database.Entry(1, 5, Guid.NewGuid());
        var profile = new StatsProfile { UserId = before.OwnerId, ProfileName = "Original", IsPublic = false };
        entry.StatsProfile = profile;
        removed.StatsProfile = profile;
        unrelated.StatsProfile = profile;
        database.Storage.AddRange(entry, removed, unrelated);
        await database.Storage.SaveChangesAsync();
        database.Reader.Owners = [after];
        var message = database.Change(before, after);

        Assert.Equal(2, await database.Service.ReconcileAsync(message, CancellationToken.None));
        Assert.Equal(0, await database.Service.ReconcileAsync(message, CancellationToken.None));
        database.Storage.ChangeTracker.Clear();
        Assert.Equal(after.OwnerId, (await database.Storage.Set<StatisticsEntry>().SingleAsync(e => e.Id == entry.Id)).StatsProfileUserId);
        Assert.Null((await database.Storage.Set<StatisticsEntry>().SingleAsync(e => e.Id == removed.Id)).StatsProfileUserId);
        Assert.Equal(before.OwnerId, (await database.Storage.Set<StatisticsEntry>().SingleAsync(e => e.Id == unrelated.Id)).StatsProfileUserId);

        var latest = after with { OwnerId = before.OwnerId };
        database.Reader.Owners = [latest];
        await database.Service.ReconcileAsync(message, CancellationToken.None);
        database.Storage.ChangeTracker.Clear();
        Assert.Equal(before.OwnerId, (await database.Storage.Set<StatisticsEntry>().SingleAsync(e => e.Id == entry.Id)).StatsProfileUserId);
        var originalProfile = await database.Storage.Set<StatsProfile>().SingleAsync(p => p.UserId == before.OwnerId);
        Assert.Equal("Original", originalProfile.ProfileName);
        Assert.False(originalProfile.IsPublic);
        Assert.Equal(2, await database.Storage.Set<StatsProfile>().CountAsync());
    }

    [Fact]
    public async Task Reconcile_BatchesAllImpactedEntriesAndDoesNotTouchGaps()
    {
        await using var database = await Database.CreateAsync();
        var first = database.Owner(0, 10);
        var second = first with { Id = Guid.NewGuid(), OwnedFrom = new DateTimeOffset(Start.AddSeconds(20)), OwnedTo = null };
        database.Reader.Owners = [first, second];
        database.Storage.AddRange(Enumerable.Range(0, 501).Select(_ => database.Entry(1, 5)));
        var gap = database.Entry(12, 15);
        database.Storage.Add(gap);
        database.Storage.Add(database.Entry(21, 100));
        await database.Storage.SaveChangesAsync();
        var message = new TransponderOwnershipChanged(Guid.NewGuid(), database.TransponderId,
            [new(null, first), new(null, second)]);

        var handler = new TransponderOwnershipChangedHandler(database.Service, NullLogger<TransponderOwnershipChangedHandler>.Instance);
        await handler.OnHandle(message, CancellationToken.None);
        database.Storage.ChangeTracker.Clear();

        Assert.Equal(502, await database.Storage.Set<StatisticsEntry>().CountAsync(e => e.StatsProfileUserId == first.OwnerId));
        Assert.Null((await database.Storage.Set<StatisticsEntry>().SingleAsync(e => e.Id == gap.Id)).StatsProfileUserId);
        Assert.Single(await database.Storage.Set<StatsProfile>().ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reconcile_DeleteOrEndClearsStaleLinks(bool end)
    {
        await using var database = await Database.CreateAsync();
        var before = database.Owner(0, null);
        var after = end ? before with { OwnedTo = new DateTimeOffset(Start.AddSeconds(5)) } : null;
        var entry = database.Entry(6, 10);
        entry.StatsProfile = new StatsProfile { UserId = before.OwnerId, ProfileName = "Existing" };
        database.Storage.Add(entry);
        await database.Storage.SaveChangesAsync();
        database.Reader.Owners = after is null ? [] : [after];

        Assert.Equal(1, await database.Service.ReconcileAsync(database.Change(before, after), CancellationToken.None));
        database.Storage.ChangeTracker.Clear();
        Assert.Null((await database.Storage.Set<StatisticsEntry>().SingleAsync()).StatsProfileUserId);
        Assert.Single(await database.Storage.Set<StatsProfile>().ToListAsync());
    }

    [Fact]
    public async Task Reconcile_ShiftMergeAndExtensionUseAllCurrentPeriods()
    {
        await using var database = await Database.CreateAsync();
        var before = database.Owner(2, 5);
        var duplicate = before with { Id = Guid.NewGuid(), OwnedFrom = new DateTimeOffset(Start.AddSeconds(4)), OwnedTo = new DateTimeOffset(Start.AddSeconds(8)) };
        var merged = before with { OwnedFrom = new DateTimeOffset(Start), OwnedTo = new DateTimeOffset(Start.AddSeconds(10)) };
        database.Reader.Owners = [merged, merged with { Id = Guid.NewGuid() }];
        database.Storage.AddRange(database.Entry(0, 2), database.Entry(3, 7), database.Entry(8, 10));
        await database.Storage.SaveChangesAsync();

        Assert.Equal(3, await database.Service.ReconcileAsync(new(Guid.NewGuid(), database.TransponderId,
            [new(before, merged), new(duplicate, null)]), CancellationToken.None));
        Assert.Single(await database.Storage.Set<StatsProfile>().ToListAsync());
    }

    [Fact]
    public async Task LookupFailureAndCancellationDoNotCreateEntriesOrClearExistingLinks()
    {
        await using var database = await Database.CreateAsync();
        var owner = database.Owner(0, null);
        var entry = database.Entry(1, 5);
        entry.StatsProfile = new StatsProfile { UserId = owner.OwnerId, ProfileName = "Existing" };
        database.Storage.Add(entry);
        await database.Storage.SaveChangesAsync();
        database.Reader.Failure = new HttpRequestException("Timing unavailable");
        await Assert.ThrowsAsync<HttpRequestException>(() => database.Service.ReconcileAsync(database.Change(null, owner), CancellationToken.None));
        var created = false;
        await Assert.ThrowsAsync<HttpRequestException>(() => database.Service.CreateEntriesAsync(database.TransponderId, _ =>
        {
            created = true;
            return Task.FromResult<IEnumerable<StatisticsEntry>>([]);
        }, CancellationToken.None));
        Assert.False(created);
        database.Reader.Failure = null;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.Service.ReconcileAsync(database.Change(null, owner), cancellation.Token));
        database.Storage.ChangeTracker.Clear();
        Assert.Equal(owner.OwnerId, (await database.Storage.Set<StatisticsEntry>().SingleAsync()).StatsProfileUserId);
    }

    [Fact]
    public async Task ConflictingOwnersRollBackAllProfileChanges()
    {
        await using var database = await Database.CreateAsync();
        var owner = database.Owner(0, null);
        database.Reader.Owners = [owner, owner with { Id = Guid.NewGuid(), OwnerId = Guid.NewGuid() }];
        database.Storage.Add(database.Entry(1, 5));
        await database.Storage.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Service.ReconcileAsync(database.Change(null, owner), CancellationToken.None));
        database.Storage.ChangeTracker.Clear();
        Assert.Null((await database.Storage.Set<StatisticsEntry>().SingleAsync()).StatsProfileUserId);
        Assert.Empty(await database.Storage.Set<StatsProfile>().ToListAsync());
    }

    [Fact]
    public async Task AssignmentFailureRollsBackEntriesAlreadySavedByCreation()
    {
        await using var database = await Database.CreateAsync();
        var owner = database.Owner(0, null);
        database.Reader.Owners = [owner, owner with { Id = Guid.NewGuid(), OwnerId = Guid.NewGuid() }];

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Service.CreateEntriesAsync(database.TransponderId, async cancel =>
        {
            var entry = database.Entry(1, 5);
            database.Storage.Add(entry);
            await database.Storage.SaveChangesAsync(cancel);
            return new[] { entry };
        }, CancellationToken.None));

        database.Storage.ChangeTracker.Clear();
        Assert.Empty(await database.Storage.Set<StatisticsEntry>().ToListAsync());
        Assert.Empty(await database.Storage.Set<StatsProfile>().ToListAsync());
    }

    [Fact]
    public async Task CreationHandlersAssignSimpleAndMultiProfilesAfterEarlierOwnershipEvent()
    {
        await using var database = await Database.CreateAsync();
        var owner = database.Owner(-100, null);
        database.Reader.Owners = [owner];
        Assert.Equal(0, await database.Service.ReconcileAsync(database.Change(null, owner), CancellationToken.None));
        var startPoint = Guid.NewGuid();
        var endPoint = Guid.NewGuid();
        database.Config.CoursePointStart = startPoint;
        database.Config.CoursePointEnd = endPoint;
        var multi = new MultiStatisticsItemConfig
        {
            Id = Guid.NewGuid(), ParentConfigId = database.Config.Id, ParentConfig = database.Config, Repetitions = 1,
            StatisticsItem = new StatisticsItem { Id = Guid.NewGuid(), Name = "Aggregate", Distance = 100 }
        };
        database.Storage.AddRange(database.Config, multi, new Sample
        {
            Id = Guid.NewGuid(), TransponderId = database.TransponderId,
            TimeStart = Start, TimeEnd = Start.AddSeconds(1), CoursePointStartId = startPoint,
            CoursePointEndId = Guid.NewGuid(), FacilityId = Guid.NewGuid()
        });
        await database.Storage.SaveChangesAsync();
        var serviceCollection = new ServiceCollection().AddMetrics();
        serviceCollection.AddHybridCache();
        var services = serviceCollection.BuildServiceProvider();
        using (services)
        {
            var statistics = new StatisticsService(database.Storage, services.GetRequiredService<HybridCache>());
            var (bus, recorder) = WorkerHandlerTests.RecordingMessageBus.Create();
            var sampleHandler = new SampleCompleteHandler(statistics, database.Service,
                new Metrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()), bus, NullLogger<SampleCompleteHandler>.Instance);
            await sampleHandler.OnHandle(new SampleComplete(database.TransponderId, Start.AddSeconds(2), Start.AddSeconds(10),
                startPoint, endPoint, 100), CancellationToken.None);
            var emitted = Assert.Single((IEnumerable<EntryCreated>)Assert.Single(recorder.Published).Payload!);
            var entryHandler = new EntryCreatedHandler(statistics, database.Service, bus, NullLogger<EntryCreatedHandler>.Instance);
            await entryHandler.OnHandle(emitted, CancellationToken.None);
            Assert.Equal(2, recorder.Published.Count);
            var aggregate = Assert.Single((IEnumerable<EntryCreated>)recorder.Published[1].Payload!);
            Assert.Equal(multi.Id, aggregate.ConfigItemId);
        }
        database.Storage.ChangeTracker.Clear();
        var entries = await database.Storage.Set<StatisticsEntry>().ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.Equal(owner.OwnerId, entry.StatsProfileUserId);
            Assert.Equal(TimeSpan.FromSeconds(10), entry.Duration);
            Assert.Equal(10, entry.Speed);
        });
        Assert.Single(await database.Storage.Set<StatsProfile>().ToListAsync());
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
