using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Test;

// Representative explicit-commit Timing writer (DeleteInstallationAsync). Commit
// boundaries are driven only by a test EF SaveChanges interceptor.
public sealed class TimingWriterCommitBoundaryTests
{
    [Fact]
    public async Task CancellationBeforeDurableWrite_LeavesDatabaseUnchangedAndDoesNotEvict()
    {
        using var request = new CancellationTokenSource();
        var interceptor = new BoundaryInterceptor { BeforeSave = request.Cancel };
        await using var fixture = await Fixture.CreateAsync(interceptor);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.DeleteInstallationAsync(fixture.InstallationId, request.Token));

        Assert.True(await fixture.InstallationExistsAsync());
        Assert.Empty(fixture.Cache.Operations);
    }

    [Fact]
    public async Task CancellationImmediatelyAfterCommit_StillEvictsWithIndependentPostCommitToken()
    {
        using var request = new CancellationTokenSource();
        var interceptor = new BoundaryInterceptor { AfterSave = request.Cancel };
        await using var fixture = await Fixture.CreateAsync(interceptor);

        await fixture.Service.DeleteInstallationAsync(fixture.InstallationId, request.Token);

        Assert.True(request.IsCancellationRequested);
        Assert.False(await fixture.InstallationExistsAsync());
        Assert.Contains($"tag:{TimingCacheTags.InstallationAgent(Fixture.AgentId)}", fixture.Cache.Operations);
        Assert.Contains($"tag:{TimingCacheTags.Installation(fixture.InstallationId)}", fixture.Cache.Operations);
        Assert.Contains($"key:{TimingCacheKeys.Installation(Fixture.AgentId)}", fixture.Cache.Operations);
        Assert.All(fixture.Cache.Tokens, observed =>
        {
            Assert.NotEqual(request.Token, observed.Token);
            Assert.False(observed.WasCanceledWhenCalled);
            Assert.True(observed.Token.CanBeCanceled);
        });
    }

    [Fact]
    public async Task SaveFailure_RollsBackAndDoesNotEvict()
    {
        var interceptor = new BoundaryInterceptor { BeforeSave = () => throw new DbUpdateException("injected save failure") };
        await using var fixture = await Fixture.CreateAsync(interceptor);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            fixture.Service.DeleteInstallationAsync(fixture.InstallationId, CancellationToken.None));

        Assert.True(await fixture.InstallationExistsAsync());
        Assert.Empty(fixture.Cache.Operations);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string AgentId = "commit-boundary-agent";
        private readonly SqliteConnection connection;
        private readonly TimingDbContext storage;

        private Fixture(SqliteConnection connection, TimingDbContext storage, Guid installationId)
        {
            this.connection = connection;
            this.storage = storage;
            InstallationId = installationId;
            Service = new TimingAdministrationService(storage, null!, Cache);
        }

        public Guid InstallationId { get; }
        public RecordingHybridCache Cache { get; } = new();
        public TimingAdministrationService Service { get; }

        public static async Task<Fixture> CreateAsync(BoundaryInterceptor interceptor)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var installationId = Guid.NewGuid();
            await using (var seed = new TimingDbContext(new DbContextOptionsBuilder<TimingDbContext>().UseSqlite(connection).Options))
            {
                await seed.Database.EnsureCreatedAsync();
                seed.Add(new Installation { Id = installationId, AgentId = AgentId, TimingSystem = TimingSystem.MyLaps_X2 });
                await seed.SaveChangesAsync();
            }
            var storage = new TimingDbContext(new DbContextOptionsBuilder<TimingDbContext>()
                .UseSqlite(connection).AddInterceptors(interceptor).Options);
            return new Fixture(connection, storage, installationId);
        }

        public async Task<bool> InstallationExistsAsync()
        {
            await using var verify = new TimingDbContext(new DbContextOptionsBuilder<TimingDbContext>().UseSqlite(connection).Options);
            return await verify.Set<Installation>().AnyAsync(item => item.Id == InstallationId);
        }

        public async ValueTask DisposeAsync()
        {
            await storage.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}

internal sealed class BoundaryInterceptor : SaveChangesInterceptor
{
    public Action? BeforeSave { get; init; }
    public Action? AfterSave { get; init; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        BeforeSave?.Invoke();
        return ValueTask.FromResult(result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        AfterSave?.Invoke();
        return ValueTask.FromResult(result);
    }
}

internal sealed record ObservedToken(CancellationToken Token, bool WasCanceledWhenCalled);

internal sealed class RecordingHybridCache : HybridCache
{
    public List<string> Operations { get; } = [];
    public List<ObservedToken> Tokens { get; } = [];

    public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state,
        Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) =>
        factory(state, cancellationToken);

    public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        Operations.Add($"key:{key}");
        Tokens.Add(new ObservedToken(cancellationToken, cancellationToken.IsCancellationRequested));
        return ValueTask.CompletedTask;
    }

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        Operations.Add($"tag:{tag}");
        Tokens.Add(new ObservedToken(cancellationToken, cancellationToken.IsCancellationRequested));
        return ValueTask.CompletedTask;
    }
}
