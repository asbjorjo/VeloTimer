using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;

namespace VeloTime.Module.Statistics.Test;

public sealed partial class StatisticsServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CourseLayoutLap_DirectOrMultipleSegments_SnapshotsAndReplays(bool multipleSegments)
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var finish = Guid.NewGuid();
        var intermediate = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var layout = Guid.NewGuid();
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var config = CreateSimpleConfig(Guid.NewGuid(), finish, finish, "Lap", 0);
        config.StatisticsItem.DistanceMode = StatisticsDistanceMode.CourseLayout;
        database.Storage.Add(config);
        if (multipleSegments)
            database.Storage.Add(LapSample(rider, finish, intermediate, start, start.AddSeconds(10), facility, layout, 333.33, 100));
        var segmentStart = multipleSegments ? start.AddSeconds(10) : start;
        var segmentPoint = multipleSegments ? intermediate : finish;
        database.Storage.Add(LapSample(rider, segmentPoint, finish, segmentStart, start.AddSeconds(25), facility, layout, 333.33, multipleSegments ? 233.33 : 333.33));
        await database.Storage.SaveChangesAsync();

        var entry = Assert.Single(await database.Service.ProcessCompletedAsync(rider, segmentStart, start.AddSeconds(25), segmentPoint, finish));
        Assert.Equal(start, entry.TimeStart);
        Assert.Equal(start.AddSeconds(25), entry.TimeEnd);
        Assert.Equal(TimeSpan.FromSeconds(25), entry.Duration);
        Assert.Equal(333.33, entry.DistanceMetres);
        Assert.Equal(333.33 / 25, entry.Speed, 8);
        Assert.Equal(facility, entry.FacilityId);
        Assert.True(entry.IsValid);
        database.Storage.ChangeTracker.Clear();
        var stored = Assert.Single(await database.Storage.Set<StatisticsEntry>().AsNoTracking().ToListAsync());
        Assert.Equal(333.33, stored.DistanceMetres);
        Assert.Equal(333.33 / 25, stored.Speed, 8);
        Assert.Equal(start, stored.TimeStart);
        Assert.Equal(start.AddSeconds(25), stored.TimeEnd);

        // A redelivery must retain the original result, even if later evidence conflicts.
        database.Storage.Add(LapSample(rider, segmentPoint, finish, segmentStart, start.AddSeconds(25), facility, Guid.NewGuid(), 250, multipleSegments ? 233.33 : 333.33));
        await database.Storage.SaveChangesAsync();
        var replay = Assert.Single(await database.Service.ProcessCompletedAsync(rider, segmentStart, start.AddSeconds(25), segmentPoint, finish));
        Assert.Equal(entry.Id, replay.Id);
        Assert.Equal(333.33, replay.DistanceMetres);
        Assert.Equal(entry.Speed, replay.Speed);
        Assert.Single(await database.Storage.Set<StatisticsEntry>().ToListAsync());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("conflicting-layout")]
    [InlineData("conflicting-distance")]
    public async Task CourseLayoutLap_RejectsMissingOrConflictingPathEvidence(string problem)
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var finish = Guid.NewGuid();
        var intermediate = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var layout = Guid.NewGuid();
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var config = CreateSimpleConfig(Guid.NewGuid(), finish, finish, "Lap", 0);
        config.StatisticsItem.DistanceMode = StatisticsDistanceMode.CourseLayout;
        database.Storage.AddRange(config,
            LapSample(rider, finish, intermediate, start, start.AddSeconds(10), facility,
                problem == "missing" ? null : problem == "conflicting-layout" ? Guid.NewGuid() : layout,
                problem == "missing" ? null : problem == "conflicting-distance" ? 250 : 333.33, 100),
            LapSample(rider, intermediate, finish, start.AddSeconds(10), start.AddSeconds(25), facility, layout, 333.33, 233.33));
        await database.Storage.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await database.Service.ProcessCompletedAsync(rider, start.AddSeconds(10), start.AddSeconds(25), intermediate, finish));
        Assert.Empty(await database.Storage.Set<StatisticsEntry>().ToListAsync());
    }

    private static Sample LapSample(Guid rider, Guid from, Guid to, DateTime start, DateTime end,
        Guid facility, Guid? layout, double? length, double segmentDistance) => new()
    {
        Id = Guid.NewGuid(), TransponderId = rider, CoursePointStartId = from, CoursePointEndId = to,
        TimeStart = start, TimeEnd = end, Duration = end - start, FacilityId = facility,
        Distance = segmentDistance, Speed = segmentDistance / (end - start).TotalSeconds,
        CourseLayoutId = layout, ClosedLoopDistanceMetres = length
    };
}
