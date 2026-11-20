using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Module.Common;
using VeloTime.Module.Timing.Handlers;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Timing.Storage;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace VeloTime.Workers.Test;

// InstallationLayoutHandler against a real in-memory FusionCache (whose L1 hands the SAME
// object to all readers) warmed through separate DbContexts. Failures are injected only via
// an EF SaveChanges interceptor (final metadata save) or a failing eviction-operations wrapper.
public sealed class InstallationLayoutHandlerCacheTests
{
    private const string AgentId = "layout-alias-agent";

    private static InstallationLayoutEvent Layout(string description, params (string Loop, string Name)[] loops) => new(
        [new SystemConfigEvent("system-1", "cycling", description, true)],
        loops.Select((loop, index) => new LoopConfigEvent(loop.Loop, loop.Name, index + 1, "system-1")).ToList(),
        []);

    [Theory]
    [InlineData(false)] // final metadata SaveChanges throws
    [InlineData(true)]  // request canceled at final metadata SaveChanges
    public async Task FinalSaveFailureAfterIntermediateCreate_DoesNotMutateWarmCacheAndRetryApplies(bool cancel)
    {
        await using var fixture = await Fixture.CreateAsync();
        var reader = fixture.NewService();
        var cachedInstallation = (await reader.GetInstallationForAgent(AgentId))!;
        var cachedExisting = (await reader.GetTimingPoint(cachedInstallation, "existing-loop"))!;
        Assert.Same(cachedInstallation, await fixture.NewService().GetInstallationForAgent(AgentId)); // shared alias
        using var request = new CancellationTokenSource();
        var interceptor = new MetadataSaveInterceptor
        {
            OnMetadataSave = token =>
            {
                if (!cancel) throw new DbUpdateException("injected final save failure");
                request.Cancel();
                token.ThrowIfCancellationRequested();
            }
        };
        // The system description equals the message's: identical text must still not alias.
        var layout = Layout("Installed system", ("existing-loop", "Existing loop"), ("new-loop", "New loop"));

        var failure = await Record.ExceptionAsync(() => fixture.NewHandler(interceptor).OnHandle(layout, request.Token));

        Assert.True(cancel ? failure is OperationCanceledException : failure is DbUpdateException, failure?.ToString());
        Assert.Equal(1, interceptor.MetadataSaves);
        // Warm cached objects were not written through.
        Assert.Equal("", cachedInstallation.Description);
        Assert.Equal(TimingSystem.Unknown, cachedInstallation.TimingSystem);
        Assert.Empty(cachedInstallation.TimingPoints);
        Assert.Equal("", cachedExisting.Description);
        // Normal cached reads equal durable state: metadata unchanged, intermediate point committed and visible.
        var after = fixture.NewService();
        var readInstallation = (await after.GetInstallationForAgent(AgentId))!;
        Assert.Equal("", readInstallation.Description);
        Assert.Equal(TimingSystem.Unknown, readInstallation.TimingSystem);
        Assert.Equal("", (await after.GetTimingPoint(readInstallation, "existing-loop"))!.Description);
        var createdPoint = await after.GetTimingPoint(readInstallation, "new-loop");
        Assert.NotNull(createdPoint);
        Assert.Equal("", createdPoint.Description);
        Assert.Equal(fixture.InstallationId, (await after.GetInstallionForTimingPoint(createdPoint.Id)).Id);
        var persisted = await fixture.PersistedAsync();
        Assert.Equal("", persisted.Installation.Description);
        Assert.Equal(TimingSystem.Unknown, persisted.Installation.TimingSystem);
        Assert.Equal(["", ""], persisted.Points.Select(point => point.Description));

        await fixture.NewHandler().OnHandle(layout, CancellationToken.None);

        persisted = await fixture.PersistedAsync();
        Assert.Equal("Installed system", persisted.Installation.Description);
        Assert.Equal(TimingSystem.MyLaps_X2, persisted.Installation.TimingSystem);
        Assert.Equal(["Existing loop", "New loop"], persisted.Points.OrderBy(point => point.SystemId).Select(point => point.Description));
        var retried = fixture.NewService();
        readInstallation = (await retried.GetInstallationForAgent(AgentId))!;
        Assert.Equal("Installed system", readInstallation.Description);
        Assert.Equal("Existing loop", (await retried.GetTimingPoint(readInstallation, "existing-loop"))!.Description);
        Assert.Equal("New loop", (await retried.GetTimingPoint(readInstallation, "new-loop"))!.Description);
        Assert.Equal(createdPoint.Id, (await retried.GetTimingPoint(readInstallation, "new-loop"))!.Id);
        Assert.Equal("", cachedInstallation.Description);
    }

    [Fact]
    public async Task DuplicateLoopsKeepMessageOrderFirstNonBlankWinsWithOnePoint()
    {
        await using var fixture = await Fixture.CreateAsync();
        var reader = fixture.NewService();
        var cachedInstallation = (await reader.GetInstallationForAgent(AgentId))!;
        _ = await reader.GetTimingPoint(cachedInstallation, "dup-loop"); // warm the negative lookup

        await fixture.NewHandler().OnHandle(Layout("Installed system",
            ("dup-loop", ""), ("dup-loop", "First"), ("existing-loop", "Existing"), ("dup-loop", "Second")), CancellationToken.None);

        var persisted = await fixture.PersistedAsync();
        Assert.Equal(2, persisted.Points.Count);
        Assert.Equal("First", persisted.Points.Single(point => point.SystemId == "dup-loop").Description);
        Assert.Equal("Existing", persisted.Points.Single(point => point.SystemId == "existing-loop").Description);
        var after = fixture.NewService();
        var installation = (await after.GetInstallationForAgent(AgentId))!;
        Assert.Equal("First", (await after.GetTimingPoint(installation, "dup-loop"))!.Description);
        Assert.Equal("", cachedInstallation.Description);
    }

    [Fact]
    public async Task StrictEvictionFailureAfterCommit_SurfacesAndRetryRefreshesCache()
    {
        await using var fixture = await Fixture.CreateAsync();
        var reader = fixture.NewService();
        var cachedInstallation = (await reader.GetInstallationForAgent(AgentId))!;
        _ = await reader.GetTimingPoint(cachedInstallation, "existing-loop");
        var layout = Layout("Installed system", ("existing-loop", "Existing loop"));
        fixture.Operations.Fail = true;

        await Assert.ThrowsAsync<AggregateException>(() => fixture.NewHandler().OnHandle(layout, CancellationToken.None));

        Assert.True(fixture.Operations.Attempts > 0);
        var persisted = await fixture.PersistedAsync();
        Assert.Equal("Installed system", persisted.Installation.Description); // durable commit happened
        Assert.Equal("Existing loop", persisted.Points.Single().Description);
        // Eviction failed and was surfaced: the warm entry is (observably) still the old value.
        Assert.Equal("", (await fixture.NewService().GetInstallationForAgent(AgentId))!.Description);
        Assert.Equal("", cachedInstallation.Description);

        fixture.Operations.Fail = false;
        await fixture.NewHandler().OnHandle(layout, CancellationToken.None); // fill-only no-op write, still evicts

        var after = fixture.NewService();
        var installation = (await after.GetInstallationForAgent(AgentId))!;
        Assert.Equal("Installed system", installation.Description);
        Assert.Equal("Existing loop", (await after.GetTimingPoint(installation, "existing-loop"))!.Description);
    }

    private sealed class MetadataSaveInterceptor : SaveChangesInterceptor
    {
        public Action<CancellationToken>? OnMetadataSave { get; init; }
        public int MetadataSaves { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Modified))
            {
                MetadataSaves++;
                OnMetadataSave?.Invoke(cancellationToken);
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ToggleOperations(ICacheInvalidationOperations inner) : ICacheInvalidationOperations
    {
        private int attempts;
        public volatile bool Fail;
        public int Attempts => attempts;
        public bool Handles(HybridCache cache) => inner.Handles(cache);
        public bool RequiresStrictHandling(HybridCache cache) => inner.RequiresStrictHandling(cache);
        public Task RemoveAsync(string key, CancellationToken cancellationToken) => Run(() => inner.RemoveAsync(key, cancellationToken));
        public Task RemoveByTagAsync(string tag, CancellationToken cancellationToken) => Run(() => inner.RemoveByTagAsync(tag, cancellationToken));
        private Task Run(Func<Task> action)
        {
            Interlocked.Increment(ref attempts);
            return Fail ? Task.FromException(new InvalidOperationException("injected eviction outage")) : action();
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider provider;
        private readonly List<TimingDbContext> contexts = [];

        private Fixture(SqliteConnection connection, ServiceProvider provider, Guid installationId)
        {
            this.connection = connection;
            this.provider = provider;
            InstallationId = installationId;
            Cache = provider.GetRequiredService<HybridCache>();
            Operations = new ToggleOperations(new FusionCacheInvalidationOperations(
                provider.GetRequiredService<IFusionCache>(), new FusionCacheCircuitBreakerState()));
        }

        public Guid InstallationId { get; }
        public HybridCache Cache { get; }
        public ToggleOperations Operations { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var installationId = Guid.NewGuid();
            await using (var seed = new TimingDbContext(new DbContextOptionsBuilder<TimingDbContext>().UseSqlite(connection).Options))
            {
                await seed.Database.EnsureCreatedAsync();
                seed.Add(new Installation { Id = installationId, AgentId = AgentId, Description = "", TimingSystem = TimingSystem.Unknown });
                seed.Add(new TimingPoint { Id = Guid.NewGuid(), InstallationId = installationId, Installation = null!, SystemId = "existing-loop", Description = "" });
                await seed.SaveChangesAsync();
            }
            var services = new ServiceCollection();
            services.AddFusionCache()
                .WithSerializer(new FusionCacheSystemTextJsonSerializer(new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve }))
                .AsHybridCache();
            return new Fixture(connection, services.BuildServiceProvider(), installationId);
        }

        private TimingDbContext NewContext(SaveChangesInterceptor? interceptor = null)
        {
            var builder = new DbContextOptionsBuilder<TimingDbContext>().UseSqlite(connection);
            if (interceptor is not null) builder.AddInterceptors(interceptor);
            var context = new TimingDbContext(builder.Options);
            contexts.Add(context);
            return context;
        }

        public InstallationService NewService() => new(NewContext(), Cache, null, Operations);

        public InstallationLayoutHandler NewHandler(SaveChangesInterceptor? interceptor = null)
        {
            var storage = NewContext(interceptor);
            return new InstallationLayoutHandler(new InstallationService(storage, Cache, null, Operations), storage,
                NullLogger<InstallationLayoutHandler>.Instance, Cache, Operations)
            {
                Context = WorkerHandlerTests.TestConsumerContext.Create(new Dictionary<string, object> { ["AgentId"] = AgentId })
            };
        }

        public async Task<(Installation Installation, List<TimingPoint> Points)> PersistedAsync()
        {
            await using var verify = new TimingDbContext(new DbContextOptionsBuilder<TimingDbContext>().UseSqlite(connection).Options);
            return (await verify.Set<Installation>().AsNoTracking().SingleAsync(),
                await verify.Set<TimingPoint>().AsNoTracking().OrderBy(point => point.SystemId).ToListAsync());
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var context in contexts) await context.DisposeAsync();
            await provider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
