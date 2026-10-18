using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Test;

public sealed class StatisticsServiceTests
{
    [Fact]
    public async Task GetMultiStatisticsItemConfigs_ReturnsConfigsByDescendingRepetitions()
    {
        await using var database = await TestDatabase.CreateAsync();
        var parent = CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Parent", 100);
        var oneRepetition = CreateMultiConfig(parent, 1, "Single", 100);
        var threeRepetitions = CreateMultiConfig(parent, 3, "Triple", 300);
        database.Storage.AddRange(parent, oneRepetition, threeRepetitions);
        await database.Storage.SaveChangesAsync();

        var result = (await database.Service.GetMultiStatisticsItemConfigs(parent.Id)).ToList();

        Assert.Collection(result,
            item => Assert.Equal(3, item.Repetitions),
            item => Assert.Equal(1, item.Repetitions));
        Assert.Equal("Triple", result[0].StatisticsItem.Name);
    }

    [Fact]
    public async Task GetStatisticsItemConfigByEndCoursePoint_ReturnsMatchingConfigs()
    {
        await using var database = await TestDatabase.CreateAsync();
        var endPoint = Guid.NewGuid();
        database.Storage.AddRange(
            CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), endPoint, "Matching", 120),
            CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Other", 200));
        await database.Storage.SaveChangesAsync();

        var result = (await database.Service.GetStatisticsItemConfigByEndCoursePoint(endPoint)).ToList();

        Assert.Equal("Matching", Assert.Single(result).StatisticsItem.Name);
    }

    [Fact]
    public async Task ProcessCompletedAsync_CreatesEntryFromPreviousSample()
    {
        await using var database = await TestDatabase.CreateAsync();
        var transponderId = Guid.NewGuid();
        var startPoint = Guid.NewGuid();
        var endPoint = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var config = CreateSimpleConfig(Guid.NewGuid(), startPoint, endPoint, "Lap", 400);
        var sample = new Sample
        {
            Id = Guid.NewGuid(),
            TimeStart = DateTime.UtcNow.AddMinutes(-2),
            TimeEnd = DateTime.UtcNow.AddMinutes(-1),
            TransponderId = transponderId,
            CoursePointStartId = startPoint,
            CoursePointEndId = Guid.NewGuid(),
            Duration = TimeSpan.FromSeconds(20),
            Distance = 100,
            Speed = 5,
            FacilityId = facilityId
        };
        database.Storage.AddRange(config, sample);
        await database.Storage.SaveChangesAsync();
        var startTime = sample.TimeEnd.AddSeconds(10);
        var endTime = startTime.AddSeconds(30);

        var entry = Assert.Single(await database.Service.ProcessCompletedAsync(transponderId, startTime, endTime, startPoint, endPoint));

        Assert.Equal(config.Id, entry.StatisticsItemConfigId);
        Assert.Equal(config.StatisticsItemId, entry.StatisticsItemId);
        Assert.Equal(sample.TimeStart, entry.TimeStart);
        Assert.Equal(endTime, entry.TimeEnd);
        var expectedDuration = endTime - sample.TimeStart;
        Assert.Equal(expectedDuration, entry.Duration);
        Assert.Equal(400d / expectedDuration.TotalSeconds, entry.Speed, precision: 8);
        Assert.Equal(facilityId, entry.FacilityId);
        Assert.Single(await database.Storage.Set<StatisticsEntry>().ToListAsync());
    }

    [Fact]
    public async Task ProcessEntryAsync_AggregatesRecentEntriesForConfiguredRepetitions()
    {
        await using var database = await TestDatabase.CreateAsync();
        var transponderId = Guid.NewGuid();
        var parent = CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Parent", 100);
        var aggregate = CreateMultiConfig(parent, 2, "Two laps", 200);
        var timeStart = DateTime.UtcNow.AddMinutes(-2);
        var firstEnd = timeStart.AddSeconds(10);
        var finalEnd = firstEnd.AddSeconds(10);
        var firstEntry = CreateEntry(transponderId, parent, timeStart, firstEnd, Guid.NewGuid());
        var secondEntry = CreateEntry(transponderId, parent, firstEnd, finalEnd, Guid.NewGuid());
        database.Storage.AddRange(parent, aggregate, firstEntry, secondEntry);
        await database.Storage.SaveChangesAsync();

        var result = await database.Service.ProcessEntryAsync(transponderId, parent.StatisticsItemId, parent.Id, timeStart, finalEnd);

        var entry = Assert.Single(result);
        Assert.Equal(aggregate.Id, entry.StatisticsItemConfigId);
        Assert.Equal(aggregate.StatisticsItemId, entry.StatisticsItemId);
        Assert.Equal(timeStart, entry.TimeStart);
        Assert.Equal(finalEnd, entry.TimeEnd);
        Assert.Equal(TimeSpan.FromSeconds(20), entry.Duration);
        Assert.Equal(10, entry.Speed);
        Assert.Single(await database.Storage.Set<StatisticsEntry>().Where(item => item.StatisticsItemConfigId == aggregate.Id).ToListAsync());
    }

    private static SimpleStatisticsItemConfig CreateSimpleConfig(Guid id, Guid startPoint, Guid endPoint, string name, float distance)
    {
        return new SimpleStatisticsItemConfig
        {
            Id = id,
            CoursePointStart = startPoint,
            CoursePointEnd = endPoint,
            StatisticsItem = new StatisticsItem { Id = Guid.NewGuid(), Name = name, Distance = distance },
            StatisticsItemId = Guid.NewGuid()
        };
    }

    private static MultiStatisticsItemConfig CreateMultiConfig(SimpleStatisticsItemConfig parent, int repetitions, string name, float distance)
    {
        return new MultiStatisticsItemConfig
        {
            Id = Guid.NewGuid(),
            ParentConfig = parent,
            ParentConfigId = parent.Id,
            Repetitions = repetitions,
            StatisticsItem = new StatisticsItem { Id = Guid.NewGuid(), Name = name, Distance = distance },
            StatisticsItemId = Guid.NewGuid()
        };
    }

    private static StatisticsEntry CreateEntry(Guid transponderId, SimpleStatisticsItemConfig config, DateTime timeStart, DateTime timeEnd, Guid facilityId)
    {
        return new StatisticsEntry
        {
            Id = Guid.NewGuid(),
            TransponderId = transponderId,
            StatisticsItem = config.StatisticsItem,
            StatisticsItemId = config.StatisticsItem.Id,
            StatisticsItemConfig = config,
            StatisticsItemConfigId = config.Id,
            TimeStart = timeStart,
            TimeEnd = timeEnd,
            Duration = timeEnd - timeStart,
            Speed = config.StatisticsItem.Distance / (float)(timeEnd - timeStart).TotalSeconds,
            FacilityId = facilityId
        };
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider services;

        private TestDatabase(SqliteConnection connection, StatisticsDbContext storage, ServiceProvider services)
        {
            this.connection = connection;
            this.services = services;
            Storage = storage;
            Service = new StatisticsService(storage, services.GetRequiredService<HybridCache>());
        }

        public StatisticsDbContext Storage { get; }
        public StatisticsService Service { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<StatisticsDbContext>().UseSqlite(connection).Options;
            var storage = new StatisticsDbContext(options);
            await storage.Database.EnsureCreatedAsync();
            var services = new ServiceCollection();
            services.AddHybridCache();
            return new TestDatabase(connection, storage, services.BuildServiceProvider());
        }

        public async ValueTask DisposeAsync()
        {
            await Storage.DisposeAsync();
            await services.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
