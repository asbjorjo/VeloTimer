using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Test;

// Representative explicit-commit Statistics writer: PUT /admin/items/{id} (UpdateItem).
// The request is aborted (HttpContext.Abort => RequestAborted) from a test EF SaveChanges
// interceptor; the real FusionCache HybridCache is wrapped only to record eviction calls.
public sealed class StatisticsWriterCommitBoundaryTests(StatisticsApiFactory factory) : IClassFixture<StatisticsApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AbortBeforeDurableWrite_LeavesItemUnchangedAndDoesNotEvict()
    {
        await using var harness = Harness.Create(factory);
        var id = await harness.CreateItemAsync();
        harness.Boundary.Arm(BoundaryMode.AbortBeforeSave);

        await harness.UpdateAndWaitAsync(id);

        Assert.Equal(BoundaryMode.AbortBeforeSave, harness.Boundary.Fired);
        Assert.NotEqual(StatusCodes.Status200OK, harness.Probe.StatusCode);
        Assert.Equal("Original", await harness.ReadNameAsync(id));
        Assert.Empty(harness.Cache.Operations);
    }

    [Fact]
    public async Task AbortImmediatelyAfterCommit_StillEvictsWithIndependentToken()
    {
        await using var harness = Harness.Create(factory);
        var id = await harness.CreateItemAsync();
        harness.Boundary.Arm(BoundaryMode.AbortAfterSave);

        await harness.UpdateAndWaitAsync(id);

        Assert.Equal(BoundaryMode.AbortAfterSave, harness.Boundary.Fired);
        // The client has gone (TestServer reports 499); the handler itself completed without an
        // escaped exception after committing and evicting.
        Assert.Null(harness.Probe.Failure);
        Assert.Equal(499, harness.Probe.StatusCode);
        Assert.Equal("Updated", await harness.ReadNameAsync(id));
        Assert.Contains($"tag:{StatisticsCacheTags.ConfigurationDefinitions}", harness.Cache.Operations);
        Assert.All(harness.Cache.Tokens, observed =>
        {
            Assert.False(observed.WasCanceledWhenCalled);
            Assert.True(observed.Token.CanBeCanceled);
            Assert.NotEqual(harness.Probe.RequestAborted, observed.Token);
        });
    }

    [Fact]
    public async Task SaveFailure_LeavesItemUnchangedAndDoesNotEvict()
    {
        await using var harness = Harness.Create(factory);
        var id = await harness.CreateItemAsync();
        harness.Boundary.Arm(BoundaryMode.FailSave);

        await harness.UpdateAndWaitAsync(id);

        Assert.Equal(BoundaryMode.FailSave, harness.Boundary.Fired);
        Assert.Equal(StatusCodes.Status500InternalServerError, harness.Probe.StatusCode);
        Assert.Equal("Original", await harness.ReadNameAsync(id));
        Assert.Empty(harness.Cache.Operations);
    }

    public enum BoundaryMode { None, AbortBeforeSave, AbortAfterSave, FailSave }

    public sealed class BoundaryInterceptor(IHttpContextAccessor accessor) : SaveChangesInterceptor
    {
        private BoundaryMode armed;
        public BoundaryMode Fired { get; private set; }
        public void Arm(BoundaryMode mode) => armed = mode;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (armed == BoundaryMode.AbortBeforeSave) Fire(() => accessor.HttpContext!.Abort());
            if (armed == BoundaryMode.FailSave) Fire(() => throw new DbUpdateException("injected save failure"));
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (armed == BoundaryMode.AbortAfterSave) Fire(() => accessor.HttpContext!.Abort());
            return ValueTask.FromResult(result);
        }

        private void Fire(Action action)
        {
            Fired = armed;
            armed = BoundaryMode.None;
            action();
        }
    }

    public sealed class CompletionProbe
    {
        private TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? Failure { get; private set; }
        public CancellationToken RequestAborted { get; private set; }
        public int StatusCode { get; private set; }
        public Task Completed => completion.Task;

        public void Reset()
        {
            Failure = null;
            StatusCode = 0;
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async Task RunAsync(HttpContext context, RequestDelegate next)
        {
            RequestAborted = context.RequestAborted;
            try { await next(context); }
            catch (Exception exception) { Failure = exception; throw; }
            finally { StatusCode = context.Response.StatusCode; completion.TrySetResult(); }
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly WebApplicationFactory<Program> host;
        private readonly HttpClient client;

        private Harness(WebApplicationFactory<Program> host)
        {
            this.host = host;
            client = host.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", "statistics-admin");
        }

        public BoundaryInterceptor Boundary => host.Services.GetRequiredService<BoundaryInterceptor>();
        public CompletionProbe Probe => host.Services.GetRequiredService<CompletionProbe>();
        public RecordingHybridCache Cache => (RecordingHybridCache)host.Services.GetRequiredService<HybridCache>();

        public static Harness Create(StatisticsApiFactory factory) => new(factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpContextAccessor();
                services.AddSingleton<BoundaryInterceptor>();
                services.AddSingleton<CompletionProbe>();
                services.ConfigureDbContext<StatisticsDbContext>((provider, options) =>
                    options.AddInterceptors(provider.GetRequiredService<BoundaryInterceptor>()));
                var inner = services.Last(descriptor => descriptor.ServiceType == typeof(HybridCache));
                foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(HybridCache)).ToList()) services.Remove(descriptor);
                services.AddSingleton<HybridCache>(provider => new RecordingHybridCache(Resolve(provider, inner)));
                services.AddTransient<IStartupFilter, ProbeStartupFilter>();
            })));

        public async Task<Guid> CreateItemAsync()
        {
            var created = await client.PostAsJsonAsync("/api/statistics/admin/items", new SaveStatisticsItemRequest("Original", "", 250));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Cache.Operations.Clear();
            Cache.Tokens.Clear();
            return (await created.Content.ReadFromJsonAsync<StatisticsItemDTO>())!.Id;
        }

        public async Task UpdateAndWaitAsync(Guid id)
        {
            Probe.Reset();
            try
            {
                using var response = await client.PutAsJsonAsync($"/api/statistics/admin/items/{id}", new SaveStatisticsItemRequest("Updated", "", 250));
            }
            catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or IOException)
            {
                // Aborted requests surface client-side; server completion is observed by the probe.
            }
            await Probe.Completed.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public async Task<string> ReadNameAsync(Guid id)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var storage = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
            return (await storage.Set<StatisticsItem>().AsNoTracking().SingleAsync(item => item.Id == id)).Name;
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await host.DisposeAsync();
        }

        private static HybridCache Resolve(IServiceProvider provider, ServiceDescriptor descriptor) =>
            (HybridCache)(descriptor.ImplementationInstance
                ?? descriptor.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
    }

    private sealed class ProbeStartupFilter(CompletionProbe probe) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) => probe.RunAsync(context, nextMiddleware));
            next(app);
        };
    }

    public sealed record ObservedToken(CancellationToken Token, bool WasCanceledWhenCalled);

    public sealed class RecordingHybridCache(HybridCache inner) : HybridCache
    {
        public List<string> Operations { get; } = [];
        public List<ObservedToken> Tokens { get; } = [];

        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) =>
            inner.GetOrCreateAsync(key, state, factory, options, tags, cancellationToken);

        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) =>
            inner.SetAsync(key, value, options, tags, cancellationToken);

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            lock (Operations) { Operations.Add($"key:{key}"); Tokens.Add(new(cancellationToken, cancellationToken.IsCancellationRequested)); }
            return inner.RemoveAsync(key, cancellationToken);
        }

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
        {
            lock (Operations) { Operations.Add($"tag:{tag}"); Tokens.Add(new(cancellationToken, cancellationToken.IsCancellationRequested)); }
            return inner.RemoveByTagAsync(tag, cancellationToken);
        }
    }
}
