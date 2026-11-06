using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using VeloTime.Module.Common;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Events;
using ZiggyCreatures.Caching.Fusion.Plugins;

namespace VeloTime.Module.Facilities.Api.Test;

// Production AddModuleCache (FusionCache + Redis distributed cache + Redis backplane + the
// production breaker plugin/operation-scoped eviction) with failures injected strictly BENEATH
// FusionCache: the registered IDistributedCache, or the backplane's Redis configuration. The
// HybridCache adapter is not replaced. Uses a dedicated Redis logical DB; nothing is flushed.
public sealed class FacilitiesEvictionProviderFailureTests
{
    private const string Tag = "Facilities.Relationships";

    [RedisCacheFact]
    public async Task DistributedCacheFailure_DefaultsSuppressButWriterCommitsSurfacesFailureAndAttemptsBothStores()
    {
        var failing = new FailingDistributedCacheSwitch();
        await using var host = CreateHost(services => DecorateDistributedCache(services, failing));
        await host.InitializeDatabaseAsync();
        var facility = await SeedAsync(host);
        using var client = host.CreateClient();
        var cache = host.Services.GetRequiredService<HybridCache>();
        Assert.Contains("FusionHybridCache", cache.GetType().Name);

        failing.Fail = true;
        // Characterization: the global default (background, non-rethrowing) eviction reports success.
        await cache.RemoveByTagAsync(Tag);
        var callsBeforeWriter = failing.Calls;
        host.OutputCache.Reset();

        var response = await client.PutAsJsonAsync($"/api/facilities/facility/{facility.Id}", new SaveFacilityRequest("Committed"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("injected distributed-cache outage", await response.Content.ReadAsStringAsync());
        Assert.Equal("Committed", await ReadNameAsync(host, facility.Id));
        Assert.True(failing.Calls > callsBeforeWriter, "The writer eviction did not reach the distributed cache.");
        Assert.Equal(1, host.OutputCache.EvictionCalls);
    }

    [RedisCacheFact]
    public async Task OpenDistributedCircuitBreaker_SkippedEvictionIsSurfacedAfterCommitAndOutputStillAttempted()
    {
        var failing = new FailingDistributedCacheSwitch();
        await using var host = CreateHost(services =>
        {
            DecorateDistributedCache(services, failing);
            // Production defaults have no breaker; enable one only to exercise the plugin's skip path.
            services.ConfigureAll<FusionCacheOptions>(options => options.DistributedCacheCircuitBreakerDuration = TimeSpan.FromMinutes(2));
        });
        await host.InitializeDatabaseAsync();
        var facility = await SeedAsync(host);
        using var client = host.CreateClient();
        var cache = host.Services.GetRequiredService<HybridCache>();

        failing.Fail = true;
        await cache.SetAsync($"breaker-trip-{Guid.NewGuid():N}", 1); // default options: failure suppressed, breaker opens
        failing.Fail = false; // provider healthy again, but breaker is still open
        var callsBeforeWriter = failing.Calls;
        host.OutputCache.Reset();

        var response = await client.PutAsJsonAsync($"/api/facilities/facility/{facility.Id}", new SaveFacilityRequest("Committed"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("circuit breaker is open", await response.Content.ReadAsStringAsync());
        Assert.Equal("Committed", await ReadNameAsync(host, facility.Id));
        Assert.Equal(callsBeforeWriter, failing.Calls); // eviction skipped by breaker, not attempted
        Assert.Equal(1, host.OutputCache.EvictionCalls);
    }

    // Deterministic ordering check for the breaker observer (no sleeps). A test plugin registered
    // in DI subscribes to Distributed.CircuitBreakerChange BEFORE the production plugin. If the event
    // is raised asynchronously (EnableSyncEventHandlersExecution = false), FusionCache runs the
    // handler list on a pool thread: the gate handler then holds back the production observer until
    // the writer has responded, so a strict eviction during a really-open breaker would be skipped
    // and reported as success -> this test fails every time. If the event is raised synchronously
    // (the supported EnableSyncEventHandlersExecution = true option), the handlers run inline inside
    // the tripping cache call (a FusionCache DistributedCacheAccessor frame is on the stack), the
    // gate does not wait, and the observer's flag is set before the tripping call returns.
    [RedisCacheFact]
    public async Task BreakerOpenedByOrdinaryFailure_IsTrackedBeforeTheCallReturns_StrictWriterFailsAndAttemptsOutput()
    {
        var failing = new FailingDistributedCacheSwitch();
        var gate = new BreakerEventGate();
        await using var host = CreateHost(services =>
        {
            DecorateDistributedCache(services, failing);
            services.ConfigureAll<FusionCacheOptions>(options => options.DistributedCacheCircuitBreakerDuration = TimeSpan.FromMinutes(2));
        });
        await host.InitializeDatabaseAsync();
        var facility = await SeedAsync(host);
        using var client = host.CreateClient();
        var cache = host.Services.GetRequiredService<HybridCache>();
        var breakers = host.Services.GetRequiredService<FusionCacheCircuitBreakerState>();
        gate.Breakers = breakers;
        // Put the gate ahead of the SAME production plugin instance: stop it (unsubscribes its own
        // delegates), subscribe the gate, then restart it. Only subscription order changes.
        var fusion = host.Services.GetRequiredService<IFusionCache>();
        var production = Assert.Single(PluginsOf(fusion).OfType<FusionCacheCircuitBreakerPlugin>());
        production.Stop(fusion);
        gate.Start(fusion);
        production.Start(fusion);

        try
        {
            failing.Fail = true;
            await cache.SetAsync($"breaker-trip-{Guid.NewGuid():N}", 1); // ordinary op: suppressed failure opens the real breaker
            gate.TripReturned = true;
            var trackedAtReturn = Record.Exception(breakers.EnsureCircuitsClosed) is InvalidOperationException;
            failing.Fail = false;
            var callsBeforeWriter = failing.Calls;
            host.OutputCache.Reset();

            var response = await client.PutAsJsonAsync($"/api/facilities/facility/{facility.Id}", new SaveFacilityRequest("Committed"));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(1, gate.OpenEvents);
            Assert.True(gate.ObserverNotYetRunAtGateEntry, "Gate handler must precede the production observer.");
            Assert.False(gate.EnteredAfterTripReturned, "Breaker-change handlers ran after the tripping call returned (asynchronous event execution).");
            Assert.True(gate.RanInline, "Breaker-change handlers were not executed inline in the tripping FusionCache call.");
            Assert.True(trackedAtReturn, "Production breaker state was not open when the tripping call returned.");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Contains("circuit breaker is open", body);
            Assert.Equal("Committed", await ReadNameAsync(host, facility.Id));
            Assert.Equal(callsBeforeWriter, failing.Calls);
            Assert.Equal(1, host.OutputCache.EvictionCalls);
        }
        finally
        {
            gate.Release();
        }
    }

    [RedisCacheFact]
    public async Task BackplaneOutage_WriterCommitsSurfacesFailureAndAttemptsBothStores()
    {
        await using var host = CreateHost(services =>
        {
            var descriptor = services.Last(item => item.ServiceType == typeof(IConnectionMultiplexer));
            services.Remove(descriptor);
            services.AddSingleton<IConnectionMultiplexer>(provider =>
                MultiplexerProxy.Create(Resolve<IConnectionMultiplexer>(provider, descriptor),
                    "127.0.0.1:1,abortConnect=true,connectTimeout=250,connectRetry=0"));
        });
        await host.InitializeDatabaseAsync();
        var facility = await SeedAsync(host);
        using var client = host.CreateClient();
        // Characterization: the global default (background, non-rethrowing) eviction reports success.
        await host.Services.GetRequiredService<HybridCache>().RemoveByTagAsync(Tag);
        host.OutputCache.Reset();

        var response = await client.PutAsJsonAsync($"/api/facilities/facility/{facility.Id}", new SaveFacilityRequest("Committed"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("FusionCacheBackplaneException", body);
        Assert.DoesNotContain("injected distributed-cache outage", body);
        Assert.Equal("Committed", await ReadNameAsync(host, facility.Id));
        Assert.Equal(1, host.OutputCache.EvictionCalls);
    }

    private static FacilitiesApiFactory CreateHost(Action<IServiceCollection> configure) => new()
    {
        UseProductionRedisCache = true,
        RedisConnectionString = $"{Environment.GetEnvironmentVariable("VELOTIME_TEST_REDIS")},defaultDatabase=13",
        ConfigureTestServices = configure
    };

    private static void DecorateDistributedCache(IServiceCollection services, FailingDistributedCacheSwitch failing)
    {
        var descriptor = services.Last(item => item.ServiceType == typeof(IDistributedCache));
        services.Remove(descriptor);
        services.AddSingleton<IDistributedCache>(provider =>
            new FailingDistributedCache(Resolve<IDistributedCache>(provider, descriptor), failing));
    }

    private static T Resolve<T>(IServiceProvider provider, ServiceDescriptor descriptor) where T : class =>
        (T)(descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(provider)
            ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));

    private static async Task<Facility> SeedAsync(FacilitiesApiFactory host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
        var facility = new Facility { Id = Guid.NewGuid(), Name = "Before" };
        storage.Add(facility);
        await storage.SaveChangesAsync();
        return facility;
    }

    private static async Task<string> ReadNameAsync(FacilitiesApiFactory host, Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
        return (await storage.Set<Facility>().AsNoTracking().SingleAsync(item => item.Id == id)).Name;
    }

    private static IEnumerable<IFusionCachePlugin> PluginsOf(IFusionCache cache)
    {
        // FusionCache does not expose its started plugins; read the private collection.
        for (var type = cache.GetType(); type is not null; type = type.BaseType)
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                if (field.GetValue(cache) is IEnumerable<IFusionCachePlugin> plugins)
                    return plugins.ToArray();
        throw new InvalidOperationException($"No plugin collection found on {cache.GetType()}.");
    }

    private sealed class BreakerEventGate : IFusionCachePlugin
    {
        private readonly ManualResetEventSlim released = new(false);
        private int openEvents;
        public FusionCacheCircuitBreakerState? Breakers;
        public volatile bool TripReturned;
        public int OpenEvents => Volatile.Read(ref openEvents);
        public bool ObserverNotYetRunAtGateEntry { get; private set; }
        public bool EnteredAfterTripReturned { get; private set; }
        public bool RanInline { get; private set; }

        public void Start(IFusionCache cache)
        {
            cache.Events.Distributed.CircuitBreakerChange += OnChange;
        }

        public void Stop(IFusionCache cache) => cache.Events.Distributed.CircuitBreakerChange -= OnChange;

        public void Release() => released.Set();

        private void OnChange(object? sender, FusionCacheCircuitBreakerChangeEventArgs args)
        {
            if (args.IsClosed) return;
            Interlocked.Increment(ref openEvents);
            EnteredAfterTripReturned = TripReturned;
            ObserverNotYetRunAtGateEntry = Breakers is not null && Record.Exception(Breakers.EnsureCircuitsClosed) is null;
            RanInline = new System.Diagnostics.StackTrace().GetFrames().Any(frame =>
                frame.GetMethod()?.DeclaringType?.FullName?.Contains("DistributedCacheAccessor", StringComparison.Ordinal) == true);
            // Never block an inline (synchronous) raise: that would block the tripping call itself.
            // The finite wait is only a hang guard; the test releases the gate in finally.
            if (!RanInline) released.Wait(TimeSpan.FromSeconds(30));
        }
    }

    private sealed class FailingDistributedCacheSwitch
    {
        private int calls;
        public volatile bool Fail;
        public int Calls => Volatile.Read(ref calls);
        public void Observe()
        {
            Interlocked.Increment(ref calls);
            if (Fail) throw new InvalidOperationException("injected distributed-cache outage");
        }
    }

    private sealed class FailingDistributedCache(IDistributedCache inner, FailingDistributedCacheSwitch failing) : IDistributedCache
    {
        public byte[]? Get(string key) { failing.Observe(); return inner.Get(key); }
        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default) { failing.Observe(); return await inner.GetAsync(key, token); }
        public void Refresh(string key) { failing.Observe(); inner.Refresh(key); }
        public async Task RefreshAsync(string key, CancellationToken token = default) { failing.Observe(); await inner.RefreshAsync(key, token); }
        public void Remove(string key) { failing.Observe(); inner.Remove(key); }
        public async Task RemoveAsync(string key, CancellationToken token = default) { failing.Observe(); await inner.RemoveAsync(key, token); }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { failing.Observe(); inner.Set(key, value, options); }
        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) { failing.Observe(); await inner.SetAsync(key, value, options, token); }
    }

    // Forwards everything to the real multiplexer except Configuration, which the production
    // backplane factory reads to open its own connection: only the backplane sees an outage.
    public class MultiplexerProxy : DispatchProxy
    {
        private IConnectionMultiplexer inner = null!;
        private string configuration = "";

        public static IConnectionMultiplexer Create(IConnectionMultiplexer inner, string configuration)
        {
            var proxy = Create<IConnectionMultiplexer, MultiplexerProxy>();
            var state = (MultiplexerProxy)(object)proxy;
            state.inner = inner;
            state.configuration = configuration;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == "get_Configuration") return configuration;
            try { return targetMethod.Invoke(inner, args); }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }
    }
}
