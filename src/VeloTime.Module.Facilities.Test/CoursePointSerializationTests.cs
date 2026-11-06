using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Service;
using VeloTime.Module.Facilities.Storage;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace VeloTime.Module.Facilities.Test;

public class CoursePointSerializationTests
{
    private static FusionCacheSystemTextJsonSerializer Serializer() =>
        new(new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve });

    private static CoursePoint Point()
    {
        var point = new CoursePoint { Id = Guid.NewGuid(), Name = "Finish" };
        point.TimingAssociations.Add(new()
        {
            CoursePoint = point, CoursePointId = point.Id, TimingPointId = Guid.NewGuid()
        });
        return point;
    }

    [Fact]
    public void ProductionSerializer_IgnoresThrowingBridgeAndPreservesAssociationGraph()
    {
        var point = Point();
        var serializer = Serializer();
        var bytes = serializer.Serialize(point);
        using var json = JsonDocument.Parse(bytes);
        Assert.False(json.RootElement.TryGetProperty("TimingPoint", out _));
        var restored = serializer.Deserialize<CoursePoint>(bytes);
        Assert.NotNull(restored);
        Assert.Equal(point.Id, restored.Id);
        Assert.Equal(point.TimingAssociations.Single().TimingPointId, restored.TimingAssociations.Single().TimingPointId);
        Assert.Same(restored, restored.TimingAssociations.Single().CoursePoint);
#pragma warning disable CS0618 // The source-compatibility bridge must still fail explicitly.
        Assert.Throws<NotSupportedException>(() => point.TimingPoint);
        Assert.Throws<NotSupportedException>(() => point.TimingPoint = Guid.NewGuid());
#pragma warning restore CS0618
    }

    [Fact]
    public async Task GetCoursePointById_FreshCacheReadsDistributedSerializedGraphWithoutDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FacilityDbContext>().UseSqlite(connection).Options;
        var storage = new FacilityDbContext(options);
        await storage.Database.EnsureCreatedAsync();
        var point = Point();
        storage.Add(point);
        await storage.SaveChangesAsync();

        var distributedServices = new ServiceCollection();
        distributedServices.AddDistributedMemoryCache();
        using var distributedProvider = distributedServices.BuildServiceProvider();
        var distributed = distributedProvider.GetRequiredService<IDistributedCache>();
        using var first = CacheProvider(distributed);
        Assert.Equal(point.Id, (await new FacilitiesService(storage,
            first.GetRequiredService<HybridCache>()).GetCoursePointById(point.Id))?.Id);
        await storage.DisposeAsync();

        // A separate FusionCache has no L1 state; a miss would invoke the disposed DbContext.
        using var second = CacheProvider(distributed);
        var restored = await new FacilitiesService(storage,
            second.GetRequiredService<HybridCache>()).GetCoursePointById(point.Id);
        Assert.NotNull(restored);
        Assert.NotSame(point, restored);
        Assert.Equal(point.Id, restored.Id);
        Assert.Equal(point.Name, restored.Name);
        Assert.Equal(point.TimingAssociations.Single().TimingPointId, restored.TimingAssociations.Single().TimingPointId);
        Assert.Same(restored, restored.TimingAssociations.Single().CoursePoint);
    }

    private static ServiceProvider CacheProvider(IDistributedCache distributed)
    {
        var services = new ServiceCollection();
        services.AddSingleton(distributed);
        services.AddFusionCache()
            .WithDefaultEntryOptions(new FusionCacheEntryOptions
            {
                AllowBackgroundDistributedCacheOperations = false,
                ReThrowSerializationExceptions = true,
                ReThrowDistributedCacheExceptions = true
            })
            .WithSerializer(Serializer())
            .WithRegisteredDistributedCache(ignoreMemoryDistributedCache: false)
            .AsHybridCache();
        return services.BuildServiceProvider();
    }
}
