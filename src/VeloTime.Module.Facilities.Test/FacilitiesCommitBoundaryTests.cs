using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Service;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Test;

// Representative Facilities writer (CreateCourseLayout) plus the domain helper.
public sealed class FacilitiesCommitBoundaryTests
{
    [Fact]
    public async Task CancellationBeforeDurableWrite_LeavesDatabaseUnchangedAndDoesNotEvict()
    {
        using var request = new CancellationTokenSource();
        await using var fixture = await Fixture.CreateAsync(new BoundaryInterceptor { BeforeSave = request.Cancel });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.CreateCourseLayout(fixture.NewLayout(), request.Token));

        Assert.Equal(0, await fixture.LayoutCountAsync());
        Assert.Empty(fixture.Cache.Operations);
    }

    [Fact]
    public async Task CancellationImmediatelyAfterCommit_StillEvictsWithIndependentPostCommitToken()
    {
        using var request = new CancellationTokenSource();
        await using var fixture = await Fixture.CreateAsync(new BoundaryInterceptor { AfterSave = request.Cancel });

        await fixture.Service.CreateCourseLayout(fixture.NewLayout(), request.Token);

        Assert.True(request.IsCancellationRequested);
        Assert.Equal(1, await fixture.LayoutCountAsync());
        Assert.Equal([$"tag:{FacilitiesCache.RelationshipsTag}"], fixture.Cache.Operations);
        var observed = Assert.Single(fixture.Cache.Tokens);
        Assert.NotEqual(request.Token, observed.Token);
        Assert.False(observed.WasCanceledWhenCalled);
        Assert.True(observed.Token.CanBeCanceled);
    }

    [Fact]
    public async Task SaveFailure_RollsBackAndDoesNotEvict()
    {
        await using var fixture = await Fixture.CreateAsync(
            new BoundaryInterceptor { BeforeSave = () => throw new DbUpdateException("injected save failure") });

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            fixture.Service.CreateCourseLayout(fixture.NewLayout(), CancellationToken.None));

        Assert.Equal(0, await fixture.LayoutCountAsync());
        Assert.Empty(fixture.Cache.Operations);
    }

    [Fact]
    public async Task RelationshipHelperPropagatesFailureAfterAttemptWithBoundedToken()
    {
        var cache = new RecordingHybridCache { FailingTag = FacilitiesCache.RelationshipsTag };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            FacilitiesCache.InvalidateRelationshipsAsync(cache, NullLogger.Instance));

        Assert.Equal([$"tag:{FacilitiesCache.RelationshipsTag}"], cache.Operations);
        var observed = Assert.Single(cache.Tokens);
        Assert.True(observed.Token.CanBeCanceled);
        Assert.False(observed.WasCanceledWhenCalled);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly FacilityDbContext storage;
        private readonly Guid facilityId;

        private Fixture(SqliteConnection connection, FacilityDbContext storage, Guid facilityId)
        {
            this.connection = connection;
            this.storage = storage;
            this.facilityId = facilityId;
            Service = new FacilitiesService(storage, Cache);
        }

        public RecordingHybridCache Cache { get; } = new();
        public FacilitiesService Service { get; }

        public CourseLayout NewLayout() => new() { Id = Guid.NewGuid(), FacilityId = facilityId };

        public static async Task<Fixture> CreateAsync(BoundaryInterceptor interceptor)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var facilityId = Guid.NewGuid();
            await using (var seed = new FacilityDbContext(new DbContextOptionsBuilder<FacilityDbContext>().UseSqlite(connection).Options))
            {
                await seed.Database.EnsureCreatedAsync();
                seed.Add(new Facility { Id = facilityId, Name = "Boundary" });
                await seed.SaveChangesAsync();
            }
            var storage = new FacilityDbContext(new DbContextOptionsBuilder<FacilityDbContext>()
                .UseSqlite(connection).AddInterceptors(interceptor).Options);
            return new Fixture(connection, storage, facilityId);
        }

        public async Task<int> LayoutCountAsync()
        {
            await using var verify = new FacilityDbContext(new DbContextOptionsBuilder<FacilityDbContext>().UseSqlite(connection).Options);
            return await verify.Set<CourseLayout>().CountAsync();
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
    public string? FailingTag { get; init; }
    public string? FailingKey { get; init; }

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
        return key == FailingKey ? ValueTask.FromException(new InvalidOperationException("injected key failure")) : ValueTask.CompletedTask;
    }

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        Operations.Add($"tag:{tag}");
        Tokens.Add(new ObservedToken(cancellationToken, cancellationToken.IsCancellationRequested));
        return tag == FailingTag ? ValueTask.FromException(new InvalidOperationException("injected tag failure")) : ValueTask.CompletedTask;
    }
}
