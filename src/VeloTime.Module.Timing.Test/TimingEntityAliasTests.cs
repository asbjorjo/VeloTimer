using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Common;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Timing.Storage;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace VeloTime.Module.Timing.Test;

// Real in-memory FusionCache L1 returns the same object reference to every reader, so a
// writer mutating a cached entity would leak an uncommitted value to normal reads. Each
// service below owns a separate DbContext; only the cache and database are shared.
public sealed class TimingEntityAliasTests
{
    private static readonly DateTime Old = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T = new(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false, false)] // aliased object is the one created by the cache factory
    [InlineData(true, false)]  // aliased object is returned from a later cache hit
    [InlineData(true, true)]   // writer context already tracks an instance with the same ID
    public async Task FailedLastSeenSave_LeavesCachedReadOldAndRetryPersists(bool fromHit, bool writerTracksSameId)
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.NewService().GetInstallationForAgent(Fixture.AgentId);
        var alias = fromHit ? await fixture.NewService().GetInstallationForAgent(Fixture.AgentId) : created;
        Assert.NotNull(alias);
        if (fromHit) Assert.Same(created, alias);

        var interceptor = new BoundaryInterceptor { BeforeSave = () => throw new DbUpdateException("injected save failure") };
        await using var writerContext = fixture.NewContext(interceptor);
        if (writerTracksSameId)
            await writerContext.Set<Installation>().SingleAsync(item => item.Id == alias.Id);
        var writer = fixture.NewService(writerContext);

        await Assert.ThrowsAsync<DbUpdateException>(() => writer.UpdateLastSeenAsync(alias, T));

        Assert.Equal(Old, alias.LastSeen);
        Assert.Equal(Old, (await fixture.NewService().GetInstallationForAgent(Fixture.AgentId))!.LastSeen);
        Assert.Equal(Old, await fixture.PersistedLastSeenAsync());

        await using var retryContext = fixture.NewContext();
        await fixture.NewService(retryContext).UpdateLastSeenAsync(alias, T);

        Assert.Equal(T, await fixture.PersistedLastSeenAsync());
        Assert.Equal(T, (await fixture.NewService().GetInstallationForAgent(Fixture.AgentId))!.LastSeen);
        Assert.Equal(Old, alias.LastSeen); // the previously cached instance was never written through
    }

    [Fact]
    public async Task FailedLastSeenSave_RetryOnSameWriterContextPersists()
    {
        await using var fixture = await Fixture.CreateAsync();
        var alias = (await fixture.NewService().GetInstallationForAgent(Fixture.AgentId))!;
        var fail = true;
        var interceptor = new BoundaryInterceptor { BeforeSave = () => { if (fail) throw new DbUpdateException("injected save failure"); } };
        await using var writerContext = fixture.NewContext(interceptor);
        var writer = fixture.NewService(writerContext);

        await Assert.ThrowsAsync<DbUpdateException>(() => writer.UpdateLastSeenAsync(alias, T));
        Assert.Equal(Old, (await fixture.NewService().GetInstallationForAgent(Fixture.AgentId))!.LastSeen);

        fail = false;
        await writer.UpdateLastSeenAsync(alias, T);

        Assert.Equal(T, await fixture.PersistedLastSeenAsync());
        Assert.Equal(T, (await fixture.NewService().GetInstallationForAgent(Fixture.AgentId))!.LastSeen);
    }

    [Fact]
    public async Task CanceledLastSeenSave_LeavesCachedObjectAndDatabaseOld()
    {
        await using var fixture = await Fixture.CreateAsync();
        var alias = (await fixture.NewService().GetInstallationForAgent(Fixture.AgentId))!;
        using var request = new CancellationTokenSource();
        await using var writerContext = fixture.NewContext(new BoundaryInterceptor { BeforeSave = request.Cancel });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.NewService(writerContext).UpdateLastSeenAsync(alias, T, request.Token));

        Assert.Equal(Old, alias.LastSeen);
        Assert.Equal(Old, (await fixture.NewService().GetInstallationForAgent(Fixture.AgentId))!.LastSeen);
        Assert.Equal(Old, await fixture.PersistedLastSeenAsync());
    }

    [Fact]
    public async Task CreateTimingPoint_ReturnsIndependentWritableInstanceOnCreateAndExistingPaths()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cachedInstallation = (await fixture.NewService().GetInstallationForAgent(Fixture.AgentId))!;
        await using var writerContext = fixture.NewContext();
        var writer = fixture.NewService(writerContext);

        var created = await writer.CreateTimingPoint(cachedInstallation, "loop-1");
        var cachedPoint = await fixture.NewService().GetTimingPoint(cachedInstallation, "loop-1");
        var existing = await writer.CreateTimingPoint(cachedInstallation, "loop-1");

        Assert.NotNull(cachedPoint);
        Assert.NotSame(cachedPoint, created);
        Assert.NotSame(cachedPoint, existing);
        Assert.NotSame(cachedInstallation, created.Installation);
        Assert.NotSame(cachedInstallation, existing.Installation);
        Assert.Empty(cachedInstallation.TimingPoints);
        existing.Description = "uncommitted";
        Assert.Equal("", (await fixture.NewService().GetTimingPoint(cachedInstallation, "loop-1"))!.Description);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string AgentId = "alias-agent";
        private readonly SqliteConnection connection;
        private readonly ServiceProvider provider;
        private readonly List<TimingDbContext> contexts = [];

        private Fixture(SqliteConnection connection, ServiceProvider provider)
        {
            this.connection = connection;
            this.provider = provider;
            Cache = provider.GetRequiredService<HybridCache>();
            Operations = new FusionCacheInvalidationOperations(provider.GetRequiredService<IFusionCache>(), new FusionCacheCircuitBreakerState());
        }

        public HybridCache Cache { get; }
        public ICacheInvalidationOperations Operations { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using (var seed = new TimingDbContext(new DbContextOptionsBuilder<TimingDbContext>().UseSqlite(connection).Options))
            {
                await seed.Database.EnsureCreatedAsync();
                seed.Add(new Installation { Id = Guid.NewGuid(), AgentId = AgentId, LastSeen = Old, TimingSystem = TimingSystem.MyLaps_X2 });
                await seed.SaveChangesAsync();
            }
            var services = new ServiceCollection();
            services.AddFusionCache()
                .WithSerializer(new FusionCacheSystemTextJsonSerializer(new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve }))
                .AsHybridCache();
            return new Fixture(connection, services.BuildServiceProvider());
        }

        public TimingDbContext NewContext(BoundaryInterceptor? interceptor = null)
        {
            var builder = new DbContextOptionsBuilder<TimingDbContext>().UseSqlite(connection);
            if (interceptor is not null) builder.AddInterceptors(interceptor);
            var context = new TimingDbContext(builder.Options);
            contexts.Add(context);
            return context;
        }

        public InstallationService NewService(TimingDbContext? context = null) =>
            new(context ?? NewContext(), Cache, null, Operations);

        public async Task<DateTime?> PersistedLastSeenAsync()
        {
            await using var verify = new TimingDbContext(new DbContextOptionsBuilder<TimingDbContext>().UseSqlite(connection).Options);
            return (await verify.Set<Installation>().AsNoTracking().SingleAsync(item => item.AgentId == AgentId)).LastSeen;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var context in contexts) await context.DisposeAsync();
            await provider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
