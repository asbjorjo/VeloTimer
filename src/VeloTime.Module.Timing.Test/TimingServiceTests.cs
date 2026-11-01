using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.Metrics;
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
            TimingPoint = timingPoint,
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
            TimingPoint = timingPoint
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
    public async Task AgentService_RegisterPassingIsIdempotent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var timestamp = DateTime.UtcNow;

        var first = await database.AgentService.RegisterPassingAsync("agent-4", "106187721", "loop-4", timestamp, "X2", true, true, true);
        var duplicate = await database.AgentService.RegisterPassingAsync("agent-4", "106187721", "loop-4", timestamp, "X2");

        Assert.Equal(first.Id, duplicate.Id);
        Assert.True(duplicate.LowBattery && duplicate.LowStrength && duplicate.LowHits);
        Assert.Single(await database.Storage.Set<Passing>().ToListAsync());
    }

    [Fact]
    public async Task AgentService_PreviousPassingIsScopedToInstallation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var transponder = new MylapsX2Transponder { Id = Guid.NewGuid(), SystemId = "106187723" };
        var firstInstallation = new Installation
        {
            Id = Guid.NewGuid(), AgentId = "passing-installation-a", TimingSystem = TimingSystem.MyLaps_X2
        };
        var secondInstallation = new Installation
        {
            Id = Guid.NewGuid(), AgentId = "passing-installation-b", TimingSystem = TimingSystem.MyLaps_X2
        };
        var firstPoint = new TimingPoint
        {
            Id = Guid.NewGuid(), SystemId = "loop-a", Installation = firstInstallation, Description = "A"
        };
        var secondPoint = new TimingPoint
        {
            Id = Guid.NewGuid(), SystemId = "loop-b", Installation = secondInstallation, Description = "B"
        };
        database.Storage.AddRange(firstInstallation, secondInstallation, firstPoint, secondPoint, transponder);
        database.Storage.Add(new Passing
        {
            Id = Guid.NewGuid(), Time = DateTime.UtcNow.AddMinutes(-2),
            TransponderId = transponder.Id, Transponder = transponder,
            TimingPointId = firstPoint.Id, TimingPoint = firstPoint
        });
        await database.Storage.SaveChangesAsync();
        database.Storage.ChangeTracker.Clear();

        var current = await database.AgentService.RegisterPassingAsync(
            secondInstallation.AgentId, transponder.SystemId, secondPoint.SystemId,
            DateTime.UtcNow, "X2");

        Assert.Null(await database.AgentService.RegisterSampleAsync(current));
        Assert.Empty(await database.Storage.Set<Sample>().ToListAsync());
    }

    [Fact]
    public async Task AgentService_OutOfOrderPassingsUseStrictlyEarlierPriorAndRemainIdempotent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var installation = new Installation
        {
            Id = Guid.NewGuid(), AgentId = "out-of-order-agent", TimingSystem = TimingSystem.MyLaps_X2
        };
        var point = new TimingPoint { Id = Guid.NewGuid(), SystemId = "finish", Description = "Finish", Installation = installation };
        var transponder = new MylapsX2Transponder { Id = Guid.NewGuid(), SystemId = "106187724" };
        var startTime = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
        var earlier = new Passing
        {
            Id = Guid.NewGuid(), Time = startTime, Transponder = transponder, TransponderId = transponder.Id,
            TimingPoint = point, TimingPointId = point.Id
        };
        var middle = new Passing
        {
            Id = Guid.NewGuid(), Time = startTime.AddSeconds(10), Transponder = transponder, TransponderId = transponder.Id,
            TimingPoint = point, TimingPointId = point.Id
        };
        var latest = new Passing
        {
            Id = Guid.NewGuid(), Time = startTime.AddSeconds(20), Transponder = transponder, TransponderId = transponder.Id,
            TimingPoint = point, TimingPointId = point.Id
        };
        database.Storage.AddRange(installation, point, transponder, earlier, middle, latest);
        await database.Storage.SaveChangesAsync();
        database.Storage.ChangeTracker.Clear();

        var latestSample = await database.AgentService.RegisterSampleAsync(latest);
        Assert.NotNull(latestSample);
        Assert.Equal(middle.Id, latestSample.StartId);
        Assert.Equal(latest.Id, latestSample.EndId);
        database.Storage.ChangeTracker.Clear();

        var middleSample = await database.AgentService.RegisterSampleAsync(middle);
        var repeatedMiddleSample = await database.AgentService.RegisterSampleAsync(middle);

        Assert.NotNull(middleSample);
        Assert.Equal(earlier.Id, middleSample.StartId);
        Assert.Equal(middle.Id, middleSample.EndId);
        Assert.Equal(middleSample.Id, repeatedMiddleSample?.Id);
        Assert.Equal(2, await database.Storage.Set<Sample>().CountAsync());
    }

    [Fact]
    public async Task AgentService_RegisterSampleCreatesOneSampleForConsecutivePassings()
    {
        await using var database = await TestDatabase.CreateAsync();
        var installation = new Installation { Id = Guid.NewGuid(), AgentId = "agent-5", TimingSystem = TimingSystem.MyLaps_X2 };
        var timingPoint = new TimingPoint { Id = Guid.NewGuid(), SystemId = "loop-5", Installation = installation, Description = "Loop" };
        var transponder = new MylapsX2Transponder { Id = Guid.NewGuid(), SystemId = "106187722" };
        Assert.Equal("106187722", database.Storage.Entry(transponder).Property(nameof(Transponder.SystemId)).CurrentValue);
        installation.TimingPoints.Add(timingPoint);
        var first = new Passing
        {
            Id = Guid.NewGuid(), Time = DateTime.UtcNow.AddSeconds(-10), Transponder = transponder,
            TimingPoint = timingPoint, TransponderId = transponder.Id, TimingPointId = timingPoint.Id
        };
        var second = new Passing
        {
            Id = Guid.NewGuid(), Time = DateTime.UtcNow, Transponder = transponder,
            TimingPoint = timingPoint, TransponderId = transponder.Id, TimingPointId = timingPoint.Id
        };
        database.Storage.AddRange(installation, transponder, first, second);
        await database.Storage.SaveChangesAsync();
        database.Storage.ChangeTracker.Clear();

        Assert.Null(await database.AgentService.RegisterSampleAsync(first));
        var sample = await database.AgentService.RegisterSampleAsync(second);
        var repeated = await database.AgentService.RegisterSampleAsync(second);

        Assert.NotNull(sample);
        Assert.Equal(first.Id, sample.StartId);
        Assert.Equal(second.Id, sample.EndId);
        Assert.Equal(sample.Id, repeated?.Id);
        Assert.Single(await database.Storage.Set<Sample>().ToListAsync());
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
            AgentService = new AgentService(InstallationService, cache, storage, new Metrics(services.GetRequiredService<IMeterFactory>()));
        }

        public TimingDbContext Storage { get; }
        public InstallationService InstallationService { get; }
        public TransponderService TransponderService { get; }
        public AgentService AgentService { get; }

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
