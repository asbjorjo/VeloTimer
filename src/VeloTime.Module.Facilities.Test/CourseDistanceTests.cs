using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Service;
using VeloTime.Module.Facilities.Storage;
using ZiggyCreatures.Caching.Fusion;

namespace VeloTime.Module.Facilities.Test;

public class CourseDistanceTests
{
    // Finish -> 200m -> Green -> 100m -> Red -> Finish, in forward segment order.
    [Theory]
    [InlineData(1, 0, 200)]
    [InlineData(0, 1, 50)]
    [InlineData(4, 0, 15)]
    [InlineData(0, 4, 235)]
    [InlineData(2, 1, 190)]
    [InlineData(1, 2, 60)]
    [InlineData(3, 2, 210)]
    [InlineData(2, 3, 40)]
    [InlineData(4, 1, 65)]
    [InlineData(1, 4, 185)]
    [InlineData(2, 0, 140)]
    [InlineData(0, 2, 110)]
    [InlineData(0, 0, 250)]
    [InlineData(1, 1, 250)]
    [InlineData(2, 2, 250)]
    [InlineData(3, 3, 250)]
    [InlineData(4, 4, 250)]
    public Task ClosedCourse_UsesForwardOrderIncludingWrapAndFullLap(int start, int end, double expected) =>
        AssertDistanceAsync(true, start, end, expected);

    // Open course has distinct first/last endpoints. Reverse travel retains linear distance,
    // not wraparound; equal IDs retain the legacy whole-path distance.
    [Theory]
    [InlineData(0, 5, 250)]
    [InlineData(5, 0, 250)]
    [InlineData(1, 5, 200)]
    [InlineData(5, 1, 200)]
    [InlineData(0, 1, 50)]
    [InlineData(1, 0, 50)]
    [InlineData(4, 5, 15)]
    [InlineData(5, 4, 15)]
    [InlineData(1, 4, 185)]
    [InlineData(4, 1, 185)]
    [InlineData(2, 3, 40)]
    [InlineData(3, 2, 40)]
    [InlineData(0, 0, 250)]
    [InlineData(2, 2, 250)]
    [InlineData(5, 5, 250)]
    public Task OpenCourse_PreservesLinearDistanceAtBothBoundaries(int start, int end, double expected) =>
        AssertDistanceAsync(false, start, end, expected);

    [Fact]
    public Task ClosedCourse_IgnoresPreviouslyCachedReverseLinearDistance() =>
        AssertDistanceAsync(true, 1, 0, 200, legacyDistance: 50);

    [Fact]
    public async Task ClosedCourse_InvalidLengthReturnsDistanceWithoutLoopEvidence()
    {
        var points = MakePoints(3);
        var layout = MakeLayout(points, [100, 0, 300]);
        await using var context = await CreateServiceContextAsync(layout);

        var resolution = await context.Service.CoursePointDistanceBetweenCoursePoints(points[0], points[1]);

        Assert.Equal(100, resolution.Distance);
        Assert.Null(resolution.CourseLayoutId);
        Assert.Null(resolution.ClosedLoopDistanceMetres);
    }

    [Fact]
    public async Task DiscontinuousCourse_CannotResolveDistanceOrLoopEvidence()
    {
        var points = MakePoints(4);
        var layout = new CourseLayout { Id = Guid.NewGuid(), Facility = new() { Id = Guid.NewGuid() } };
        layout.EnsureUnboundedRevision().Segments.Add(new CourseSegment
        {
            Id = Guid.NewGuid(), Order = 1,
            Start = points[0], End = points[1], Length = 100
        });
        layout.EnsureUnboundedRevision().Segments.Add(new CourseSegment
        {
            Id = Guid.NewGuid(), Order = 2,
            Start = points[2], End = points[3], Length = 200
        });
        await using var context = await CreateServiceContextAsync(layout);

        await Assert.ThrowsAsync<IncompatibleCoursePathException>(() =>
            context.Service.CoursePointDistanceBetweenCoursePoints(points[0], points[3]));
    }

    [Fact]
    public async Task MultipleMatchingLayouts_CannotResolveDistanceOrLoopEvidence()
    {
        var points = MakePoints(2);
        var first = MakeLayout(points, [100]);
        var second = MakeLayout(points, [250]);
        await using var context = await CreateServiceContextAsync(first, second);

        await Assert.ThrowsAsync<IncompatibleCoursePathException>(() =>
            context.Service.CoursePointDistanceBetweenCoursePoints(points[0], points[1]));
    }

    private static CoursePoint[] MakePoints(int count) => Enumerable.Range(0, count)
        .Select(index => new CoursePoint { Id = Guid.NewGuid(), Name = $"Point {index}" }).ToArray();

    private static CourseLayout MakeLayout(IReadOnlyList<CoursePoint> points, IReadOnlyList<double> lengths)
    {
        var layout = new CourseLayout { Id = Guid.NewGuid(), Facility = new() { Id = Guid.NewGuid() } };
        for (var index = 0; index < lengths.Count; index++)
            layout.EnsureUnboundedRevision().Segments.Add(new CourseSegment
            {
                Id = Guid.NewGuid(), Order = index + 1,
                Start = points[index], End = points[(index + 1) % points.Count], Length = lengths[index]
            });
        return layout;
    }

    private static async Task<DistanceServiceContext> CreateServiceContextAsync(params CourseLayout[] layouts)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var storage = new FacilityDbContext(new DbContextOptionsBuilder<FacilityDbContext>()
            .UseSqlite(connection).Options);
        try
        {
            await storage.Database.EnsureCreatedAsync();
            storage.AddRange(layouts);
            await storage.SaveChangesAsync();
            storage.ChangeTracker.Clear();
            var services = new ServiceCollection();
            services.AddFusionCache().AsHybridCache();
            var provider = services.BuildServiceProvider();
            var service = new FacilitiesService(storage, provider.GetRequiredService<HybridCache>());
            return new DistanceServiceContext(connection, storage, provider, service);
        }
        catch
        {
            await storage.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class DistanceServiceContext(SqliteConnection connection, FacilityDbContext storage,
        ServiceProvider provider, FacilitiesService service) : IAsyncDisposable
    {
        public FacilitiesService Service { get; } = service;

        public async ValueTask DisposeAsync()
        {
            await storage.DisposeAsync();
            await provider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private static async Task AssertDistanceAsync(bool closed, int start, int end, double expected,
        double? legacyDistance = null)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var storage = new FacilityDbContext(new DbContextOptionsBuilder<FacilityDbContext>()
            .UseSqlite(connection).Options);
        await storage.Database.EnsureCreatedAsync();
        var names = new[] { "Finish", "200m", "Green", "100m", "Red", "Open end" };
        var points = names.Take(closed ? 5 : 6)
            .Select(name => new CoursePoint { Id = Guid.NewGuid(), Name = name }).ToArray();
        var layout = new CourseLayout { Id = Guid.NewGuid(), Facility = new() { Id = Guid.NewGuid() } };
        var lengths = new double[] { 50, 60, 40, 85, 15 };
        // Deliberately insert backwards: traversal must use Order, not collection insertion order.
        for (var i = lengths.Length - 1; i >= 0; i--)
            layout.EnsureUnboundedRevision().Segments.Add(new()
            {
                Id = Guid.NewGuid(), Order = i,
                Start = points[i], End = points[(i + 1) % points.Length], Length = lengths[i]
            });
        storage.Add(layout);
        await storage.SaveChangesAsync();
        storage.ChangeTracker.Clear();
        var services = new ServiceCollection();
        services.AddFusionCache().AsHybridCache();
        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();
        var service = new FacilitiesService(storage, cache);
        // Distinct model instances ensure traversal/equal endpoints are identified by ID.
        var from = new CoursePoint { Id = points[start].Id };
        var to = new CoursePoint { Id = points[end].Id };
        if (legacyDistance.HasValue)
            await cache.SetAsync($"velotime:facilities:distance-between-course-points:v1:{from.Id}:{to.Id}",
                legacyDistance.Value);
        Assert.Equal(expected, await service.CalculateDistanceBetweenCoursePoints(from, to));
        Assert.Equal(expected, await service.DistanceBetweenCoursePoints(from, to));
    }
}
