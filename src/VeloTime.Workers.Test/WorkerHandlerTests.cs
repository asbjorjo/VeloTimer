using System.Reflection;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SlimMessageBus;
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
            Id = "passing-1", Time = time, TransponderType = "X2", TransponderId = "106187718", LoopId = "loop-1"
        }]);
        await service.SendEventsAsync([segment]);
        await service.SendEventsAsync([system]);

        Assert.Equal(6, recorder.Published.Count);
        Assert.IsType<VeloTime.Agent.Interface.Messages.Events.InstallationLayoutEvent>(recorder.Published[0].Payload);
        Assert.IsAssignableFrom<IEnumerable<VeloTime.Agent.Interface.Messages.Events.LoopConfigEvent>>(recorder.Published[1].Payload);
        Assert.IsType<VeloTime.Agent.Interface.Messages.Events.LoopStatusEvent>(recorder.Published[2].Payload);
        Assert.IsAssignableFrom<IEnumerable<VeloTime.Agent.Interface.Messages.Events.PassingEvent>>(recorder.Published[3].Payload);
        Assert.IsAssignableFrom<IEnumerable<VeloTime.Agent.Interface.Messages.Events.SegmentConfigEvent>>(recorder.Published[4].Payload);
        Assert.IsAssignableFrom<IEnumerable<VeloTime.Agent.Interface.Messages.Events.SystemConfigEvent>>(recorder.Published[5].Payload);
        Assert.Single(await storage.Set<VeloTime.Agent.Model.Passing>().ToListAsync());
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
            DateTime.UtcNow, "X2", "106187718", "loop-1"), CancellationToken.None);

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
        var metrics = new Metrics(serviceProvider.GetRequiredService<IMeterFactory>());
        var (messageBus, recorder) = RecordingMessageBus.Create();
        var sampleHandler = new SampleCompleteHandler(statistics, metrics, messageBus, NullLogger<SampleCompleteHandler>.Instance);
        var entryHandler = new EntryCreatedHandler(statistics, messageBus, NullLogger<EntryCreatedHandler>.Instance);

        await sampleHandler.OnHandle(new SampleComplete(Guid.NewGuid(), DateTime.UtcNow.AddSeconds(-10), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), 100), CancellationToken.None);
        await entryHandler.OnHandle(new EntryCreated(Guid.NewGuid(), DateTime.UtcNow.AddSeconds(-10), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(2, recorder.Published.Count);
        Assert.Empty((IEnumerable<EntryCreated>)recorder.Published[0].Payload!);
        Assert.Equal(true, recorder.Published[0].Headers!["Single"]);
        Assert.Empty((IEnumerable<EntryCreated>)recorder.Published[1].Payload!);
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

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
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

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name == "get_Headers" ? headers : null;
        }

        public static IConsumerContext Create(IDictionary<string, object> headers)
        {
            var context = DispatchProxy.Create<IConsumerContext, TestConsumerContext>();
            ((TestConsumerContext)(object)context).headers = headers;
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
            InstallationService = new InstallationService(storage, serviceProvider.GetRequiredService<HybridCache>());
        }

        public TimingDbContext Storage { get; }
        public InstallationService InstallationService { get; }

        public static async Task<TimingTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<TimingDbContext>().UseSqlite(connection).Options;
            var storage = new TimingDbContext(options);
            await storage.Database.EnsureCreatedAsync();
            var services = new ServiceCollection();
            services.AddHybridCache();
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
