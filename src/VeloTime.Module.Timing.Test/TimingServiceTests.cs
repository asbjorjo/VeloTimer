using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.Metrics;
using System.Reflection;
using SlimMessageBus;
using VeloTime.Module.Timing.Interface.Messages;
using System.Text.Json;
using System.Text.Json.Serialization;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Timing.Storage;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace VeloTime.Module.Timing.Test;

public sealed class TimingServiceTests
{
    [Fact]
    public async Task InstallationService_CreatesAndLooksUpInstallationsAndTimingPoints()
    {
        await using var database = await TestDatabase.CreateAsync();

        var installation = await database.InstallationService.CreateInstallationForAgent("agent-1", TimingSystem.MyLaps_X2);
        var repeatedInstallation = await database.InstallationService.CreateInstallationForAgent("agent-1", TimingSystem.Unknown);

        Assert.Equal(installation.Id, repeatedInstallation.Id);
        Assert.Equal(installation.Id, (await database.InstallationService.GetInstallationForAgent("agent-1"))?.Id);
        Assert.Single(await database.InstallationService.GetAllInstallations());

        var timingPoint = await database.InstallationService.CreateTimingPoint(installation, "loop-1");
        var repeatedTimingPoint = await database.InstallationService.CreateTimingPoint(installation, "loop-1");

        Assert.Equal(timingPoint.Id, repeatedTimingPoint.Id);
        Assert.Equal(timingPoint.Id, (await database.InstallationService.GetTimingPoint(installation, "loop-1"))?.Id);
        Assert.Equal(installation.Id, (await database.InstallationService.GetInstallionForTimingPoint(timingPoint.Id)).Id);
    }

    [Fact]
    public async Task InstallationService_RegistersTransponderAndFindsLatestPassing()
    {
        await using var database = await TestDatabase.CreateAsync();
        var installation = new Installation { Id = Guid.NewGuid(), AgentId = "agent-2", TimingSystem = TimingSystem.MyLaps_X2 };
        var timingPoint = new TimingPoint { Id = Guid.NewGuid(), SystemId = "loop-2", Installation = installation, Description = "Finish" };
        var transponder = new MylapsX2Transponder { Id = Guid.NewGuid(), SystemId = "106187718" };
        Assert.Equal("106187718", database.Storage.Entry(transponder).Property(nameof(Transponder.SystemId)).CurrentValue);
        installation.TimingPoints.Add(timingPoint);
        database.Storage.Add(installation);
        database.Storage.Add(transponder);
        await database.Storage.SaveChangesAsync();

        Assert.Null(await database.InstallationService.GetTransponder("missing", TimingSystem.MyLaps_X2));
        var registered = await database.InstallationService.RegisterTransponder(new MylapsX2Transponder { SystemId = "106187719" });
        var repeated = await database.InstallationService.RegisterTransponder(new MylapsX2Transponder { SystemId = "106187719" });
        Assert.Equal(registered.Id, repeated.Id);

        var passing = new Passing
        {
            Id = Guid.NewGuid(),
            Time = DateTime.UtcNow,
            Transponder = transponder,
            TimingPoint = timingPoint, Installation = timingPoint.Installation, SourcePassingId = Guid.NewGuid().ToString(),
            TransponderId = transponder.Id,
            TimingPointId = timingPoint.Id
        };
        database.Storage.Add(passing);
        await database.Storage.SaveChangesAsync();

        Assert.Equal(passing.Id, (await database.InstallationService.GetLastPassingForTransponder(transponder, installation))?.Id);
    }

    [Fact]
    public async Task InstallationService_UpdatesLastSeenOnlyForNewerTimestamps()
    {
        await using var database = await TestDatabase.CreateAsync();
        var installation = await database.InstallationService.CreateInstallationForAgent("agent-3");
        var firstTimestamp = DateTime.UtcNow.AddMinutes(-1);
        var newerTimestamp = DateTime.UtcNow;

        await database.InstallationService.UpdateLastSeenAsync(installation, newerTimestamp);
        await database.InstallationService.UpdateLastSeenAsync(installation, firstTimestamp);

        var persisted = await database.Storage.Set<Installation>().SingleAsync(item => item.Id == installation.Id);
        Assert.Equal(newerTimestamp, persisted.LastSeen);
    }

    [Fact]
    public async Task TransponderQuery_SortsByObservationWithMissingLastAndPrioritizesCurrentPersonalOwnership()
    {
        await using var database = await TestDatabase.CreateAsync();
        var ownerId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var activeRecent = CreateTransponder("Active recent");
        var activeOld = CreateTransponder("Active old");
        var activeUnseen = CreateTransponder("Active unseen");
        var endedRecent = CreateTransponder("Ended recent");
        var endedUnseen = CreateTransponder("Ended unseen");
        var transponders = new[] { activeRecent, activeOld, activeUnseen, endedRecent, endedUnseen };

        foreach (var transponder in transponders)
        {
            var ended = transponder == endedRecent || transponder == endedUnseen;
            transponder.Owners.Add(new TransponderOwner
            {
                Id = Guid.NewGuid(),
                TransponderId = transponder.Id,
                OwnerId = ownerId,
                Transponder = transponder,
                OwnedFrom = now.AddDays(-30),
                OwnedTo = ended ? now.AddDays(-1) : null
            });
        }

        var installation = new Installation { Id = Guid.NewGuid(), AgentId = "sort-test", TimingSystem = TimingSystem.MyLaps_X2 };
        var timingPoint = new TimingPoint
        {
            Id = Guid.NewGuid(),
            SystemId = "sort-test",
            Description = "Sort test",
            Installation = installation,
            InstallationId = installation.Id
        };
        database.Storage.AddRange(installation, timingPoint);
        database.Storage.Set<Transponder>().AddRange(transponders);
        AddPassing(activeRecent, now.AddMinutes(-5));
        AddPassing(activeOld, now.AddDays(-10));
        AddPassing(endedRecent, now.AddMinutes(-1));
        await database.Storage.SaveChangesAsync();

        var adminPage = await TransponderQuery.ListAsync(database.Storage,
            new TransponderPageRequest(0, 20, "", true, SortBy: "lastSeen", SortDescending: true),
            null, CancellationToken.None, statusAdministration: true);
        var personalPage = await TransponderQuery.ListAsync(database.Storage,
            new TransponderPageRequest(0, 20, "", true, SortBy: "ownedLastSeen", SortDescending: true),
            ownerId, CancellationToken.None);

        Assert.Equal(new[] { "Ended recent", "Active recent", "Active old", "Active unseen", "Ended unseen" },
            adminPage.Items.Select(row => row.Transponder.Label));
        Assert.Equal(new[] { "Active recent", "Active old", "Active unseen", "Ended recent", "Ended unseen" },
            personalPage.Items.Select(row => row.Transponder.Label));

        void AddPassing(Transponder transponder, DateTime time) => database.Storage.Add(new Passing
        {
            Id = Guid.NewGuid(),
            Time = time,
            TransponderId = transponder.Id,
            Transponder = transponder,
            TimingPointId = timingPoint.Id,
            TimingPoint = timingPoint, Installation = timingPoint.Installation, SourcePassingId = Guid.NewGuid().ToString()
        });

        static MylapsX2Transponder CreateTransponder(string label) => new()
        {
            Id = Guid.NewGuid(),
            SystemId = Guid.NewGuid().ToString("N"),
            Label = label
        };
    }

    [Fact]
    public async Task TransponderService_ListsFindsAndUpdatesTransponders()
    {
        await using var database = await TestDatabase.CreateAsync();
        var transponder = new MylapsX2Transponder { Id = Guid.NewGuid(), SystemId = "106187720" };
        Assert.Equal("106187720", database.Storage.Entry(transponder).Property(nameof(Transponder.SystemId)).CurrentValue);
        database.Storage.Add(transponder);
        await database.Storage.SaveChangesAsync();
        database.Storage.ChangeTracker.Clear();

        Assert.Contains(await database.TransponderService.ListAllTransponders(), item => item.Id == transponder.Id);
        Assert.Equal(transponder.Id, (await database.TransponderService.GetTransponderById(transponder.Id))?.Id);

        var updated = await database.TransponderService.GetTransponderById(transponder.Id);
        Assert.NotNull(updated);
        updated.AddOwner(Guid.NewGuid(), DateTime.UtcNow);
        await database.TransponderService.Update(updated);

        Assert.Single((await database.TransponderService.GetTransponderById(transponder.Id))!.Owners);
        Assert.Null(await database.TransponderService.GetTransponderById(Guid.NewGuid()));
    }

    [Fact]
    public async Task AgentService_RecordsPassingAndEnqueuesOneRecordedEventInItsTransaction()
    {
        await using var database = await TestDatabase.CreateAsync();
        var timestamp = new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234567);

        var first = await database.AgentService.RegisterPassingAsync(Observation("agent-4", "p-1", "106187721", "loop-4", timestamp, lowBattery: true));

        Assert.Equal(PassingRegistrationOutcome.Recorded, first.Outcome);
        Assert.Equal(timestamp.AddTicks(-7), first.Passing.Time); // Stored at timestamptz (1 µs) precision.
        var stored = await database.Storage.Set<Passing>().SingleAsync();
        Assert.Equal("p-1", stored.SourcePassingId);
        Assert.Equal((await database.Storage.Set<Installation>().SingleAsync()).Id, stored.InstallationId);
        var recorded = Assert.IsType<PassingRecordedV1>(Assert.Single(database.Bus.Published));
        Assert.Equal(new PassingRecordedV1(stored.Id, stored.InstallationId, stored.TimingPointId, stored.TransponderId, stored.Time,
            true, false, false), recorded);
        Assert.True(database.Bus.PublishedInTransaction.Single());
    }

    [Fact]
    public async Task AgentService_RedeliveredObservationIsADuplicateAndPublishesNothing()
    {
        await using var database = await TestDatabase.CreateAsync();
        var observation = Observation("agent-4", "p-1", "106187721", "loop-4", DateTime.UtcNow, lowBattery: true, lowStrength: true, lowHits: true);

        var first = await database.AgentService.RegisterPassingAsync(observation);
        var duplicate = await database.AgentService.RegisterPassingAsync(observation);

        Assert.Equal(PassingRegistrationOutcome.Duplicate, duplicate.Outcome);
        Assert.Equal(first.Passing.Id, duplicate.Passing.Id);
        Assert.True(duplicate.Passing.LowBattery && duplicate.Passing.LowStrength && duplicate.Passing.LowHits);
        Assert.Single(await database.Storage.Set<Passing>().ToListAsync());
        Assert.Single(database.Bus.Published);
        Assert.Empty(await database.Storage.Set<PassingConflict>().ToListAsync());
    }

    [Fact]
    public async Task AgentService_SameSourceIdWithDifferentPayloadIsRejectedAndFlagged()
    {
        await using var database = await TestDatabase.CreateAsync();
        var time = DateTime.UtcNow;
        var first = await database.AgentService.RegisterPassingAsync(Observation("agent-c", "p-1", "106187721", "loop-1", time));

        var changed = Observation("agent-c", "p-1", "106187721", "loop-1", time.AddSeconds(1));
        var conflict = await database.AgentService.RegisterPassingAsync(changed);
        var repeated = await database.AgentService.RegisterPassingAsync(changed);

        Assert.Equal(PassingRegistrationOutcome.Conflict, conflict.Outcome);
        Assert.Equal(PassingRegistrationOutcome.Conflict, repeated.Outcome);
        Assert.Equal(first.Passing.Id, conflict.Passing.Id);
        var stored = await database.Storage.Set<Passing>().SingleAsync();
        Assert.Equal(first.Passing.Time, stored.Time); // The recorded fact is never rewritten.
        var flagged = await database.Storage.Set<PassingConflict>().SingleAsync();
        Assert.Equal(PassingConflictReason.SourcePassingIdPayloadMismatch, flagged.Reason);
        Assert.Equal(first.Passing.Id, flagged.ExistingPassingId);
        Assert.Equal(PassingTime.Normalize(changed.Time), flagged.Time);
        Assert.Equal(2, flagged.Occurrences);
        Assert.Single(database.Bus.Published);
    }

    [Fact]
    public async Task AgentService_DifferentSourceIdForTheSameObservationIsFlagged()
    {
        await using var database = await TestDatabase.CreateAsync();
        var time = DateTime.UtcNow;
        var first = await database.AgentService.RegisterPassingAsync(Observation("agent-d", "p-1", "106187721", "loop-1", time));

        var other = await database.AgentService.RegisterPassingAsync(Observation("agent-d", "p-2", "106187721", "loop-1", time));

        Assert.Equal(PassingRegistrationOutcome.Conflict, other.Outcome);
        Assert.Equal(PassingConflictReason.DuplicateObservation, other.Conflict!.Reason);
        Assert.Equal(first.Passing.Id, other.Conflict.ExistingPassingId);
        Assert.Single(await database.Storage.Set<Passing>().ToListAsync());
        Assert.Single(database.Bus.Published);
    }

    [Fact]
    public async Task AgentService_SourceIdsAreScopedToTheirInstallation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var time = DateTime.UtcNow;

        var a = await database.AgentService.RegisterPassingAsync(Observation("agent-a", "p-1", "106187721", "loop-1", time));
        var b = await database.AgentService.RegisterPassingAsync(Observation("agent-b", "p-1", "106187721", "loop-1", time));

        Assert.Equal(PassingRegistrationOutcome.Recorded, a.Outcome);
        Assert.Equal(PassingRegistrationOutcome.Recorded, b.Outcome);
        Assert.NotEqual(a.Passing.InstallationId, b.Passing.InstallationId);
        Assert.Equal(2, database.Bus.Published.Count);
    }

    [Fact]
    public async Task AgentService_LatePassingIsRecordedLikeAnyOther()
    {
        await using var database = await TestDatabase.CreateAsync();
        var start = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

        // P1, P3, then the late P2: previously the late passing could never be processed.
        foreach (var (id, seconds) in new[] { ("p-1", 0), ("p-3", 20), ("p-2", 10) })
            Assert.Equal(PassingRegistrationOutcome.Recorded,
                (await database.AgentService.RegisterPassingAsync(Observation("late-agent", id, "106187724", "finish", start.AddSeconds(seconds)))).Outcome);

        Assert.Equal(["p-1", "p-2", "p-3"], await database.Storage.Set<Passing>().OrderBy(p => p.Time).Select(p => p.SourcePassingId).ToListAsync());
        Assert.Equal(3, database.Bus.Published.Count);
    }

    [Fact]
    public async Task AgentService_FailedEnqueueRollsBackThePassing()
    {
        await using var database = await TestDatabase.CreateAsync();
        database.Bus.Failure = new InvalidOperationException("outbox unavailable");
        var observation = Observation("agent-r", "p-1", "106187721", "loop-1", DateTime.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.AgentService.RegisterPassingAsync(observation));
        Assert.Empty(await database.Storage.Set<Passing>().ToListAsync());

        database.Bus.Failure = null;
        Assert.Equal(PassingRegistrationOutcome.Recorded, (await database.AgentService.RegisterPassingAsync(observation)).Outcome);
        Assert.Single(await database.Storage.Set<Passing>().ToListAsync());
    }

    [Fact]
    public async Task AgentService_RejectsObservationsWithoutSourcePassingId()
    {
        await using var database = await TestDatabase.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            database.AgentService.RegisterPassingAsync(Observation("agent-x", " ", "106187721", "loop-1", DateTime.UtcNow)));
    }

    private static PassingObservation Observation(string agentId, string sourcePassingId, string transponderId, string loopId,
        DateTime time, bool lowBattery = false, bool lowStrength = false, bool lowHits = false) =>
        new(agentId, sourcePassingId, transponderId, "X2", loopId, time, lowBattery, lowStrength, lowHits);

    public class RecordingBus : DispatchProxy
    {
        public List<object?> Published { get; } = [];
        public List<bool> PublishedInTransaction { get; } = [];
        public Exception? Failure { get; set; }
        public TimingDbContext? Storage { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != "Publish") throw new NotSupportedException(targetMethod?.Name);
            if (Failure is not null) return Task.FromException(Failure);
            Published.Add(args?.FirstOrDefault());
            PublishedInTransaction.Add(Storage?.Database.CurrentTransaction is not null);
            return Task.CompletedTask;
        }
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider services;

        private TestDatabase(SqliteConnection connection, TimingDbContext storage, ServiceProvider services)
        {
            this.connection = connection;
            this.services = services;
            Storage = storage;
            var cache = services.GetRequiredService<HybridCache>();
            InstallationService = new InstallationService(storage, cache);
            TransponderService = new TransponderService(storage, cache);
            var bus = DispatchProxy.Create<IMessageBus, RecordingBus>();
            Bus = (RecordingBus)(object)bus;
            Bus.Storage = storage;
            AgentService = new AgentService(InstallationService, storage, bus, new Metrics(services.GetRequiredService<IMeterFactory>()));
        }

        public TimingDbContext Storage { get; }
        public InstallationService InstallationService { get; }
        public TransponderService TransponderService { get; }
        public AgentService AgentService { get; }
        public RecordingBus Bus { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<TimingDbContext>().UseSqlite(connection).Options;
            var storage = new TimingDbContext(options);
            await storage.Database.EnsureCreatedAsync();
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddMetrics();
            serviceCollection.AddFusionCache()
                .WithSerializer(new FusionCacheSystemTextJsonSerializer(new JsonSerializerOptions
                {
                    ReferenceHandler = ReferenceHandler.Preserve
                }))
                .AsHybridCache();
            var services = serviceCollection.BuildServiceProvider();
            return new TestDatabase(connection, storage, services);
        }

        public async ValueTask DisposeAsync()
        {
            await Storage.DisposeAsync();
            await services.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
