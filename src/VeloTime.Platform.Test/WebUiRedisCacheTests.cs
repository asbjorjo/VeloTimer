using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using Duende.AccessTokenManagement;
using Duende.AccessTokenManagement.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.AspNetCore.OutputCaching;
using VeloTime.Module.Facilities.Api.Test;
using VeloTime.Module.Facilities.Storage;
using StackExchange.Redis;
using VeloTime.Module.Common;
using VeloTime.Module.Facilities.Interface;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Services;
using ZiggyCreatures.Caching.Fusion;
using Xunit.Abstractions;
using Profiles = VeloTime.Module.Profiles.Client;
using Facilities = VeloTime.Module.Facilities.Client;

namespace VeloTime.Platform.Test;

public sealed class WebUiRedisFactAttribute : FactAttribute
{
    public WebUiRedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOTIME_TEST_REDIS")))
            Skip = "Requires actual Redis: set VELOTIME_TEST_REDIS.";
    }
}

/// <summary>Production WebUI hosts, typed clients and Duende delegated-token handlers, real L1/L2/backplane.</summary>
public sealed class WebUiRedisCacheTests(ITestOutputHelper output)
{
    [WebUiRedisFact]
    public async Task Preferences_AcrossRequestScopesHostsSessionsAndIssuers_SaveOnlyEvictsAffectedUser()
    {
        var upstream = new Upstream();
        using var first = new UiHost(upstream);
        using var second = new UiHost(upstream);
        var issuer = $"https://test/{Guid.NewGuid():N}";
        var alice = WebUiCacheTestHost.Principal(issuer, "alice", "sid-1");
        var aliceOtherSession = WebUiCacheTestHost.Principal(issuer, "alice", "sid-2");
        var bob = WebUiCacheTestHost.Principal(issuer, "bob");
        var otherIssuer = WebUiCacheTestHost.Principal(issuer + "/other", "alice");
        foreach (var user in new[] { alice, aliceOtherSession, bob, otherIssuer })
        {
            Assert.Equal("m", (await ReadDisplay(first, user)).DistanceUnit);
            Assert.Equal("m", (await ReadDisplay(second, user)).DistanceUnit);
        }
        Assert.Equal(4, upstream.Count("display", "GET"));
        for (var page = 0; page < 8; page++)
        {
            await ReadDisplay(first, alice);
            await ReadLanguage(first, alice);
            await ReadLanguage(second, alice);
            await WithScope(first, alice, service => service.GetTimeZonesAsync());
        }
        Assert.Equal(4, upstream.Count("display", "GET"));
        Assert.Equal(1, upstream.Count("language", "GET")); // legitimate unset is a success
        Assert.Equal(1, upstream.Count("time-zones", "GET"));
        var adminId = Guid.NewGuid();
        Assert.Equal("Before", await ReadAdmin(first, alice, adminId));
        Assert.Equal("Before", await ReadAdmin(second, alice, adminId));
        var timer = Stopwatch.StartNew();
        await WithScope(first, alice, service => service.SaveAsync(new("ft", "mph", "utc", null)));
        await Poll(async () => (await ReadDisplay(second, aliceOtherSession)).DistanceUnit == "ft", TimeSpan.FromSeconds(3));
        output.WriteLine($"Healthy preference tag propagation: {timer.Elapsed.TotalMilliseconds:F1}ms.");
        Assert.Equal("ft", (await ReadDisplay(second, alice)).DistanceUnit);
        Assert.Equal("m", (await ReadDisplay(second, bob)).DistanceUnit);
        Assert.Equal("m", (await ReadDisplay(second, otherIssuer)).DistanceUnit);
        Assert.Equal(6, upstream.Count("display", "GET"));
        Assert.Equal("Before", await ReadAdmin(second, alice, adminId));
        Assert.Equal(1, upstream.Count("admin", "GET"));
        Assert.Null((await ReadLanguage(second, alice)).Language);
        Assert.Equal(1, upstream.Count("language", "GET"));
        // Admin's own preferences remain the personal DTO, never an admin lookup DTO.
        Assert.Equal("ft", (await ReadDisplay(first, alice)).DistanceUnit);
        using var restarted = new UiHost(upstream);
        Assert.Equal("ft", (await ReadDisplay(restarted, aliceOtherSession)).DistanceUnit);
        Assert.Equal(6, upstream.Count("display", "GET"));
    }

    [WebUiRedisFact]
    public async Task ActualAdapter_HasThirtySecondLocalAndDistributedExpiry_NoGlobalDefaultChange()
    {
        var upstream = new Upstream();
        using var first = new UiHost(upstream);
        var user = WebUiCacheTestHost.Principal($"https://expiry/{Guid.NewGuid():N}");
        var started = Stopwatch.StartNew();
        Assert.Equal("m", (await ReadDisplay(first, user)).DistanceUnit);
        var fusion = first.Services.GetRequiredService<IFusionCache>();
        Assert.Equal(TimeSpan.FromMinutes(5), fusion.DefaultEntryOptions.Duration);
        Assert.Equal(TimeSpan.FromMinutes(1), fusion.DefaultEntryOptions.MemoryCacheDuration);
        // The actual Redis entry TTL verifies L2, not merely HybridCache option arguments.
        var redis = first.Services.GetRequiredService<IConnectionMultiplexer>();
        var server = redis.GetServer(redis.GetEndPoints()[0]);
        RedisKey[] keys = server.Keys(pattern: "*velotime:webui:personal:display-preference:*").ToArray();
        var subjectHash = HashTuple(user.FindFirst("iss")!.Value, "rider", "session");
        var key = Assert.Single(keys, value => value.ToString().Contains(subjectHash, StringComparison.Ordinal));
        var ttl = await redis.GetDatabase().KeyTimeToLiveAsync(key);
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value.TotalSeconds, 27, 30);
        upstream.SetDisplay(user, "ft");
        await Task.Delay(TimeSpan.FromSeconds(27) - started.Elapsed);
        Assert.Equal("m", (await ReadDisplay(first, user)).DistanceUnit); // real L1 still warm
        using (var cold = new UiHost(upstream))
            Assert.Equal("m", (await ReadDisplay(cold, user)).DistanceUnit); // real L2 still warm
        Assert.Equal(1, upstream.Count("display", "GET"));
        await Task.Delay(TimeSpan.FromSeconds(31) - started.Elapsed);
        Assert.Equal("ft", (await ReadDisplay(first, user)).DistanceUnit);
        using var expiredCold = new UiHost(upstream);
        Assert.Equal("ft", (await ReadDisplay(expiredCold, user)).DistanceUnit);
        Assert.Equal(2, upstream.Count("display", "GET"));
        output.WriteLine($"Measured expiry: {started.Elapsed.TotalSeconds:F2}s; L1 and cold-host L2 stale at 27s, fresh after 30s.");
    }

    [WebUiRedisFact]
    public async Task PublicDifferentlyTypedEntries_ExistingOwnerTagInvalidatesAllShapesAcrossHosts()
    {
        var upstream = new Upstream();
        using var first = new UiHost(upstream);
        using var second = new UiHost(upstream);
        var user = WebUiCacheTestHost.Principal($"https://facilities/{Guid.NewGuid():N}");
        var facilityId = Guid.NewGuid();
        upstream.FacilityId = facilityId;
        upstream.LayoutId = Guid.NewGuid();
        // Unique detail/name keys; public global list keys are deliberately not used in shared Redis.
        Assert.Equal("Before", await ReadPublicName(first, user, facilityId));
        Assert.Equal("Before", await ReadPublicName(second, user, facilityId));
        Assert.Equal(100, await ReadLayout(first, user, upstream.LayoutId));
        Assert.Equal(100, await ReadLayout(second, user, upstream.LayoutId));
        upstream.Name = "After";
        upstream.Length = 200;
        var timer = Stopwatch.StartNew();
        await first.Services.GetRequiredService<ICacheInvalidationOperations>().RemoveByTagAsync(FacilitiesCacheTags.Relationships, default);
        await Poll(async () => await ReadPublicName(second, user, facilityId) == "After" &&
                               await ReadLayout(second, user, upstream.LayoutId) == 200, TimeSpan.FromSeconds(3));
        output.WriteLine($"Healthy Facilities owner tag propagation across string/layout snapshot types: {timer.Elapsed.TotalMilliseconds:F1}ms.");
        using var restarted = new UiHost(upstream);
        Assert.Equal("After", await ReadPublicName(restarted, user, facilityId));
        Assert.Equal(200, await ReadLayout(restarted, user, upstream.LayoutId));
    }

    [WebUiRedisFact]
    public async Task ColdRedisHost_RoundTripsNestedFacilitySnapshotsForPublicAndAdminShapes()
    {
        var upstream = new Upstream
        {
            FacilityId = Guid.NewGuid(),
            LayoutId = Guid.NewGuid(),
            CoursePointId = Guid.NewGuid(),
            SegmentId = Guid.NewGuid(),
            StartPointId = Guid.NewGuid(),
            EndPointId = Guid.NewGuid()
        };
        var user = WebUiCacheTestHost.Principal($"https://snapshots/{Guid.NewGuid():N}");
        using (var warm = new UiHost(upstream))
        {
            warm.State.User = user;
            await using var scope = warm.Services.CreateAsyncScope();
            var adminFacility = Assert.Single(await scope.ServiceProvider
                .GetRequiredService<IInfrastructureAdministrationService>().GetFacilitiesAsync());
            Assert.Equal(upstream.FacilityId, adminFacility.Id);
            Assert.Equal(upstream.LayoutId, Assert.Single(adminFacility.Layouts));
            var detail = await scope.ServiceProvider.GetRequiredService<IFacilityService>()
                .GetCourseLayoutDetailAsync(upstream.LayoutId);
            var segment = Assert.Single(detail.Segments);
            Assert.Equal(upstream.SegmentId, segment.Id);
            Assert.Equal(upstream.StartPointId, segment.StartPoint.Id);
            Assert.Equal(upstream.EndPointId, segment.EndPoint.Id);
            Assert.Equal("Before", segment.StartPoint.Name);
            Assert.Equal("End", segment.EndPoint.Name);
        }

        using var cold = new UiHost(upstream);
        cold.State.User = user;
        await using var coldScope = cold.Services.CreateAsyncScope();
        var coldFacility = Assert.Single(await coldScope.ServiceProvider
            .GetRequiredService<IInfrastructureAdministrationService>().GetFacilitiesAsync());
        Assert.Equal(upstream.FacilityId, coldFacility.Id);
        Assert.Equal(upstream.LayoutId, Assert.Single(coldFacility.Layouts));
        var coldDetail = await coldScope.ServiceProvider.GetRequiredService<IFacilityService>()
            .GetCourseLayoutDetailAsync(upstream.LayoutId);
        var coldSegment = Assert.Single(coldDetail.Segments);
        Assert.Equal(upstream.SegmentId, coldSegment.Id);
        Assert.Equal(upstream.StartPointId, coldSegment.StartPoint.Id);
        Assert.Equal(upstream.EndPointId, coldSegment.EndPoint.Id);
        Assert.Equal("Before", coldSegment.StartPoint.Name);
        Assert.Equal("End", coldSegment.EndPoint.Name);
        Assert.Equal(1, upstream.Count("admin", "GET"));
        Assert.Equal(1, upstream.Count($"/api/facilities/layouts/{upstream.LayoutId}", "GET"));
    }

    [WebUiRedisFact]
    public async Task LatePreferenceFactoryAfterCommittedSave_IsBoundedByActualThirtySecondEntryLifetime()
    {
        var upstream = new Upstream { PauseDisplay = true, CaptureBeforePause = true };
        using var reader = new UiHost(upstream);
        using var writer = new UiHost(upstream);
        var user = WebUiCacheTestHost.Principal($"https://late/{Guid.NewGuid():N}");
        var oldRead = ReadDisplay(reader, user);
        await upstream.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WithScope(writer, user, service => service.SaveAsync(new("ft", "mph", "utc", null)));
        upstream.PauseDisplay = false;
        upstream.Release.TrySetResult();
        Assert.Equal("m", (await oldRead.WaitAsync(TimeSpan.FromSeconds(5))).DistanceUnit);
        var completed = Stopwatch.StartNew();
        using (var missedNotificationHost = new UiHost(upstream))
        {
            var immediate = (await ReadDisplay(missedNotificationHost, user)).DistanceUnit;
            Assert.Contains(immediate, new[] { "m", "ft" });
            output.WriteLine($"Late factory read m before commit, returned m after save+eviction; restarted host immediately observed {immediate}.");
        }
        await Task.Delay(TimeSpan.FromSeconds(31));
        Assert.Equal("ft", (await ReadDisplay(reader, user)).DistanceUnit);
        using var expiredRestart = new UiHost(upstream);
        Assert.Equal("ft", (await ReadDisplay(expiredRestart, user)).DistanceUnit);
        output.WriteLine($"Late-factory/restart value converged after {completed.Elapsed.TotalSeconds:F2}s from late factory completion.");
    }

    [WebUiRedisFact]
    public async Task SamplesKeepUtcAndBaseSiThroughActualTypedClient()
    {
        var upstream = new Upstream();
        using var host = new UiHost(upstream);
        host.State.User = WebUiCacheTestHost.Principal($"https://units/{Guid.NewGuid():N}");
        await using var scope = host.Services.CreateAsyncScope();
        var sample = Assert.Single(await scope.ServiceProvider.GetRequiredService<IStatisticsService>().GetSamplesAsync(null, false, 10));
        Assert.Equal(100, sample.Distance);
        Assert.Equal(TimeSpan.FromSeconds(20), sample.Duration);
    }

    [WebUiRedisFact]
    public async Task ProductionBffAndServerAdapterShareCache_NoStoreAndWarmPolicyRevocationRemainEnforced()
    {
        var upstream = new Upstream();
        using var host = new UiHost(upstream);
        var user = WebUiCacheTestHost.Principal($"https://bff/{Guid.NewGuid():N}");
        host.State.User = user;
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        for (var request = 0; request < 6; request++)
        {
            using var display = await client.GetAsync("/api/profile/preferences/display");
            Assert.Equal(HttpStatusCode.OK, display.StatusCode);
            Assert.True(display.Headers.CacheControl!.NoStore);
            Assert.Equal("m", (await display.Content.ReadFromJsonAsync<VeloTime.Module.Profiles.Interface.Data.DisplayPreferenceDTO>())!.DistanceUnit);
            await ReadDisplay(host, user); // direct server scope shares the same persisted DTO
            using var language = await client.GetAsync("/language/state");
            Assert.Equal(HttpStatusCode.OK, language.StatusCode);
            Assert.True(language.Headers.CacheControl!.NoStore);
        }
        Assert.Equal(1, upstream.Count("display", "GET"));
        Assert.Equal(1, upstream.Count("language", "GET"));
        using var admin = await client.GetAsync("/api/admin/lookups/facilities");
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Equal(1, upstream.Count("admin", "GET"));
        host.State.User = WebUiCacheTestHost.Principal(user.FindFirst("iss")!.Value, admin: false);
        using var denied = await client.GetAsync("/api/admin/lookups/facilities");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(1, upstream.Count("admin", "GET"));
        host.State.User = new(new ClaimsIdentity());
        using var loggedOut = await client.GetAsync("/api/profile/preferences/display");
        Assert.Equal(HttpStatusCode.Unauthorized, loggedOut.StatusCode);
        Assert.Equal(1, upstream.Count("display", "GET"));
    }

    [WebUiRedisFact]
    public async Task RealFacilitiesMutation_ComposedOutputFreshnessIsSeparateFromHealthyBackplanePropagation()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOTIME_TEST_POSTGRES")),
            "This regression requires PostgreSQL; it must not silently run with SQLite.");
        var redis = Environment.GetEnvironmentVariable("VELOTIME_TEST_REDIS")!;
        using var apiA = new FacilitiesApiFactory { UseProductionRedisCache = true, RedisConnectionString = redis };
        await apiA.InitializeDatabaseAsync();
        string connection;
        await using (var scope = apiA.Services.CreateAsyncScope())
            connection = scope.ServiceProvider.GetRequiredService<FacilityDbContext>().Database.GetDbConnection().ConnectionString;
        using var apiB = new FacilitiesApiFactory
        {
            UseProductionRedisCache = true,
            RedisConnectionString = redis,
            ConfigureTestServices = services =>
            {
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(FacilityDbContext) ||
                             d.ServiceType == typeof(DbContextOptions<FacilityDbContext>) ||
                             d.ServiceType == typeof(IDbContextOptionsConfiguration<FacilityDbContext>) ||
                             d.ServiceType == typeof(IOutputCacheStore)).ToArray())
                    services.Remove(descriptor);
                services.AddDbContext<FacilityDbContext>(options => options.UseNpgsql(connection));
                services.AddOutputCache(); // actual time-based per-host output store, not fixture clock
            }
        };
        using var writer = apiA.CreateClient();
        using var rawB = apiB.CreateClient();
        using var created = await writer.PostAsJsonAsync("/api/facilities/facility", new { name = "Before" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var facility = (await created.Content.ReadFromJsonAsync<VeloTime.Module.Facilities.Interface.Data.FacilityDTO>())!;
        var start = Guid.NewGuid();
        var end = Guid.NewGuid();
        var segment = Guid.NewGuid();
        object LayoutRequest(string name) => new
        {
            points = new[] { new { id = start, name }, new { id = end, name = "End" } },
            segments = new[] { new { id = segment, startId = start, endId = end, length = 100d } }
        };
        using var layoutResponse = await writer.PostAsJsonAsync($"/api/facilities/facility/{facility.Id}/layouts", LayoutRequest("Before"));
        Assert.Equal(HttpStatusCode.Created, layoutResponse.StatusCode);
        var layout = (await layoutResponse.Content.ReadFromJsonAsync<VeloTime.Module.Facilities.Interface.Data.CourseLayoutDTO>())!;
        var upstreamA = new Upstream { FacilityId = facility.Id, CoursePointId = start, FacilitiesApi = apiA };
        var upstreamB = new Upstream { FacilityId = facility.Id, CoursePointId = start, FacilitiesApi = apiB };
        using var uiA = new UiHost(upstreamA);
        using var uiB = new UiHost(upstreamB);
        var user = WebUiCacheTestHost.Principal($"https://api-mutation/{Guid.NewGuid():N}");
        // Ensure API B's output is warm even though UI B can initially read from Redis.
        using var warm = new HttpRequestMessage(HttpMethod.Get, $"/api/facilities/coursepoint/{start}");
        warm.Headers.Add("X-Test-Anonymous", "true");
        Assert.Equal(HttpStatusCode.OK, (await rawB.SendAsync(warm)).StatusCode);
        Assert.Equal("Before", await ReadSampleName(uiA, user));
        Assert.Equal("Before", await ReadSampleName(uiB, user));
        using var updated = await writer.PutAsJsonAsync($"/api/facilities/facility/{facility.Id}/layouts/{layout.Id}", LayoutRequest("After"));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var committed = Stopwatch.StartNew();
        // API owner's existing tag reaches the independent UI host, but a remote
        // API output response is allowed to refill it with the old value.
        await Poll(async () =>
        {
            await ReadSampleName(uiB, user);
            return upstreamB.Count($"/api/facilities/coursepoint/{start}", "GET") == 1;
        }, TimeSpan.FromSeconds(3));
        Assert.Equal("Before", await ReadSampleName(uiB, user));
        output.WriteLine($"Owner tag triggered remote UI factory in {committed.Elapsed.TotalMilliseconds:F1}ms; composed response still stale due to API B output.");
        await Task.Delay(TimeSpan.FromSeconds(31));
        Assert.Equal("After", await ReadSampleName(uiB, user));
        using var restarted = new UiHost(upstreamB);
        Assert.Equal("After", await ReadSampleName(restarted, user));
        output.WriteLine($"Composed output converged after {committed.Elapsed.TotalSeconds:F2}s in this schedule; this is not a universal 30s freshness SLA.");
    }

    [WebUiRedisFact]
    public async Task CancelAndDisposeFirstCaller_AllowsBoundedRetryWithPinnedIdentityAndOwnedScope()
    {
        var upstream = new Upstream { PauseDisplay = true };
        using var host = new UiHost(upstream);
        var user = WebUiCacheTestHost.Principal($"https://cancel/{Guid.NewGuid():N}", "alice");
        host.State.User = user;
        using var cancellation = new CancellationTokenSource();
        var scope = host.Services.CreateAsyncScope();
        var first = scope.ServiceProvider.GetRequiredService<IDisplayPreferenceService>().GetAsync(cancellation.Token);
        await upstream.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var secondScope = host.Services.CreateAsyncScope();
        var second = secondScope.ServiceProvider.GetRequiredService<IDisplayPreferenceService>().GetAsync();
        await Task.Delay(50);
        cancellation.Cancel();
        await scope.DisposeAsync();
        host.State.User = WebUiCacheTestHost.Principal("https://stale", "wrong");
        upstream.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal("m", (await second.WaitAsync(TimeSpan.FromSeconds(5))).DistanceUnit);
        output.WriteLine($"Canceled initiator: {upstream.Count("display", "GET")} upstream attempts; identities: {string.Join(", ", upstream.DisplayIdentities)}.");
        Assert.All(upstream.DisplayIdentities, identity => Assert.Equal(Identity(user), identity));
        Assert.InRange(upstream.Count("display", "GET"), 1, 2);
        Assert.NotEmpty(upstream.DisplayIdentities);
        Assert.Equal("m", (await ReadDisplay(host, user)).DistanceUnit);
        Assert.InRange(upstream.Count("display", "GET"), 1, 2);
    }

    [WebUiRedisFact]
    public async Task OrdinaryConcurrentMiss_CoalescesToOneFactoryAcrossScopes()
    {
        var upstream = new Upstream { PauseDisplay = true };
        using var host = new UiHost(upstream);
        var user = WebUiCacheTestHost.Principal($"https://singleflight/{Guid.NewGuid():N}", "alice");
        host.State.User = user;
        await using var firstScope = host.Services.CreateAsyncScope();
        await using var secondScope = host.Services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IDisplayPreferenceService>().GetAsync();
        await upstream.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = secondScope.ServiceProvider.GetRequiredService<IDisplayPreferenceService>().GetAsync();
        await Task.Delay(50);
        upstream.Release.TrySetResult();

        Assert.Equal("m", (await first.WaitAsync(TimeSpan.FromSeconds(5))).DistanceUnit);
        Assert.Equal("m", (await second.WaitAsync(TimeSpan.FromSeconds(5))).DistanceUnit);
        Assert.Equal(1, upstream.Count("display", "GET"));
        Assert.Equal(Identity(user), Assert.Single(upstream.DisplayIdentities));
    }

    private static async Task<T> WithScope<T>(UiHost host, ClaimsPrincipal user, Func<IDisplayPreferenceService, Task<T>> action)
    {
        host.State.User = user;
        host.Services.GetRequiredService<IHttpContextAccessor>().HttpContext = null;
        await using var scope = host.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IDisplayPreferenceService>());
    }

    private static Task<VeloTime.Module.Profiles.Interface.Data.DisplayPreferenceDTO> ReadDisplay(UiHost host, ClaimsPrincipal user) =>
        WithScope(host, user, service => service.GetAsync());

    private static async Task<VeloTime.Module.Profiles.Interface.Data.LanguagePreferenceDTO> ReadLanguage(UiHost host, ClaimsPrincipal user)
    {
        host.State.User = user;
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ILanguagePreferenceService>().GetAsync();
    }

    private static async Task<string> ReadAdmin(UiHost host, ClaimsPrincipal user, Guid id)
    {
        host.State.User = user;
        await using var scope = host.Services.CreateAsyncScope();
        return Assert.Single(await scope.ServiceProvider.GetRequiredService<IInfrastructureAdministrationService>().GetFacilitiesAsync()).Name;
    }

    private static async Task<string> ReadPublicName(UiHost host, ClaimsPrincipal user, Guid id)
    {
        host.State.User = user;
        await using var scope = host.Services.CreateAsyncScope();
        return Assert.Single(await scope.ServiceProvider.GetRequiredService<IFacilityService>().GetInstallationsAsync(id, default)).FacilityName;
    }

    private static async Task<double> ReadLayout(UiHost host, ClaimsPrincipal user, Guid id)
    {
        host.State.User = user;
        await using var scope = host.Services.CreateAsyncScope();
        return Assert.Single((await scope.ServiceProvider.GetRequiredService<IFacilityService>().GetCourseLayoutDetailAsync(id)).Segments).Distance;
    }

    private static async Task<string> ReadSampleName(UiHost host, ClaimsPrincipal user)
    {
        host.State.User = user;
        await using var scope = host.Services.CreateAsyncScope();
        var sample = Assert.Single(await scope.ServiceProvider.GetRequiredService<IStatisticsService>().GetSamplesAsync(null, false, 10));
        Assert.Equal(100, sample.Distance);
        Assert.Equal(TimeSpan.FromSeconds(20), sample.Duration);
        return sample.StartPoint;
    }

    private static async Task Poll(Func<Task<bool>> predicate, TimeSpan deadline)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(10);
        }
        Assert.Fail($"Cache did not converge within {deadline.TotalSeconds}s.");
    }

    private static string HashTuple(params string[] values)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            foreach (var value in values) { var bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }
    private static string Identity(ClaimsPrincipal user) => user.FindFirst("iss")!.Value + "|" + user.FindFirst("sub")!.Value;

    private sealed class UiHost(Upstream upstream) : WebApplicationFactory<DisplayPreferenceService>
    {
        internal WebUiCacheTestHost.MutableAuthenticationState State { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:cache", Environment.GetEnvironmentVariable("VELOTIME_TEST_REDIS")!);
            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(AuthenticationStateProvider)).ToArray())
                    services.Remove(descriptor);
                services.AddSingleton<AuthenticationStateProvider>(State);
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "UiCacheTest";
                    options.DefaultChallengeScheme = "UiCacheTest";
                    options.DefaultForbidScheme = "UiCacheTest";
                }).AddScheme<AuthenticationSchemeOptions, UiAuthenticationHandler>("UiCacheTest", _ => { });
                services.AddTransient<UiAuthenticationHandler>();
                services.PostConfigure<AuthenticationOptions>(options =>
                    options.SchemeMap["Cookies"].HandlerType = typeof(UiAuthenticationHandler));
                // Replace only the original ambient accessor. The production cache
                // decorator and real Duende handler still execute unmodified.
                services.AddKeyedScoped<IUserAccessor>("webui-original-user-accessor",
                    (_, _) => new CircuitAccessor(State));
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IUserTokenManager)).ToArray())
                    services.Remove(descriptor);
                services.AddSingleton(PlatformServiceTests.TestProxy.Create<IUserTokenManager>((method, args) =>
                {
                    Assert.Equal(nameof(IUserTokenManager.GetAccessTokenAsync), method.Name);
                    var principal = Assert.IsType<ClaimsPrincipal>(args[0]);
                    return (TokenResult<UserToken>)new UserToken
                    {
                        AccessToken = AccessToken.Parse(Convert.ToBase64String(Encoding.UTF8.GetBytes(Identity(principal)))),
                        Expiration = DateTimeOffset.UtcNow.AddHours(1),
                        ClientId = ClientId.Parse("velotime.webui"),
                        AccessTokenType = AccessTokenType.Parse("Bearer")
                    };
                }));
                services.ConfigureAll<HttpClientFactoryOptions>(options =>
                    options.HttpMessageHandlerBuilderActions.Add(handler => handler.PrimaryHandler = new UpstreamHandler(upstream)));
            });
        }
    }

    private sealed class CircuitAccessor(WebUiCacheTestHost.MutableAuthenticationState state) : IUserAccessor
    {
        public Task<ClaimsPrincipal> GetCurrentUserAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(state.User);
        }
    }

    private sealed class UiAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
        AuthenticationStateProvider state) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = (await state.GetAuthenticationStateAsync()).User;
            return user.Identity?.IsAuthenticated == true
                ? AuthenticateResult.Success(new AuthenticationTicket(user, Scheme.Name))
                : AuthenticateResult.NoResult();
        }
    }

    private sealed class Upstream
    {
        private readonly ConcurrentDictionary<string, string> displays = new();
        private readonly ConcurrentDictionary<string, int> calls = new();
        internal readonly ConcurrentQueue<string> DisplayIdentities = new();
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool PauseDisplay, CaptureBeforePause;
        internal FacilitiesApiFactory? FacilitiesApi;
        internal Guid FacilityId = Guid.NewGuid(), LayoutId = Guid.NewGuid(), CoursePointId = Guid.NewGuid();
        internal Guid SegmentId = Guid.NewGuid(), StartPointId = Guid.NewGuid(), EndPointId = Guid.NewGuid();
        internal string Name = "Before";
        internal double Length = 100;
        internal int Count(string kind, string method) => calls.GetValueOrDefault(kind + method);
        internal void SetDisplay(ClaimsPrincipal user, string value) => displays[Identity(user)] = value;
        internal async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            var kind = path.Contains("display") ? "display" : path.EndsWith("/preferences", StringComparison.Ordinal) ? "language" :
                path.Contains("time-zones") ? "time-zones" : path.EndsWith("/facility", StringComparison.Ordinal) ? "admin" : path;
            calls.AddOrUpdate(kind + request.Method.Method, 1, (_, count) => count + 1);
            if (FacilitiesApi is not null && path.StartsWith("/api/facilities/", StringComparison.Ordinal))
            {
                using var client = FacilitiesApi.CreateClient();
                using var forwarded = new HttpRequestMessage(request.Method, path);
                if (request.Method == HttpMethod.Get) forwarded.Headers.Add("X-Test-Anonymous", "true");
                return await client.SendAsync(forwarded, token);
            }
            var identity = request.Headers.Authorization is { Parameter: { } bearer }
                ? Encoding.UTF8.GetString(Convert.FromBase64String(bearer)) : "anonymous";
            object response;
            if (kind == "display")
            {
                var captured = displays.GetValueOrDefault(identity, "m");
                if (request.Method == HttpMethod.Put)
                {
                    using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                    displays[identity] = json.RootElement.GetProperty("distanceUnit").GetString()!;
                }
                else
                {
                    DisplayIdentities.Enqueue(identity);
                    Started.TrySetResult();
                    if (PauseDisplay) await Release.Task.WaitAsync(token);
                }
                var distance = CaptureBeforePause && request.Method == HttpMethod.Get ? captured : displays.GetValueOrDefault(identity, "m");
                response = new { distanceUnit = distance, speedUnit = distance == "ft" ? "mph" : "km/h", timeMode = "utc", timeZoneId = (string?)null };
            }
            else if (kind == "language") response = new { language = (string?)null };
            else if (kind == "time-zones") response = new[] { new { id = "Europe/Oslo", displayName = "Oslo" } };
            else if (path == "/api/statistics/sample")
                response = new { next = (DateTimeOffset?)null, previous = (DateTimeOffset?)null, isFirstPage = true,
                    samples = new[] { new { time = DateTimeOffset.UtcNow, transponderId = Guid.NewGuid(),
                    coursePointStartId = CoursePointId, coursePointEndId = CoursePointId, distance = 100, duration = "00:00:20", speed = 18 } } };
            else if (path.Contains("installations"))
                response = new[] { new { id = Guid.NewGuid(), facilityId = FacilityId, agentId = "agent", description = "Live", timingSystem = new { name = "X2" } } };
            else if (path.Contains("coursepoint"))
                response = new { id = CoursePointId, name = Name, timingPointId = (Guid?)null };
            else if (kind == "admin")
                response = new[] { new { id = FacilityId, name = Name, layouts = new[] { LayoutId } } };
            else if (path.Contains("layouts"))
            {
                response = new { id = LayoutId, facilityId = FacilityId, segments = new[] { new { id = SegmentId, order = 1,
                    length = Length, start = new { id = StartPointId, name = Name }, end = new { id = EndPointId, name = "End" } } } };
            }
            else if (path.Contains("facility")) response = new { id = FacilityId, name = Name, layouts = new[] { LayoutId } };
            else throw new NotSupportedException(path);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json") };
        }
    }

    private sealed class UpstreamHandler(Upstream upstream) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            upstream.Send(request, cancellationToken);
    }
}
