using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Api.Test;

public sealed class RedisCacheFactAttribute : FactAttribute
{
    public RedisCacheFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOTIME_TEST_REDIS")))
            Skip = "Set VELOTIME_TEST_REDIS to run the real two-host Redis/output-cache test.";
    }
}

public sealed class FacilitiesOutputCacheReplicaTests
{
    private const string RelationshipsTag = "Facilities.Relationships";
    private static readonly TimeSpan OutputExpiration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PropagationSafetyTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(5);

    [RedisCacheFact]
    public async Task RemoteDomainInvalidationLeavesOtherHostsOutputUntilExpiryThenNestedRedisTagIsFreshAfterRestart()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"velotimer_facilities_replica_{Guid.NewGuid():N}.db");
        var redis = Environment.GetEnvironmentVariable("VELOTIME_TEST_REDIS")!;
        // Isolate this characterization in a dedicated Redis logical database;
        // no shared Redis database is flushed.
        var isolatedRedis = $"{redis},defaultDatabase=14";

        try
        {
            await using var hostA = CreateHost(dbPath, isolatedRedis);
            await hostA.InitializeDatabaseAsync();
            var (facility, start) = await SeedAsync(hostA);
            await using var hostB = CreateHost(dbPath, isolatedRedis);
            using var clientA = hostA.CreateClient();
            using var clientB = hostB.CreateClient();
            var route = $"/api/facilities/coursepoint/{start.Id}/facility";

            Assert.Equal("Before", (await GetFacilityAsync(clientA, route)).Name);
            Assert.Equal("Before", (await GetFacilityAsync(clientB, route)).Name);
            var hostBStored = Assert.Single(hostB.OutputCache.Stored);
            Assert.Contains(RelationshipsTag, hostBStored.Tags);
            Assert.Equal(OutputExpiration, hostBStored.ValidFor);

            var update = await clientA.PutAsJsonAsync(
                $"/api/facilities/facility/{facility.Id}",
                new SaveFacilityRequest("After"));
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            Assert.True(hostA.OutputCache.EvictionCalls > 0);

            // Output-cache stores are per host. A's write cannot evict B's
            // response; B continues serving its warm response until expiry.
            Assert.Equal("Before", (await GetFacilityAsync(clientB, route)).Name);
            Assert.Single(hostB.OutputCache.Stored);

            // Expire only B's output-cache entry. Its nested production
            // FusionCache/Redis entry must have observed the remote domain tag.
            hostB.OutputCache.AdvanceBy(OutputExpiration + TimeSpan.FromSeconds(1));
            var propagationTimer = Stopwatch.StartNew();
            FacilityDTO? expiredOutputResult = null;
            while (propagationTimer.Elapsed < PropagationSafetyTimeout)
            {
                expiredOutputResult = await GetFacilityAsync(clientB, route);
                if (expiredOutputResult.Name == "After") break;
                hostB.OutputCache.AdvanceBy(OutputExpiration + TimeSpan.FromSeconds(1));
                await Task.Delay(PollInterval);
            }
            Assert.NotNull(expiredOutputResult);
            Assert.Equal("After", expiredOutputResult.Name);
            Assert.Equal(2, hostB.OutputCache.SetCalls);

            // Restart B while a nested cache entry is warm, mutate from A, then
            // start a fresh replica after the notification. It must not serve
            // an obsolete Redis value after the missed-notification window.
            Assert.Equal("After", (await GetFacilityAsync(clientB, route)).Name);
            await hostB.DisposeAsync();
            var secondUpdate = await clientA.PutAsJsonAsync(
                $"/api/facilities/facility/{facility.Id}",
                new SaveFacilityRequest("After restart"));
            Assert.Equal(HttpStatusCode.OK, secondUpdate.StatusCode);

            await using var restartedB = CreateHost(dbPath, isolatedRedis);
            using var restartedClient = restartedB.CreateClient();
            var result = await GetFacilityAsync(restartedClient, route);
            Assert.Equal("After restart", result.Name);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    private static FacilitiesApiFactory CreateHost(string dbPath, string redis) =>
        new()
        {
            SqliteFilePath = dbPath,
            UseProductionRedisCache = true,
            RedisConnectionString = redis
        };

    private static async Task<(Facility Facility, CoursePoint Start)> SeedAsync(FacilitiesApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
        var facility = new Facility { Id = Guid.NewGuid(), Name = "Before" };
        var start = new CoursePoint { Id = Guid.NewGuid(), Name = "Start", TimingPoint = Guid.NewGuid() };
        var end = new CoursePoint { Id = Guid.NewGuid(), Name = "Finish", TimingPoint = Guid.NewGuid() };
        var layout = new CourseLayout { Id = Guid.NewGuid(), FacilityId = facility.Id, Facility = facility };
        layout.Segments.Add(new CourseSegment
        {
            Id = Guid.NewGuid(),
            CourseLayout = layout,
            CourseLayoutId = layout.Id,
            Order = 1,
            Start = start,
            End = end,
            Length = 100
        });
        facility.Layouts.Add(layout);
        storage.Add(facility);
        await storage.SaveChangesAsync();
        return (facility, start);
    }

    private static async Task<FacilityDTO> GetFacilityAsync(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-Anonymous", "true");
        using var response = await client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode,
            $"GET {path} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<FacilityDTO>())!;
    }
}
