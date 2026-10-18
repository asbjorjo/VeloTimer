using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Service;
using VeloTime.Module.Facilities.Storage;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace VeloTime.Module.Facilities.Test;

public class FacilitiesServiceTests
{
    [Fact]
    public async Task GetCoursePointById_ReturnsPersistedPoint()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<FacilityDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var storage = new FacilityDbContext(options);
        await storage.Database.EnsureCreatedAsync();

        var point = new CoursePoint
        {
            Id = Guid.NewGuid(),
            Name = "Start",
            TimingPoint = Guid.NewGuid()
        };
        storage.Add(point);
        await storage.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddFusionCache()
            .WithSerializer(new FusionCacheSystemTextJsonSerializer(new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve
            }))
            .AsHybridCache();
        using var serviceProvider = services.BuildServiceProvider();
        var service = new FacilitiesService(storage, serviceProvider.GetRequiredService<HybridCache>());

        var result = await service.GetCoursePointById(point.Id);

        Assert.NotNull(result);
        Assert.Equal(point.Id, result.Id);
        Assert.Equal(point.Name, result.Name);
    }

    [Fact]
    public async Task CreateCourseLayoutAndDistanceMethods_PersistGraphAndCalculateDistances()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<FacilityDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var storage = new FacilityDbContext(options);
        await storage.Database.EnsureCreatedAsync();

        var facility = new Facility { Id = Guid.NewGuid(), Name = "Test facility" };
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
            Length = 125
        });
        facility.Layouts.Add(layout);

        var services = new ServiceCollection();
        services.AddFusionCache()
            .WithSerializer(new FusionCacheSystemTextJsonSerializer(new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve
            }))
            .AsHybridCache();
        using var serviceProvider = services.BuildServiceProvider();
        var service = new FacilitiesService(storage, serviceProvider.GetRequiredService<HybridCache>());

        await service.CreateCourseLayout(layout);

        var segments = (await service.GetSegmentsBetweenCoursePoints(start, end)).ToList();
        Assert.Single(segments);
        Assert.Equal(125, await service.CalculateDistanceBetweenCoursePoints(start, end));
        Assert.Equal(125, await service.DistanceBetweenCoursePoints(start, start));
        Assert.Equal(125, await service.DistanceBetweenTimingPoints(start.TimingPoint!.Value, end.TimingPoint!.Value));
        Assert.Equal(facility.Id, (await service.FacilityByCoursePoint(start.Id))?.Id);
    }
}
