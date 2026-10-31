using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Serialization;
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
