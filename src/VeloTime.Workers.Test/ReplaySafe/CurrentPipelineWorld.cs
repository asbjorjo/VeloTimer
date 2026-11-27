using System.Collections;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SlimMessageBus;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Service;
using VeloTime.Module.Facilities.Storage;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Handlers;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Interface.Messages;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Timing.Storage;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;
using StatisticsMetrics = VeloTime.Module.Statistics.Metrics;
using TimingMetrics = VeloTime.Module.Timing.Metrics;
using StatisticsSample = VeloTime.Module.Statistics.Model.Sample;

namespace VeloTime.Workers.Test.ReplaySafe;

/// <summary>Creates the pipeline under test. Each plan phase rewires this one method.</summary>
public static class PipelineWorlds
{
    public static Task<IPipelineWorld> CreateAsync(Redelivery redelivery = Redelivery.None) =>
        CurrentPipelineWorld.CreateAsync(redelivery);
}

/// <summary>
/// The pipeline as of Phase 1: Timing records each <see cref="PassingObservedV1"/> as an idempotent
/// fact and publishes <see cref="PassingRecordedV1"/>, which Statistics does not consume until Phase 2,
/// so no samples or entries are derived yet. Statistics' SampleComplete → EntryCreated handlers are
/// wired for when they are fed again; Facilities configuration lives in its own database. Messages
/// go through an in-memory FIFO bus; a failing message is retried a few times and then
/// dead-lettered, as the broker would.
/// </summary>
internal sealed class CurrentPipelineWorld : IPipelineWorld
{
    private const int MaxAttempts = 3;

    private readonly string admin;
    private readonly string id = Guid.NewGuid().ToString("N")[..12];
    private readonly Redelivery redelivery;
    private readonly ServiceProvider services;
    private readonly Queue<Envelope> queue = new();
    private readonly List<Envelope> consumed = [];
    private readonly List<string> deadLetters = [];
    private readonly Dictionary<string, Guid> coursePoints = new();
    private readonly Dictionary<Guid, string> coursePointNames = new();
    private readonly Dictionary<string, Guid> facilities = new();
    private readonly Dictionary<Guid, string> facilityNames = new();
    private readonly Dictionary<string, SimpleStatisticsItemConfig> simpleItems = new();
    private readonly Dictionary<Guid, string> itemNames = new();
    // MYLAPS X2 transponder IDs are numeric; riders are named in the tests.
    private readonly Dictionary<string, string> transponderIds = new();
    private string timing = "", facility = "", statistics = "";

    private CurrentPipelineWorld(string admin, Redelivery redelivery)
    {
        this.admin = admin;
        this.redelivery = redelivery;
        var collection = new ServiceCollection();
        collection.AddMetrics();
        // Timing caches entity graphs; mirror its production serializer settings.
        collection.AddFusionCache()
            .WithSerializer(new FusionCacheSystemTextJsonSerializer(new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve
            }))
            .AsHybridCache();
        services = collection.BuildServiceProvider();
    }

    public IReadOnlyList<string> DeadLetters => deadLetters;

    private HybridCache Cache => services.GetRequiredService<HybridCache>();
    private IMeterFactory Meters => services.GetRequiredService<IMeterFactory>();

    public static async Task<IPipelineWorld> CreateAsync(Redelivery redelivery)
    {
        var admin = Environment.GetEnvironmentVariable(RequirementFactAttribute.PostgresVariable)
                    ?? throw new InvalidOperationException($"{RequirementFactAttribute.PostgresVariable} is not set.");
        var world = new CurrentPipelineWorld(admin, redelivery);
        world.timing = await world.CreateDatabaseAsync("timing");
        world.facility = await world.CreateDatabaseAsync("facility");
        world.statistics = await world.CreateDatabaseAsync("statistics");
        await using (var db = world.TimingDb()) await db.Database.MigrateAsync();
        await using (var db = world.FacilityDb()) await db.Database.MigrateAsync();
        await using (var db = world.StatisticsDb()) await db.Database.MigrateAsync();
        return world;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in new[] { timing, facility, statistics }.Where(c => c.Length > 0))
        {
            using (var pool = new NpgsqlConnection(connection)) NpgsqlConnection.ClearPool(pool);
            var database = new NpgsqlConnectionStringBuilder(connection).Database;
            await ExecuteAsync(admin, $"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)");
        }
        await services.DisposeAsync();
    }

    // ---- Configuration ----------------------------------------------------------------------

    public async Task CreateFacilityAsync(string name, IReadOnlyList<SegmentInput> segments)
    {
        await using var db = FacilityDb();
        var created = new Facility { Id = Guid.NewGuid(), Name = name };
        var layout = new CourseLayout { Id = Guid.NewGuid(), FacilityId = created.Id, Facility = created };
        var order = 0;
        foreach (var segment in segments)
        {
            layout.Segments.Add(new CourseSegment
            {
                Id = Guid.NewGuid(), CourseLayout = layout, CourseLayoutId = layout.Id, Order = order++,
                Start = Point(segment.From), End = Point(segment.To), Length = segment.Length
            });
        }
        db.Add(created);
        db.Add(layout);
        await db.SaveChangesAsync();
        facilities[name] = created.Id;
        facilityNames[created.Id] = name;

        CoursePoint Point(string pointName)
        {
            var tracked = db.ChangeTracker.Entries<CoursePoint>().Select(e => e.Entity)
                .FirstOrDefault(p => p.Name == pointName);
            if (tracked is not null) return tracked;
            if (coursePoints.ContainsKey(pointName))
                throw new InvalidOperationException($"Course point {pointName} already belongs to another facility.");
            var point = new CoursePoint { Id = Guid.NewGuid(), Name = pointName };
            coursePoints[pointName] = point.Id;
            coursePointNames[point.Id] = pointName;
            db.Add(point);
            return point;
        }
    }

    public async Task AssignInstallationAsync(string agent, string facilityName, DateTime? validFrom = null)
    {
        if (validFrom is not null)
            throw new NotSupportedException("C2: Installation.Facility is still a mutable field; it has no effective date.");
        await using var db = TimingDb();
        var installation = await EnsureInstallationAsync(db, agent);
        var writable = await db.Set<Installation>().SingleAsync(i => i.Id == installation.Id);
        writable.Facility = facilities[facilityName];
        await db.SaveChangesAsync();
    }

    public async Task AssociateLoopAsync(string agent, string loop, string coursePoint, DateTime? from = null, DateTime? to = null)
    {
        var timingPointId = await EnsureTimingPointAsync(agent, loop);
        await using var db = FacilityDb();
        db.Add(new CoursePointTimingAssociation
        {
            Id = Guid.NewGuid(), CoursePointId = coursePoints[coursePoint], TimingPointId = timingPointId,
            ValidFromUtc = from.HasValue ? new DateTimeOffset(from.Value) : null,
            ValidToUtc = to.HasValue ? new DateTimeOffset(to.Value) : null
        });
        await db.SaveChangesAsync();
        // Facilities publishes CoursePointTimingAssociationsChangedV1, but no service consumes it yet (C4).
    }

    public async Task MoveLoopsAsync(string agent, IReadOnlyDictionary<string, string> moves, DateTime from)
    {
        var timingPoints = new Dictionary<string, Guid>();
        foreach (var loop in moves.Keys) timingPoints[loop] = await EnsureTimingPointAsync(agent, loop);
        await using var db = FacilityDb();
        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var (loop, timingPointId) in timingPoints)
        {
            var open = await db.Set<CoursePointTimingAssociation>()
                .SingleAsync(a => a.TimingPointId == timingPointId && a.ValidToUtc == null);
            open.ValidToUtc = new DateTimeOffset(from);
        }
        await db.SaveChangesAsync();
        foreach (var (loop, timingPointId) in timingPoints)
            db.Add(new CoursePointTimingAssociation
            {
                Id = Guid.NewGuid(), CoursePointId = coursePoints[moves[loop]], TimingPointId = timingPointId,
                ValidFromUtc = new DateTimeOffset(from)
            });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task DefineSimpleItemAsync(string name, string start, string end, double distance,
        DateTime? validFrom = null, TimeSpan? maxDuration = null)
    {
        if (validFrom is not null)
            throw new NotSupportedException("C3: statistics definitions have no effective date yet.");
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = name, Distance = (float)distance, MaxDuration = maxDuration };
        var config = new SimpleStatisticsItemConfig
        {
            Id = Guid.NewGuid(), StatisticsItem = item, StatisticsItemId = item.Id,
            CoursePointStart = coursePoints[start], CoursePointEnd = coursePoints[end]
        };
        await using var db = StatisticsDb();
        db.Add(config);
        await db.SaveChangesAsync();
        simpleItems[name] = config;
        itemNames[item.Id] = name;
    }

    public async Task DefineMultiItemAsync(string name, string parent, int repetitions, double distance)
    {
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = name, Distance = (float)distance };
        await using var db = StatisticsDb();
        var parentConfig = await db.Set<SimpleStatisticsItemConfig>().SingleAsync(c => c.Id == simpleItems[parent].Id);
        db.Add(new MultiStatisticsItemConfig
        {
            Id = Guid.NewGuid(), StatisticsItem = item, StatisticsItemId = item.Id,
            ParentConfig = parentConfig, ParentConfigId = parentConfig.Id, Repetitions = repetitions
        });
        await db.SaveChangesAsync();
        itemNames[item.Id] = name;
    }

    public Task ChangeSegmentLengthsAsync(string facilityName, IReadOnlyList<SegmentInput> segments, DateTime validFrom) =>
        throw new NotSupportedException("C1: course layouts and segment lengths are still edited in place; there are no dated revisions.");

    public Task ChangeItemAsync(string name, ItemChange change, DateTime validFrom) =>
        throw new NotSupportedException("C3: statistics definitions (thresholds, required points) are not versioned yet.");

    // ---- Input ------------------------------------------------------------------------------

    public Task DeliverAsync(PassingInput passing)
    {
        if (!transponderIds.TryGetValue(passing.Rider, out var transponder))
            transponderIds[passing.Rider] = transponder = (200_000_000 + transponderIds.Count).ToString();
        Enqueue(new PassingObservedV1(new Guid(0, 0, 0, BitConverter.GetBytes(passing.SourcePassingId)), passing.Time, "ProChip", transponder, passing.Loop),
            new Dictionary<string, object> { ["AgentId"] = passing.Agent });
        return Task.CompletedTask;
    }

    public async Task DrainAsync()
    {
        while (queue.TryDequeue(out var envelope))
        {
            var times = redelivery == Redelivery.Immediate ? 2 : 1;
            for (var i = 0; i < times; i++) await ConsumeAsync(envelope);
        }
    }

    public async Task RedeliverHistoryAsync()
    {
        foreach (var envelope in consumed.ToList()) queue.Enqueue(envelope with { Attempt = 1 });
        await DrainAsync();
    }

    public async Task InvalidateEntryAsync(Guid entryId, string note)
    {
        // The same field updates as AdminStatisticsEntryEndpoints' manual invalidation.
        await using var db = StatisticsDb();
        var entry = await db.Set<StatisticsEntry>().SingleAsync(e => e.Id == entryId);
        entry.IsValid = false;
        entry.InvalidReason = StatisticsEntryInvalidReason.ManuallyInvalidated;
        entry.ManualInvalidationChangedByUserId = Guid.NewGuid();
        entry.ManualInvalidationChangedAtUtc = DateTimeOffset.UtcNow;
        entry.ManualInvalidationNote = note;
        await db.SaveChangesAsync();
    }

    // ---- Observation ------------------------------------------------------------------------

    public async Task<PipelineSnapshot> SnapshotAsync()
    {
        Dictionary<Guid, string> riders;
        int passings;
        await using (var db = TimingDb())
        {
            var names = transponderIds.ToDictionary(pair => pair.Value, pair => pair.Key);
            riders = (await db.Set<Transponder>().AsNoTracking().ToListAsync())
                .ToDictionary(t => t.Id, t => names.GetValueOrDefault(t.SystemId, t.SystemId));
            passings = await db.Set<Passing>().CountAsync();
        }
        await using var stats = StatisticsDb();
        var samples = (await stats.Set<StatisticsSample>().AsNoTracking().ToListAsync())
            .Select(s => new SampleRow(s.Id, Name(riders, s.TransponderId), Name(coursePointNames, s.CoursePointStartId),
                Name(coursePointNames, s.CoursePointEndId), Utc(s.TimeStart), Utc(s.TimeEnd), s.Distance,
                Name(facilityNames, s.FacilityId)))
            .ToList();
        var entries = (await stats.Set<StatisticsEntry>().AsNoTracking().ToListAsync())
            .Select(e => new EntryRow(e.Id, Name(riders, e.TransponderId), Name(itemNames, e.StatisticsItemId),
                Utc(e.TimeStart), Utc(e.TimeEnd), e.Duration, e.DistanceMetres, e.IsValid, e.InvalidReason?.ToString(),
                e.ManualInvalidationNote, Name(facilityNames, e.FacilityId)))
            .ToList();
        return new PipelineSnapshot(samples, entries, passings);
    }

    public Task<IReadOnlyList<ManualDecisionReviewRow>> ManualDecisionReviewsAsync() =>
        throw new NotSupportedException("§5.4: there is no manual decision review yet; a decision on a changed entry is silently kept or lost.");

    // ---- Message handling -------------------------------------------------------------------

    private sealed record Envelope(object Message, IDictionary<string, object> Headers, int Attempt = 1);

    private void Enqueue(object message, IDictionary<string, object>? headers = null)
    {
        if (message is PassingRecordedV1) return; // No Statistics consumer until Phase 2.
        if (message is IEnumerable batch and not string)
        {
            foreach (var item in batch) Enqueue(item!, headers);
            return;
        }
        queue.Enqueue(new Envelope(message, headers ?? new Dictionary<string, object>()));
    }

    private async Task ConsumeAsync(Envelope envelope)
    {
        try
        {
            await HandleAsync(envelope);
            consumed.Add(envelope);
        }
        catch (Exception exception)
        {
            if (envelope.Attempt < MaxAttempts) queue.Enqueue(envelope with { Attempt = envelope.Attempt + 1 });
            else deadLetters.Add($"{envelope.Message}: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private async Task HandleAsync(Envelope envelope)
    {
        var bus = QueueBus.Create(this);
        switch (envelope.Message)
        {
            case PassingObservedV1 passing:
            {
                await using var db = TimingDb();
                var installations = new InstallationService(db, Cache);
                var agentService = new AgentService(installations, db, bus, new TimingMetrics(Meters));
                var handler = new PassingObservedHandler(agentService, installations)
                {
                    Context = WorkerHandlerTests.TestConsumerContext.Create(new Dictionary<string, object>(envelope.Headers))
                };
                await handler.OnHandle(passing, CancellationToken.None);
                break;
            }
            case SampleComplete completed:
            {
                await using var db = StatisticsDb();
                await new SampleCompleteHandler(new StatisticsService(db, Cache),
                    new StatisticsOwnershipService(db, new NoOwners()), new StatisticsMetrics(Meters), bus,
                    NullLogger<SampleCompleteHandler>.Instance).OnHandle(completed, CancellationToken.None);
                break;
            }
            case EntryCreated created:
            {
                await using var db = StatisticsDb();
                await new EntryCreatedHandler(new StatisticsService(db, Cache),
                    new StatisticsOwnershipService(db, new NoOwners()), bus,
                    NullLogger<EntryCreatedHandler>.Instance).OnHandle(created, CancellationToken.None);
                break;
            }
            default:
                throw new NotSupportedException($"No consumer for {envelope.Message.GetType().Name}.");
        }
    }

    public class QueueBus : DispatchProxy
    {
        private CurrentPipelineWorld world = null!;

        public static IMessageBus Create(CurrentPipelineWorld world)
        {
            var bus = DispatchProxy.Create<IMessageBus, QueueBus>();
            ((QueueBus)(object)bus).world = world;
            return bus;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != "Publish") throw new NotSupportedException(targetMethod?.Name);
            world.Enqueue(args![0]!, args.OfType<IDictionary<string, object>>().FirstOrDefault());
            return Task.CompletedTask;
        }
    }

    private sealed class NoOwners : ITransponderOwnershipReader
    {
        public Task<IReadOnlyList<TransponderOwnershipDTO>> ReadAsync(Guid transponderId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TransponderOwnershipDTO>>([]);
    }

    // ---- Storage ----------------------------------------------------------------------------

    private async Task<Installation> EnsureInstallationAsync(TimingDbContext db, string agent)
    {
        var installations = new InstallationService(db, Cache);
        return await installations.GetInstallationForAgent(agent)
               ?? await installations.CreateInstallationForAgent(agent, TimingSystem.MyLaps_X2);
    }

    private async Task<Guid> EnsureTimingPointAsync(string agent, string loop)
    {
        await using var db = TimingDb();
        var installation = await EnsureInstallationAsync(db, agent);
        var installations = new InstallationService(db, Cache);
        var point = await installations.GetTimingPoint(installation, loop)
                    ?? await installations.CreateTimingPoint(installation, loop);
        return point.Id;
    }

    private async Task<string> CreateDatabaseAsync(string service)
    {
        var name = $"rs_{id}_{service}";
        await ExecuteAsync(admin, $"CREATE DATABASE \"{name}\"");
        return new NpgsqlConnectionStringBuilder(admin) { Database = name }.ConnectionString;
    }

    private TimingDbContext TimingDb() => new(Options<TimingDbContext>(timing));
    private FacilityDbContext FacilityDb() => new(Options<FacilityDbContext>(facility));
    private StatisticsDbContext StatisticsDb() => new(Options<StatisticsDbContext>(statistics));

    private static DbContextOptions<T> Options<T>(string connection) where T : DbContext =>
        new DbContextOptionsBuilder<T>()
            .UseNpgsql(connection, o => o.MigrationsHistoryTable("__ef_migrations_history", "ef"))
            .UseSnakeCaseNamingConvention()
            .Options;

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var npgsql = new NpgsqlConnection(connection);
        await npgsql.OpenAsync();
        await using var command = new NpgsqlCommand(sql, npgsql);
        await command.ExecuteNonQueryAsync();
    }

    private static string Name(IReadOnlyDictionary<Guid, string> names, Guid id) =>
        names.TryGetValue(id, out var name) ? name : id == Guid.Empty ? "-" : "?" + id.ToString()[..8];

    private static DateTime Utc(DateTime time) => DateTime.SpecifyKind(time, DateTimeKind.Utc);
}
