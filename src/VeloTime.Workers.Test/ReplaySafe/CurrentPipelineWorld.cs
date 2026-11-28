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
using Microsoft.AspNetCore.DataProtection;
using VeloTime.Module.Common;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Interface.Messages;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Service;
using VeloTime.Module.Facilities.Storage;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Service.Recompute;
using Microsoft.Extensions.Options;
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
/// The pipeline as of Phase 4: Timing records each <see cref="PassingObservedV1"/> as an idempotent
/// fact and publishes <see cref="PassingRecordedV1"/>; Statistics stores its own fact copy, marks the
/// range dirty, and the recompute worker derives samples and entries per riding session from the facts
/// and the configuration history (read through the shared cache from the Facilities and Timing
/// databases). Facilities course edits (layouts, revisions, associations) go through the Facilities
/// services, which publish <see cref="CourseConfigChangedV1"/>; Statistics' handler evicts the cached
/// history and marks the range dirty. Messages go through an in-memory FIFO bus; a failing message is
/// retried a few times and then dead-lettered, as the broker would. A recompute that fails is reported as
/// a dead letter too. Installation facility assignments and statistics config versions are dated too;
/// their edits go through the production services and change-event handlers.
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
    private readonly Dictionary<string, Guid> layouts = new();
    private readonly IDataProtectionProvider protection = new EphemeralDataProtectionProvider();
    private readonly Dictionary<Guid, string> facilityNames = new();
    private readonly Dictionary<string, SimpleStatisticsItemConfig> simpleItems = new();
    private readonly Dictionary<string, string> pointFacilities = new();
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
        db.Add(created);
        await db.SaveChangesAsync();
        var (points, requested) = Geometry(segments, allowNewPoints: true);
        await new CourseConfigurationService(db).CreateLayoutAsync(created.Id, points, requested, DateTimeOffset.UtcNow,
            EnqueueAsync, CancellationToken.None);
        facilities[name] = created.Id;
        facilityNames[created.Id] = name;
        layouts[name] = await db.Set<CourseLayout>().Where(l => l.FacilityId == created.Id).Select(l => l.Id).SingleAsync();
        foreach (var segment in segments) pointFacilities[segment.From] = pointFacilities[segment.To] = name;
    }

    /// <summary>The request shape of a chain of named segments; new names become new course points.</summary>
    private (List<SaveCoursePointRequest> Points, List<SaveCourseSegmentRequest> Segments) Geometry(
        IReadOnlyList<SegmentInput> segments, bool allowNewPoints)
    {
        var points = new List<SaveCoursePointRequest>();
        foreach (var pointName in segments.SelectMany(segment => new[] { segment.From, segment.To }).Distinct())
        {
            if (!coursePoints.TryGetValue(pointName, out var id))
            {
                if (!allowNewPoints) throw new InvalidOperationException($"Unknown course point {pointName}.");
                coursePoints[pointName] = id = Guid.NewGuid();
                coursePointNames[id] = pointName;
            }
            points.Add(new SaveCoursePointRequest(id, pointName));
        }
        var requested = segments.Select(segment =>
            new SaveCourseSegmentRequest(Guid.NewGuid(), coursePoints[segment.From], coursePoints[segment.To], segment.Length)).ToList();
        return (points, requested);
    }

    public async Task AssignInstallationAsync(string agent, string facilityName, DateTime? validFrom = null)
    {
        // As TimingAdministrationService does: a dated assignment plus InstallationFacilityChangedV1 in one transaction.
        await using var db = TimingDb();
        var installation = await EnsureInstallationAsync(db, agent);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var writable = await db.Set<Installation>().SingleAsync(i => i.Id == installation.Id);
        await InstallationFacilityService.AssignAsync(db, QueueBus.Create(this), writable, facilities[facilityName], validFrom,
            CancellationToken.None);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task AssociateLoopAsync(string agent, string loop, string coursePoint, DateTime? from = null, DateTime? to = null)
    {
        var timingPointId = await EnsureTimingPointAsync(agent, loop);
        await SetAssociationAsync(coursePoints[coursePoint], timingPointId,
            from.HasValue ? new DateTimeOffset(from.Value) : null, to.HasValue ? new DateTimeOffset(to.Value) : null);
    }

    public async Task MoveLoopsAsync(string agent, IReadOnlyDictionary<string, string> moves, DateTime from)
    {
        // One association edit per destination course point, as an administrator would make them.
        foreach (var (loop, coursePoint) in moves)
            await SetAssociationAsync(coursePoints[coursePoint], await EnsureTimingPointAsync(agent, loop), new DateTimeOffset(from), null);
    }

    /// <summary>Sets an association through the Facilities service, confirming a destination conflict as an admin would.</summary>
    private async Task SetAssociationAsync(Guid coursePointId, Guid timingPointId, DateTimeOffset? from, DateTimeOffset? to)
    {
        var request = new SetCoursePointTimingAssociationRequest { TimingPointId = timingPointId, ValidFromUtc = from, ValidToUtc = to };
        var references = new TimingReferences(this);
        await using (var db = FacilityDb())
        {
            var edit = await new TimingAssociationService(db, protection).SetIntervalAsync(coursePointId, request, DateTimeOffset.UtcNow,
                references, EnqueueAsync, CancellationToken.None);
            if (edit.ConflictPreview is null) return;
            request = request with { ConfirmationToken = edit.ConflictPreview.ConfirmationToken };
        }
        await using (var db = FacilityDb())
        {
            var confirmed = await new TimingAssociationService(db, protection).SetIntervalAsync(coursePointId, request, DateTimeOffset.UtcNow,
                references, EnqueueAsync, CancellationToken.None);
            if (confirmed.ConflictPreview is not null)
                throw new InvalidOperationException($"Association conflict was not confirmed: {confirmed.ConflictPreview.Code}");
        }
    }

    public async Task DefineSimpleItemAsync(string name, string start, string end, double distance,
        DateTime? validFrom = null, TimeSpan? maxDuration = null)
    {
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = name, Distance = (float)distance, MaxDuration = maxDuration };
        var config = new SimpleStatisticsItemConfig
        {
            Id = Guid.NewGuid(), StatisticsItem = item, StatisticsItemId = item.Id,
            CoursePointStart = coursePoints[start], CoursePointEnd = coursePoints[end]
        };
        await using var db = StatisticsDb();
        db.Add(config);
        // As StatisticsDefinitionEndpoints does: the first version counts history from validFrom (all of it when null).
        StatisticsConfigVersioning.Create(db, item, config, validFrom, DateTime.UtcNow);
        await db.SaveChangesAsync();
        simpleItems[name] = config;
        itemNames[item.Id] = name;
    }

    public async Task DefineMultiItemAsync(string name, string parent, int repetitions, double distance)
    {
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = name, Distance = (float)distance };
        await using var db = StatisticsDb();
        var parentConfig = await db.Set<SimpleStatisticsItemConfig>().SingleAsync(c => c.Id == simpleItems[parent].Id);
        var config = new MultiStatisticsItemConfig
        {
            Id = Guid.NewGuid(), StatisticsItem = item, StatisticsItemId = item.Id,
            ParentConfig = parentConfig, ParentConfigId = parentConfig.Id, Repetitions = repetitions
        };
        db.Add(config);
        StatisticsConfigVersioning.Create(db, item, config, null, DateTime.UtcNow);
        await db.SaveChangesAsync();
        itemNames[item.Id] = name;
    }

    public async Task ChangeSegmentLengthsAsync(string facilityName, IReadOnlyList<SegmentInput> segments, DateTime validFrom)
    {
        var (points, requested) = Geometry(segments, allowNewPoints: false);
        var request = new SetCourseLayoutRevisionRequest
        {
            Points = points, Segments = requested, ValidFromUtc = new DateTimeOffset(validFrom), ValidToUtc = null
        };
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var db = FacilityDb();
            var edit = await new CourseConfigurationService(db, protection).SetRevisionAsync(facilities[facilityName], layouts[facilityName],
                request, DateTimeOffset.UtcNow, EnqueueAsync, CancellationToken.None);
            if (edit.ConflictPreview is null) return;
            request = request with { ConfirmationToken = edit.ConflictPreview.ConfirmationToken };
        }
        throw new InvalidOperationException("The layout revision was not confirmed.");
    }

    public async Task ChangeItemAsync(string name, ItemChange change, DateTime validFrom)
    {
        // As StatisticsDefinitionEndpoints does: item values (thresholds) on the item, the rest on its configuration,
        // each applied to the version history from validFrom, which marks results from validFrom dirty.
        await using var db = StatisticsDb();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var config = await db.Set<SimpleStatisticsItemConfig>().Include(c => c.StatisticsItem).SingleAsync(c => c.Id == simpleItems[name].Id);
        if (change.MaxDuration is { } maxDuration)
        {
            var before = StatisticsConfigVersioning.Detach(config.StatisticsItem);
            config.StatisticsItem.MaxDuration = maxDuration;
            await StatisticsConfigVersioning.ApplyItemFromAsync(db, before, config.StatisticsItem, validFrom, DateTime.UtcNow, CancellationToken.None);
        }
        if (change.RequiredCoursePoints is { } required)
        {
            var before = StatisticsConfigVersioning.Snapshot(config.StatisticsItem, config);
            config.RequiredCoursePointIds = required.Select(point => coursePoints[point]).ToArray();
            config.RequiredCourseLayoutId = config.RequiredCoursePointIds.Length == 0 ? null : layouts[pointFacilities[required[0]]];
            await StatisticsConfigVersioning.ApplyFromAsync(db, before, StatisticsConfigVersioning.Snapshot(config.StatisticsItem, config),
                validFrom, DateTime.UtcNow, CancellationToken.None);
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

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
        while (true)
        {
            while (queue.TryDequeue(out var envelope))
            {
                var times = redelivery == Redelivery.Immediate ? 2 : 1;
                for (var i = 0; i < times; i++) await ConsumeAsync(envelope);
            }
            // The recompute worker, one step at a time. A failed recompute backs off, so it ends the drain.
            await using var db = StatisticsDb();
            var result = await RecomputeService(db).ProcessNextAsync(CancellationToken.None);
            if (result.Outcome == RecomputeStepOutcome.Failed)
                deadLetters.Add($"Recompute of {result.TransponderId}: {result.Error?.GetType().Name}: {result.Error?.Message}");
            if (result.Outcome is RecomputeStepOutcome.Idle or RecomputeStepOutcome.Failed && queue.Count == 0) return;
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

    public async Task<IReadOnlyList<ManualDecisionReviewRow>> ManualDecisionReviewsAsync()
    {
        Dictionary<Guid, string> riders;
        await using (var db = TimingDb())
        {
            var names = transponderIds.ToDictionary(pair => pair.Value, pair => pair.Key);
            riders = (await db.Set<Transponder>().AsNoTracking().ToListAsync())
                .ToDictionary(t => t.Id, t => names.GetValueOrDefault(t.SystemId, t.SystemId));
        }
        await using var stats = StatisticsDb();
        return (await stats.Set<ManualDecisionReview>().AsNoTracking().OrderBy(r => r.CreatedAtUtc).ThenBy(r => r.Id).ToListAsync())
            .Select(r => new ManualDecisionReviewRow(r.EntryId, Name(riders, r.TransponderId), Name(itemNames, r.StatisticsItemId), r.Note ?? ""))
            .ToList();
    }

    // ---- Message handling -------------------------------------------------------------------

    private sealed record Envelope(object Message, IDictionary<string, object> Headers, int Attempt = 1);

    private Task EnqueueAsync(CourseConfigChangedV1 message, CancellationToken cancellationToken)
    {
        // Facilities enqueues in its edit transaction; a rolled-back edit is never published here because the
        // services only call this immediately before committing.
        Enqueue(message);
        return Task.CompletedTask;
    }

    private void Enqueue(object message, IDictionary<string, object>? headers = null)
    {
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
            case CourseConfigChangedV1 changed:
            {
                await using var db = StatisticsDb();
                await new CourseConfigChangedHandler(db, History(), new StatisticsRecomputeSignal(),
                    NullLogger<CourseConfigChangedHandler>.Instance).OnHandle(changed, CancellationToken.None);
                break;
            }
            case PassingRecordedV1 recorded:
            {
                await using var db = StatisticsDb();
                await new PassingRecordedHandler(db, new StatisticsRecomputeSignal(), new StatisticsMetrics(Meters),
                    NullLogger<PassingRecordedHandler>.Instance).OnHandle(recorded, CancellationToken.None);
                break;
            }
            case InstallationFacilityChangedV1 changed:
            {
                await using var db = StatisticsDb();
                await new InstallationFacilityChangedHandler(db, History(), new StatisticsRecomputeSignal(),
                    NullLogger<InstallationFacilityChangedHandler>.Instance).OnHandle(changed, CancellationToken.None);
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

    private StatisticsConfigurationHistory History() => new(Cache, new DatabaseConfigurationSource(this));

    private StatisticsRecomputeService RecomputeService(StatisticsDbContext db) => new(db, History(),
        new StatisticsOwnershipService(db, new NoOwners()),
        Microsoft.Extensions.Options.Options.Create(new StatisticsRecomputeOptions()), new StatisticsMetrics(Meters),
        NullLogger<StatisticsRecomputeService>.Instance);

    /// <summary>The owners' history read straight from their databases, where production reads their APIs.</summary>
    private sealed class DatabaseConfigurationSource(CurrentPipelineWorld world) : IStatisticsConfigurationSource
    {
        public async Task<InstallationFacilityHistory> GetInstallationAsync(Guid installationId, CancellationToken cancellationToken)
        {
            await using var db = world.TimingDb();
            var assignments = await db.Set<InstallationFacilityAssignment>().AsNoTracking()
                .Where(a => a.InstallationId == installationId).ToListAsync(cancellationToken);
            return new()
            {
                InstallationId = installationId,
                Assignments = assignments.Select(a => new InstallationFacilityVersion
                {
                    FacilityId = a.FacilityId,
                    ValidFromUtc = a.ValidFromUtc is { } from ? Utc(from) : null,
                    ValidToUtc = a.ValidToUtc is { } to ? Utc(to) : null
                }).ToList()
            };
        }

        public async Task<FacilityCourseHistory> GetFacilityCourseAsync(Guid facilityId, CancellationToken cancellationToken)
        {
            await using var db = world.FacilityDb();
            var history = await new FacilityCourseHistoryReader(db).ReadAsync(facilityId, cancellationToken);
            return new()
            {
                FacilityId = facilityId,
                Layouts = history?.Layouts.Select(l => new CourseLayoutVersion
                {
                    LayoutId = l.LayoutId,
                    Assignments = l.Assignments.Select(a => new EffectiveVersion
                        { ValidFromUtc = a.ValidFromUtc?.UtcDateTime, ValidToUtc = a.ValidToUtc?.UtcDateTime }).ToList(),
                    Revisions = l.Revisions.Select(r => new CourseLayoutRevisionVersion
                    {
                        RevisionId = r.RevisionId, ValidFromUtc = r.ValidFromUtc?.UtcDateTime, ValidToUtc = r.ValidToUtc?.UtcDateTime,
                        Segments = r.Segments.Select(s => new CourseSegmentVersion
                            { Order = s.Order, StartPointId = s.StartPointId, EndPointId = s.EndPointId, Length = s.Length }).ToList()
                    }).ToList()
                }).ToList() ?? [],
                Associations = history?.Associations.Select(a => new TimingAssociationVersion
                {
                    TimingPointId = a.TimingPointId, CoursePointId = a.CoursePointId,
                    ValidFromUtc = a.ValidFromUtc?.UtcDateTime, ValidToUtc = a.ValidToUtc?.UtcDateTime
                }).ToList() ?? []
            };
        }
    }

    /// <summary>Timing point → facility, as Timing's reference endpoint answers it (through the installation).</summary>
    private sealed class TimingReferences(CurrentPipelineWorld world) : IAdministrationReferences
    {
        public async Task<EntityReference?> GetAsync(string module, string kind, Guid id, CancellationToken cancellationToken)
        {
            if (module != "timing" || kind != "timingpoints") throw new NotSupportedException($"{module}/{kind}");
            await using var db = world.TimingDb();
            var facility = await db.Set<TimingPoint>().AsNoTracking().Where(t => t.Id == id)
                .Select(t => (Guid?)t.Installation.Facility).SingleOrDefaultAsync(cancellationToken);
            return facility is { } found ? new EntityReference(id, found) : null;
        }

        public Task<DependencySummary> GetDependenciesAsync(string module, string kind, Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(new DependencySummary(0, ""));
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
