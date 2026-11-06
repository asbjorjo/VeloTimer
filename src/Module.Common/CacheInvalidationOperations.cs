using Microsoft.Extensions.Caching.Hybrid;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Events;
using ZiggyCreatures.Caching.Fusion.MicrosoftHybridCache;
using ZiggyCreatures.Caching.Fusion.Plugins;

namespace VeloTime.Module.Common;

/// <summary>
/// Executes cache invalidations with synchronous, operation-scoped FusionCache
/// failure behavior. Normal cache reads retain the application's configured defaults.
/// </summary>
public interface ICacheInvalidationOperations
{
    bool Handles(HybridCache cache);
    bool RequiresStrictHandling(HybridCache cache);
    Task RemoveAsync(string key, CancellationToken cancellationToken);
    Task RemoveByTagAsync(string tag, CancellationToken cancellationToken);
}

public sealed class FusionCacheCircuitBreakerState
{
    private int _distributedCircuitOpen;
    private int _backplaneCircuitOpen;

    public void ObserveDistributed(FusionCacheCircuitBreakerChangeEventArgs args) =>
        Volatile.Write(ref _distributedCircuitOpen, args.IsClosed ? 0 : 1);

    public void ObserveBackplane(FusionCacheCircuitBreakerChangeEventArgs args) =>
        Volatile.Write(ref _backplaneCircuitOpen, args.IsClosed ? 0 : 1);

    public void EnsureCircuitsClosed()
    {
        if (Volatile.Read(ref _distributedCircuitOpen) != 0 || Volatile.Read(ref _backplaneCircuitOpen) != 0)
            throw new InvalidOperationException("FusionCache eviction was skipped because its distributed-cache or backplane circuit breaker is open.");
    }
}

/// <summary>Starts tracking breaker transitions when FusionCache itself is first created.</summary>
public sealed class FusionCacheCircuitBreakerPlugin(FusionCacheCircuitBreakerState state) : IFusionCachePlugin
{
    public void Start(IFusionCache cache)
    {
        cache.Events.Distributed.CircuitBreakerChange += OnDistributedCircuitBreakerChange;
        cache.Events.Backplane.CircuitBreakerChange += OnBackplaneCircuitBreakerChange;
    }

    public void Stop(IFusionCache cache)
    {
        cache.Events.Distributed.CircuitBreakerChange -= OnDistributedCircuitBreakerChange;
        cache.Events.Backplane.CircuitBreakerChange -= OnBackplaneCircuitBreakerChange;
    }

    private void OnDistributedCircuitBreakerChange(object? sender, FusionCacheCircuitBreakerChangeEventArgs args) =>
        state.ObserveDistributed(args);

    private void OnBackplaneCircuitBreakerChange(object? sender, FusionCacheCircuitBreakerChangeEventArgs args) =>
        state.ObserveBackplane(args);
}

public sealed class FusionCacheInvalidationOperations(IFusionCache cache, FusionCacheCircuitBreakerState circuitBreakers) : ICacheInvalidationOperations
{
    public bool Handles(HybridCache hybridCache) =>
        hybridCache is FusionHybridCache adapter && ReferenceEquals(adapter.InnerFusionCache, cache);

    public bool RequiresStrictHandling(HybridCache hybridCache) => hybridCache is FusionHybridCache;

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        circuitBreakers.EnsureCircuitsClosed();
        await cache.RemoveAsync(key, CreateEvictionOptions(), cancellationToken);
        circuitBreakers.EnsureCircuitsClosed();
    }

    public async Task RemoveByTagAsync(string tag, CancellationToken cancellationToken)
    {
        circuitBreakers.EnsureCircuitsClosed();
        await cache.RemoveByTagAsync(tag, CreateEvictionOptions(), cancellationToken);
        circuitBreakers.EnsureCircuitsClosed();
    }

    private FusionCacheEntryOptions CreateEvictionOptions()
    {
        var options = cache.DefaultEntryOptions.Duplicate();
        options.AllowBackgroundDistributedCacheOperations = false;
        options.AllowBackgroundBackplaneOperations = false;
        options.ReThrowDistributedCacheExceptions = true;
        options.ReThrowBackplaneExceptions = true;
        return options;
    }

}
