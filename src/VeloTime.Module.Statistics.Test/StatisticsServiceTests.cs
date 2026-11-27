using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Service.Recompute;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Test;

public sealed partial class StatisticsServiceTests
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
    public void Simple_CreatesEntryFromPreviousSample()
    {
        var transponderId = Guid.NewGuid();
        var startPoint = Guid.NewGuid();
        var midPoint = Guid.NewGuid();
        var endPoint = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var config = CreateSimpleConfig(Guid.NewGuid(), startPoint, endPoint, "Lap", 400);
        var time = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var first = CreateSample(transponderId, startPoint, midPoint, time, time.AddSeconds(20), facilityId);
        var completion = CreateSample(transponderId, midPoint, endPoint, time.AddSeconds(20), time.AddSeconds(50), facilityId);

        var entry = Assert.Single(Derive(config, first, completion));

        Assert.Equal(config.Id, entry.StatisticsItemConfigId);
        Assert.Equal(config.StatisticsItem.Id, entry.StatisticsItemId);
        Assert.Equal(time, entry.TimeStart);
        Assert.Equal(completion.TimeEnd, entry.TimeEnd);
        Assert.Equal(TimeSpan.FromSeconds(50), entry.Duration);
        Assert.Equal(400d / 50, entry.Speed, precision: 8);
        Assert.Equal(facilityId, entry.FacilityId);
        Assert.Equal(DeterministicId.SimpleEntry(config.Id, first.StartPassingId, completion.EndPassingId), entry.Id);
    }

    [Fact]
    public void Simple_IsDeterministicAcrossRepeatedDerivationAndInputOrder()
    {
        var transponderId = Guid.NewGuid();
        var finish = Guid.NewGuid();
        var red = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var config = CreateSimpleConfig(Guid.NewGuid(), finish, finish, "Lap", 250);
        var time = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var samples = new[]
        {
            CreateSample(transponderId, finish, red, time, time.AddSeconds(10), facilityId),
            CreateSample(transponderId, red, finish, time.AddSeconds(10), time.AddSeconds(20), facilityId),
            CreateSample(transponderId, finish, red, time.AddSeconds(20), time.AddSeconds(30), facilityId),
            CreateSample(transponderId, red, finish, time.AddSeconds(30), time.AddSeconds(40), facilityId)
        };

        var forward = Derive(config, samples);
        var reversed = Derive(config, samples.Reverse().ToArray());

        Assert.Equal(2, forward.Count);
        Assert.Equal(forward.Select(e => (e.Id, e.TimeStart, e.TimeEnd)), reversed.Select(e => (e.Id, e.TimeStart, e.TimeEnd)));
    }

    [Fact]
    public void Simple_RedToFinish_CompletesLapAnd200mFromTheirOwnStarts()
    {
        var transponderId = Guid.NewGuid();
        var twoHundredPoint = Guid.NewGuid();
        var finishPoint = Guid.NewGuid();
        var greenPoint = Guid.NewGuid();
        var hundredPoint = Guid.NewGuid();
        var redPoint = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var twoHundredConfig = CreateSimpleConfig(Guid.NewGuid(), twoHundredPoint, finishPoint, "200m", 200);
        var lapConfig = CreateSimpleConfig(Guid.NewGuid(), finishPoint, finishPoint, "Lap", 250);
        var otherEndConfig = CreateSimpleConfig(Guid.NewGuid(), redPoint, greenPoint, "Other end", 50);
        var lapStart = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var points = new[] { finishPoint, twoHundredPoint, greenPoint, hundredPoint, redPoint, finishPoint };
        var seconds = new[] { 0, 5, 11, 17, 23, 29 };
        // Two contiguous laps: the previous lap must not supply either start of the second.
        var samples = new List<Sample>();
        foreach (var offset in new[] { -29, 0 })
        {
            for (var segment = 0; segment < points.Length - 1; segment++)
            {
                samples.Add(CreateSample(transponderId, points[segment], points[segment + 1],
                    lapStart.AddSeconds(offset + seconds[segment]), lapStart.AddSeconds(offset + seconds[segment + 1]),
                    facilityId));
            }
        }
        var finish = lapStart.AddSeconds(29);

        AssertCompletedEntry(Assert.Single(Derive(lapConfig, samples), e => e.TimeEnd == finish),
            lapConfig, lapStart, finish, facilityId);
        AssertCompletedEntry(Assert.Single(Derive(twoHundredConfig, samples), e => e.TimeEnd == finish),
            twoHundredConfig, lapStart.AddSeconds(5), finish, facilityId);
        Assert.DoesNotContain(Derive(otherEndConfig, samples), e => e.TimeEnd == finish);
    }

    [Fact]
    public void Simple_ConfiguredStartSampleEndingAtCurrentStart_IsEligible()
    {
        var transponderId = Guid.NewGuid();
        var startPoint = Guid.NewGuid();
        var redPoint = Guid.NewGuid();
        var finishPoint = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var config = CreateSimpleConfig(Guid.NewGuid(), startPoint, finishPoint, "200m", 200);
        var time = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var entry = Assert.Single(Derive(config,
            CreateSample(transponderId, startPoint, redPoint, time.AddSeconds(-30), time.AddSeconds(-20), facilityId),
            CreateSample(transponderId, startPoint, redPoint, time, time.AddSeconds(10), facilityId),
            CreateSample(transponderId, redPoint, finishPoint, time.AddSeconds(10), time.AddSeconds(20), facilityId)));

        AssertCompletedEntry(entry, config, time, time.AddSeconds(20), facilityId);
    }

    [Fact]
    public void Simple_NoConfiguredStart_DoesNotUseOtherTransponderOrSegmentStart()
    {
        var transponderId = Guid.NewGuid();
        var configuredStart = Guid.NewGuid();
        var redPoint = Guid.NewGuid();
        var finishPoint = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var config = CreateSimpleConfig(Guid.NewGuid(), configuredStart, finishPoint, "200m", 200);
        var time = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        Assert.Empty(Derive(config,
            CreateSample(Guid.NewGuid(), configuredStart, redPoint, time, time.AddSeconds(10), facilityId),
            CreateSample(transponderId, redPoint, finishPoint, time.AddSeconds(-20), time.AddSeconds(-10), facilityId),
            CreateSample(transponderId, redPoint, finishPoint, time.AddSeconds(10), time.AddSeconds(20), facilityId)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Simple_CurrentSampleCoversItem_UsesCurrentStart(bool hasPreviousLapAtAnotherFacility)
    {
        var transponderId = Guid.NewGuid();
        var redPoint = Guid.NewGuid();
        var finishPoint = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var config = CreateSimpleConfig(Guid.NewGuid(), redPoint, finishPoint, "Final segment", 50);
        var time = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var samples = new List<Sample> { CreateSample(transponderId, redPoint, finishPoint, time, time.AddSeconds(6), facilityId) };
        if (hasPreviousLapAtAnotherFacility)
            samples.Add(CreateSample(transponderId, redPoint, finishPoint, time.AddSeconds(-30), time.AddSeconds(-24), Guid.NewGuid()));

        var entry = Assert.Single(Derive(config, samples), e => e.FacilityId == facilityId);

        AssertCompletedEntry(entry, config, time, time.AddSeconds(6), facilityId);
    }

    [Fact]
    public void Multi_AggregatesConsecutiveEntriesForConfiguredRepetitions()
    {
        var transponderId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var parent = CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Parent", 100);
        var aggregate = CreateMultiConfig(parent, 2, "Two laps", 200);
        var timeStart = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var firstEnd = timeStart.AddSeconds(10);
        var finalEnd = firstEnd.AddSeconds(10);
        var firstEntry = CreateEntry(transponderId, parent, timeStart, firstEnd, facilityId);
        var secondEntry = CreateEntry(transponderId, parent, firstEnd, finalEnd, facilityId);

        var entry = Assert.Single(EntryDerivation.Multi(aggregate, StatisticsDurationBounds.Unbounded, [secondEntry, firstEntry]));

        Assert.Equal(aggregate.Id, entry.StatisticsItemConfigId);
        Assert.Equal(aggregate.StatisticsItem.Id, entry.StatisticsItemId);
        Assert.Equal(timeStart, entry.TimeStart);
        Assert.Equal(finalEnd, entry.TimeEnd);
        Assert.Equal(TimeSpan.FromSeconds(20), entry.Duration);
        Assert.Equal(10, entry.Speed);
        Assert.True(entry.IsValid);
        Assert.Equal(DeterministicId.MultiEntry(aggregate.Id, firstEntry.Id, secondEntry.Id), entry.Id);
    }

    [Fact]
    public void Multi_InvalidContributorInvalidatesWindowAndOtherFacilitiesNeverJoin()
    {
        var transponderId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var parent = CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Parent", 100);
        var aggregate = CreateMultiConfig(parent, 2, "Two laps", 200);
        var time = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var first = CreateEntry(transponderId, parent, time, time.AddSeconds(10), facilityId);
        var second = CreateEntry(transponderId, parent, time.AddSeconds(10), time.AddSeconds(20), facilityId);
        second.IsValid = false;
        var elsewhere = CreateEntry(transponderId, parent, time.AddSeconds(20), time.AddSeconds(30), Guid.NewGuid());

        var entry = Assert.Single(EntryDerivation.Multi(aggregate, StatisticsDurationBounds.Unbounded, [first, second, elsewhere]));

        Assert.False(entry.IsValid);
        Assert.Equal(facilityId, entry.FacilityId);
    }

    private static IReadOnlyList<StatisticsEntry> Derive(SimpleStatisticsItemConfig config, params IEnumerable<Sample> samples) =>
        Derive(config, null, samples);

    private static IReadOnlyList<StatisticsEntry> Derive(SimpleStatisticsItemConfig config,
        ICollection<DerivationRejection>? rejections, IEnumerable<Sample> samples) =>
        EntryDerivation.Simple(config, StatisticsDurationBounds.Effective(config.StatisticsItem, config), samples,
            rejections: rejections);

    private static Sample CreateSample(Guid transponderId, Guid startPoint, Guid endPoint,
        DateTime timeStart, DateTime timeEnd, Guid facilityId)
    {
        var startPassingId = Guid.NewGuid();
        var endPassingId = Guid.NewGuid();
        return new Sample
        {
            Id = DeterministicId.Sample(startPassingId, endPassingId),
            StartPassingId = startPassingId,
            EndPassingId = endPassingId,
            TransponderId = transponderId,
            CoursePointStartId = startPoint,
            CoursePointEndId = endPoint,
            TimeStart = timeStart,
            TimeEnd = timeEnd,
            Duration = timeEnd - timeStart,
            Distance = 50,
            Speed = 50 / (timeEnd - timeStart).TotalSeconds,
            FacilityId = facilityId
        };
    }

    private static void AssertCompletedEntry(StatisticsEntry entry, SimpleStatisticsItemConfig config,
        DateTime timeStart, DateTime timeEnd, Guid facilityId)
    {
        Assert.Equal(config.Id, entry.StatisticsItemConfigId);
        Assert.Equal(config.StatisticsItem.Id, entry.StatisticsItemId);
        Assert.Equal(timeStart, entry.TimeStart);
        Assert.Equal(timeEnd, entry.TimeEnd);
        Assert.Equal(timeEnd - timeStart, entry.Duration);
        Assert.Equal(config.StatisticsItem.Distance / (timeEnd - timeStart).TotalSeconds, entry.Speed, precision: 8);
        Assert.Equal(facilityId, entry.FacilityId);
        Assert.True(entry.IsValid);
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
