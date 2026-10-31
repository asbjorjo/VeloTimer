using System.Reflection;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SlimMessageBus;
using SlimMessageBus.Host.AzureServiceBus;
using VeloTime.Agent.Handler;
using VeloTime.Agent.Interface.Messages.Control;
using VeloTime.Agent.Service;
using VeloTime.Agent.Storage;
using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Statistics;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Handlers;
using VeloTime.Module.Timing.Interface.Messages;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Timing.Storage;
using VeloTime.Module.Common;
using VeloTime.Agent.Interface.Messages.Events;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace VeloTime.Workers.Test;

public sealed class WorkerHandlerTests
{
    [Fact]
    public async Task AgentControlHandlers_DelegatePauseAndResumeCommands()
    {
        var control = new RecordingControlService();
        var pause = new PauseAgentHandler(control, NullLogger<PauseAgentHandler>.Instance);
        var resume = new ResumeAgentHandler(control, NullLogger<ResumeAgentHandler>.Instance);

        await pause.OnHandle(new PauseAgentCommand("agent"), CancellationToken.None);
        await resume.OnHandle(new ResumeAgentCommand("agent"), CancellationToken.None);

        Assert.Equal(1, control.PauseCalls);
        Assert.Equal(1, control.ResumeCalls);
    }

    [Fact]
    public async Task AgentMessagingService_PublishesAllEventContractsAndStoresPassings()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AgentDbContext>().UseSqlite(connection).Options;
        await using var storage = new AgentDbContext(options);
        await storage.Database.EnsureCreatedAsync();
        var (messageBus, recorder) = RecordingMessageBus.Create();
        using var serviceProvider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var service = new MessagingService(
            messageBus,
            storage,
            new VeloTime.Agent.Metrics(serviceProvider.GetRequiredService<IMeterFactory>()),
            NullLogger<MessagingService>.Instance);
        var loop = new VeloTime.Agent.Model.LoopConfig { LoopId = "loop-1", Name = "Start", Order = 1, SystemId = "system-1" };
        var segment = new VeloTime.Agent.Model.SegmentConfig
        {
            SegmentId = "segment-1", Name = "Lap", FromLoopId = "loop-1", ToLoopId = "loop-2", Length = 250, SystemId = "system-1"
        };
        var system = new VeloTime.Agent.Model.SystemConfig
        {
            SystemId = "system-1", Sport = "cycling", Description = "Timing system",
            Loops = [loop], Segments = [segment]
        };
        var time = DateTime.UtcNow;

        await service.SendEventAsync(new VeloTime.Agent.Model.InstallationConfig { Systems = [system] });
        await service.SendEventsAsync([loop]);
        await service.SendEventAsync(new VeloTime.Agent.Model.LoopStatus { Time = time, LoopId = "loop-1", Noise = 0.25 });
        await service.SendEventsAsync([new VeloTime.Agent.Model.Passing
        {
            Id = "passing-1", Time = time, TransponderType = "X2", TransponderId = "106187718", LoopId = "loop-1",
            LowBattery = true, LowStrength = true, LowHits = true
        }]);
        await service.SendEventsAsync([segment]);
        await service.SendEventsAsync([system]);

        Assert.Equal(6, recorder.Published.Count);
        Assert.IsType<VeloTime.Agent.Interface.Messages.Events.InstallationLayoutEvent>(recorder.Published[0].Payload);
        Assert.IsAssignableFrom<IEnumerable<VeloTime.Agent.Interface.Messages.Events.LoopConfigEvent>>(recorder.Published[1].Payload);
        Assert.IsType<VeloTime.Agent.Interface.Messages.Events.LoopStatusEvent>(recorder.Published[2].Payload);
        Assert.IsAssignableFrom<IEnumerable<VeloTime.Agent.Interface.Messages.Events.PassingEvent>>(recorder.Published[3].Payload);
        var sentPassing = Assert.Single((IEnumerable<VeloTime.Agent.Interface.Messages.Events.PassingEvent>)recorder.Published[3].Payload!);
        Assert.True(sentPassing.LowBattery && sentPassing.LowStrength && sentPassing.LowHits);
        Assert.IsAssignableFrom<IEnumerable<VeloTime.Agent.Interface.Messages.Events.SegmentConfigEvent>>(recorder.Published[4].Payload);
        Assert.IsAssignableFrom<IEnumerable<VeloTime.Agent.Interface.Messages.Events.SystemConfigEvent>>(recorder.Published[5].Payload);
        var storedPassing = await storage.Set<VeloTime.Agent.Model.Passing>().SingleAsync();
        Assert.True(storedPassing.LowBattery && storedPassing.LowStrength && storedPassing.LowHits);
    }

    [Fact]
    public async Task LoopStatusEventHandler_RequiresAgentIdAndUpdatesLastSeen()
    {
        await using var database = await TimingTestDatabase.CreateAsync();
        var handler = new LoopStatusEventHandler(database.InstallationService)
        {
            Context = TestConsumerContext.Create(new Dictionary<string, object>())
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.OnHandle(
            new VeloTime.Agent.Interface.Messages.Events.LoopStatusEvent(DateTime.UtcNow, "loop-1", 0.1),
            CancellationToken.None));

        var installation = new Installation { Id = Guid.NewGuid(), AgentId = "agent-loop" };
        database.Storage.Add(installation);
        await database.Storage.SaveChangesAsync();
        database.Storage.ChangeTracker.Clear();
        var seenAt = DateTime.UtcNow;
        handler.Context = TestConsumerContext.Create(new Dictionary<string, object> { ["AgentId"] = "agent-loop" });

        await handler.OnHandle(new VeloTime.Agent.Interface.Messages.Events.LoopStatusEvent(seenAt, "loop-1", 0.1), CancellationToken.None);

        Assert.Equal(seenAt, (await database.Storage.Set<Installation>().SingleAsync()).LastSeen);
    }

    [Fact]
    public async Task InstallationLayoutHandler_InvalidatesUnloadedExistingAndIntermediateCreatedPointIdentities()
    {
        await using var database = await TimingTestDatabase.CreateAsync();
        var installation = new Installation
        {
            Id = Guid.NewGuid(), AgentId = "layout-agent", Description = "", TimingSystem = TimingSystem.Unknown
        };
        var existing = new TimingPoint
        {
            Id = Guid.NewGuid(), InstallationId = installation.Id, Installation = installation,
            SystemId = "existing-loop", Description = ""
        };
        database.Storage.AddRange(installation, existing);
        await database.Storage.SaveChangesAsync();
        database.Storage.ChangeTracker.Clear();

        var cachedInstallation = await database.InstallationService.GetInstallationForAgent("layout-agent");
        Assert.NotNull(cachedInstallation);
        Assert.Empty(cachedInstallation.TimingPoints);
        Assert.Equal(installation.Id, (await database.InstallationService.GetInstallionForTimingPoint(existing.Id)).Id);
        Assert.Equal(existing.Id, (await database.InstallationService.GetTimingPoint(cachedInstallation, existing.SystemId))?.Id);

        var context = TestConsumerContext.Create(new Dictionary<string, object> { ["AgentId"] = "layout-agent" });
        Assert.Equal("layout-agent", context.GetTransportMessage().ApplicationProperties["AgentId"]);
        var handler = new InstallationLayoutHandler(
            database.InstallationService, database.Storage, NullLogger<InstallationLayoutHandler>.Instance,
            database.ServiceProvider.GetRequiredService<HybridCache>(), database.Operations)
        {
            Context = context
        };
        var layout = new InstallationLayoutEvent(
            [new SystemConfigEvent("system-1", "cycling", "Installed system", true)],
            [
                new LoopConfigEvent(existing.SystemId, "Existing loop description", 1, "system-1"),
                new LoopConfigEvent("new-loop", "New loop description", 2, "system-1")
            ],
            []);

        await handler.OnHandle(layout, CancellationToken.None);

        database.Storage.ChangeTracker.Clear();
        var refreshedInstallation = await database.InstallationService.GetInstallationForAgent("layout-agent");
        Assert.NotNull(refreshedInstallation);
        Assert.Equal("Installed system", refreshedInstallation.Description);
        Assert.Equal(TimingSystem.MyLaps_X2, refreshedInstallation.TimingSystem);
        Assert.Equal("Existing loop description",
            (await database.InstallationService.GetTimingPoint(refreshedInstallation, "existing-loop"))?.Description);
        Assert.Equal("New loop description",
            (await database.InstallationService.GetTimingPoint(refreshedInstallation, "new-loop"))?.Description);
        Assert.Equal(installation.Id,
            (await database.InstallationService.GetInstallionForTimingPoint(existing.Id)).Id);
        var createdPoint = await database.Storage.Set<TimingPoint>().SingleAsync(point => point.SystemId == "new-loop");
        Assert.Equal(installation.Id,
            (await database.InstallationService.GetInstallionForTimingPoint(createdPoint.Id)).Id);

        await handler.OnHandle(layout with
        {
            Systems = [new SystemConfigEvent("system-1", "cycling", "Do not replace filled description", true)],
            TimingLoops =
            [
                new LoopConfigEvent(existing.SystemId, "Do not replace filled loop", 1, "system-1"),
                new LoopConfigEvent("new-loop", "Do not replace filled loop", 2, "system-1")
            ]
        }, CancellationToken.None);

        database.Storage.ChangeTracker.Clear();
        refreshedInstallation = await database.InstallationService.GetInstallationForAgent("layout-agent");
        Assert.Equal("Installed system", refreshedInstallation!.Description);
        Assert.Equal("Existing loop description",
            (await database.InstallationService.GetTimingPoint(refreshedInstallation, "existing-loop"))?.Description);
        Assert.Equal("New loop description",
            (await database.InstallationService.GetTimingPoint(refreshedInstallation, "new-loop"))?.Description);
        Assert.Equal(2, await database.Storage.Set<TimingPoint>().CountAsync());
    }

    [Fact]
    public async Task InstallationLayoutHandler_CanceledBeforeDatabaseReadDoesNotApplyLayout()
    {
        await using var database = await TimingTestDatabase.CreateAsync();
        database.Storage.Add(new Installation
        {
            Id = Guid.NewGuid(), AgentId = "layout-cancel-agent", Description = "", TimingSystem = TimingSystem.Unknown
        });
        await database.Storage.SaveChangesAsync();

        var context = TestConsumerContext.Create(new Dictionary<string, object> { ["AgentId"] = "layout-cancel-agent" });
        Assert.Equal("layout-cancel-agent", context.GetTransportMessage().ApplicationProperties["AgentId"]);
        var handler = new InstallationLayoutHandler(
            database.InstallationService, database.Storage, NullLogger<InstallationLayoutHandler>.Instance,
            database.ServiceProvider.GetRequiredService<HybridCache>(), database.Operations)
        {
            Context = context
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.OnHandle(
            new InstallationLayoutEvent(
                [new SystemConfigEvent("system-1", "cycling", "Cancelled", true)],
                [new LoopConfigEvent("new-loop", "Cancelled", 1, "system-1")],
                []),
            cancellation.Token));

        Assert.Empty(await database.Storage.Set<TimingPoint>().ToListAsync());
        var persisted = await database.Storage.Set<Installation>().SingleAsync();
        Assert.Equal("", persisted.Description);
        Assert.Equal(TimingSystem.Unknown, persisted.TimingSystem);
    }

    [Fact]
    public async Task PassingObservedHandler_PublishesPassingAndSampleEvents()
    {
        await using var database = await TimingTestDatabase.CreateAsync();
        var transponderId = Guid.NewGuid();
        var timingPointStart = Guid.NewGuid();
        var timingPointEnd = Guid.NewGuid();
        var start = new Passing
        {
            Id = Guid.NewGuid(), Time = DateTime.UtcNow.AddSeconds(-10), TransponderId = transponderId,
            TimingPointId = timingPointStart
        };
        var end = new Passing
        {
            Id = Guid.NewGuid(), Time = DateTime.UtcNow, TransponderId = transponderId,
            TimingPointId = timingPointEnd
        };
        var sample = new VeloTime.Module.Timing.Model.Sample { Id = Guid.NewGuid(), Start = start, End = end };
        var agentService = DispatchProxy.Create<IAgentService, RecordingAgentService>();
        var agentProxy = (RecordingAgentService)(object)agentService;
        agentProxy.Passing = end;
        agentProxy.Sample = sample;
        var (messageBus, recorder) = RecordingMessageBus.Create();
        var handler = new PassingObservedHandler(agentService, database.InstallationService, messageBus)
        {
            Context = TestConsumerContext.Create(new Dictionary<string, object> { ["AgentId"] = "agent-test" })
        };

        await handler.OnHandle(new VeloTime.Agent.Interface.Messages.Events.PassingEvent(
            DateTime.UtcNow, "X2", "106187718", "loop-1") { LowBattery = true, LowStrength = true, LowHits = true }, CancellationToken.None);
        Assert.Equal(new object?[] { true, true, true }, agentProxy.PassingArguments![5..8]);

        Assert.Collection(recorder.Published,
            call => Assert.IsType<PassingSaved>(call.Payload),
            call => Assert.IsType<TimingSampleComplete>(call.Payload));
    }

    [Fact]
    public async Task TimingSampleHandler_PersistsSampleAndPublishesCompletedEvent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<StatisticsDbContext>().UseSqlite(connection).Options;
        await using var storage = new StatisticsDbContext(options);
        await storage.Database.EnsureCreatedAsync();
        var startTime = DateTime.UtcNow.AddSeconds(-20);
        var endTime = DateTime.UtcNow;
        var distance = new VeloTime.Module.Facilities.Client.CoursePointDistance
        {
            CoursePointStartId = Guid.NewGuid(),
            CoursePointEndId = Guid.NewGuid(),
            Distance = 150,
            FacilityId = Guid.NewGuid()
        };
        var facilities = DispatchProxy.Create<IFacilitiesClient, RecordingFacilitiesClient>();
        ((RecordingFacilitiesClient)(object)facilities).Distance = distance;
        var (messageBus, recorder) = RecordingMessageBus.Create();
        using var serviceProvider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var handler = new TimingSampleHandler(
            storage,
            facilities,
            messageBus,
            new Metrics(serviceProvider.GetRequiredService<IMeterFactory>()),
            NullLogger<TimingSampleHandler>.Instance);

        await handler.OnHandle(new TimingSampleComplete(startTime, endTime, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        var sample = await storage.Set<VeloTime.Module.Statistics.Model.Sample>().SingleAsync();
        Assert.Equal(distance.Distance, sample.Distance);
        Assert.Equal(distance.FacilityId, sample.FacilityId);
        Assert.Equal(distance.Distance / (endTime - startTime).TotalSeconds, sample.Speed);
        var published = Assert.IsType<SampleComplete>(Assert.Single(recorder.Published).Payload);
        Assert.Equal(sample.CoursePointStartId, published.CoursePointStart);
        Assert.Equal(sample.CoursePointEndId, published.CoursePointEnd);
    }

    [Fact]
    public async Task StatisticsHandlers_PublishEmptyEntryBatchesForUnconfiguredSample()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<StatisticsDbContext>().UseSqlite(connection).Options;
        await using var storage = new StatisticsDbContext(options);
        await storage.Database.EnsureCreatedAsync();
        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddMetrics();
        using var serviceProvider = services.BuildServiceProvider();
        var statistics = new StatisticsService(storage, serviceProvider.GetRequiredService<HybridCache>());
        var ownership = new StatisticsOwnershipService(storage, new EmptyOwnershipReader());
        var metrics = new Metrics(serviceProvider.GetRequiredService<IMeterFactory>());
        var (messageBus, recorder) = RecordingMessageBus.Create();
        var sampleHandler = new SampleCompleteHandler(statistics, ownership, metrics, messageBus, NullLogger<SampleCompleteHandler>.Instance);
        var entryHandler = new EntryCreatedHandler(statistics, ownership, messageBus, NullLogger<EntryCreatedHandler>.Instance);

        await sampleHandler.OnHandle(new SampleComplete(Guid.NewGuid(), DateTime.UtcNow.AddSeconds(-10), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), 100), CancellationToken.None);
        await entryHandler.OnHandle(new EntryCreated(Guid.NewGuid(), DateTime.UtcNow.AddSeconds(-10), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(2, recorder.Published.Count);
        Assert.Empty((IEnumerable<EntryCreated>)recorder.Published[0].Payload!);
        Assert.Equal(true, recorder.Published[0].Headers!["Single"]);
        Assert.Empty((IEnumerable<EntryCreated>)recorder.Published[1].Payload!);
    }

    private sealed class EmptyOwnershipReader : ITransponderOwnershipReader
    {
        public Task<IReadOnlyList<VeloTime.Module.Timing.Interface.Data.TransponderOwnershipDTO>> ReadAsync(
            Guid transponderId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VeloTime.Module.Timing.Interface.Data.TransponderOwnershipDTO>>([]);
    }

    private sealed class RecordingControlService : IControlService
    {
        public int PauseCalls { get; private set; }
        public int ResumeCalls { get; private set; }
        public Task PauseAgent() { PauseCalls++; return Task.CompletedTask; }
        public Task ResumeAgent() { ResumeCalls++; return Task.CompletedTask; }
    }

    public class RecordingMessageBus : DispatchProxy
    {
        public List<PublishedMessage> Published { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != "Publish")
            {
                throw new NotSupportedException(targetMethod?.Name);
            }

            var payload = args?.FirstOrDefault();
            var headers = args?.OfType<IDictionary<string, object>>().FirstOrDefault();
            Published.Add(new PublishedMessage(payload, headers));
            return Task.CompletedTask;
        }

        public static (IMessageBus Bus, RecordingMessageBus Recorder) Create()
        {
            var bus = DispatchProxy.Create<IMessageBus, RecordingMessageBus>();
            return (bus, (RecordingMessageBus)(object)bus);
        }
    }

    public class RecordingAgentService : DispatchProxy
    {
        public VeloTime.Module.Timing.Model.Passing Passing { get; set; } = null!;
        public VeloTime.Module.Timing.Model.Sample? Sample { get; set; }
        public object?[]? PassingArguments { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IAgentService.RegisterPassingAsync)) PassingArguments = args;
            return targetMethod?.Name switch
            {
                nameof(IAgentService.RegisterPassingAsync) => Task.FromResult(Passing),
                nameof(IAgentService.RegisterSampleAsync) => Task.FromResult(Sample),
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
        }
    }

    public class RecordingFacilitiesClient : DispatchProxy
    {
        public VeloTime.Module.Facilities.Client.CoursePointDistance Distance { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name == "Distance2Async"
                ? Task.FromResult(Distance)
                : throw new NotSupportedException(targetMethod?.Name);
        }
    }

    public class TestConsumerContext : DispatchProxy
    {
        private IDictionary<string, object> headers = new Dictionary<string, object>();
        private IDictionary<string, object> properties = new Dictionary<string, object>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                "get_Headers" => headers,
                "get_Properties" => properties,
                _ => null
            };
        }

        public static IConsumerContext Create(IDictionary<string, object> headers)
        {
            var context = DispatchProxy.Create<IConsumerContext, TestConsumerContext>();
            var proxy = (TestConsumerContext)(object)context;
            proxy.headers = headers;
            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
                properties: new Dictionary<string, object>(headers));
            proxy.properties["transportMessage"] = message;
            proxy.properties["TransportMessage"] = message;
            proxy.properties["ServiceBusReceivedMessage"] = message;
            proxy.properties["ServiceBus_Message"] = message;
            proxy.headers["transportMessage"] = message;
            proxy.headers["TransportMessage"] = message;
            return context;
        }
    }

    public sealed record PublishedMessage(object? Payload, IDictionary<string, object>? Headers);

    private sealed class TimingTestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider serviceProvider;

        private TimingTestDatabase(SqliteConnection connection, TimingDbContext storage, ServiceProvider serviceProvider)
        {
            this.connection = connection;
            this.serviceProvider = serviceProvider;
            Storage = storage;
            Operations = new FusionCacheInvalidationOperations(serviceProvider.GetRequiredService<IFusionCache>(), new FusionCacheCircuitBreakerState());
            InstallationService = new InstallationService(storage, serviceProvider.GetRequiredService<HybridCache>(), null, Operations);
        }

        public ICacheInvalidationOperations Operations { get; }

        public TimingDbContext Storage { get; }
        public ServiceProvider ServiceProvider => serviceProvider;
        public InstallationService InstallationService { get; }

        public static async Task<TimingTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<TimingDbContext>().UseSqlite(connection).Options;
            var storage = new TimingDbContext(options);
            await storage.Database.EnsureCreatedAsync();
            var services = new ServiceCollection();
            services.AddFusionCache()
                .WithSerializer(new FusionCacheSystemTextJsonSerializer(new JsonSerializerOptions
                {
                    ReferenceHandler = ReferenceHandler.Preserve
                }))
                .AsHybridCache();
            return new TimingTestDatabase(connection, storage, services.BuildServiceProvider());
        }

        public async ValueTask DisposeAsync()
        {
            await Storage.DisposeAsync();
            await serviceProvider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
