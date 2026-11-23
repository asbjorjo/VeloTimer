using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;

namespace VeloTime.Module.Statistics.Test;

public sealed partial class StatisticsServiceTests
{
    [Fact]
    public async Task FixedCompletionReplayKeepsOriginalMeasurementAndInvalidationAfterLateStartAndDefinitionEdits()
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var startPoint = Guid.NewGuid();
        var intermediate = Guid.NewGuid();
        var finish = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var time = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        var config = CreateSimpleConfig(Guid.NewGuid(), startPoint, finish, "Fixed", 200);
        database.Storage.AddRange(config,
            CreateSample(rider, startPoint, intermediate, time, time.AddSeconds(5), facility));
        await database.Storage.SaveChangesAsync();
        var original = Assert.Single(await database.Service.ProcessCompletedAsync(
            rider, time.AddSeconds(10), time.AddSeconds(20), intermediate, finish));
        var owner = Guid.NewGuid();
        database.Storage.Add(new StatsProfile { UserId = owner, ProfileName = "Owner" });
        original.StatsProfileUserId = owner;
        original.IsValid = false;
        original.InvalidReason = StatisticsEntryInvalidReason.ManuallyInvalidated;
        original.ManualInvalidationChangedByUserId = owner;
        original.ManualInvalidationChangedAtUtc = time;
        config.StatisticsItem.Distance = 999;
        config.RequiredCoursePointIds = [Guid.NewGuid()];
        config.CoursePointEnd = Guid.NewGuid(); // Retargeting must not hide an unpublished result.
        database.Storage.Add(CreateSample(rider, startPoint, intermediate,
            time.AddSeconds(6), time.AddSeconds(9), Guid.NewGuid()));
        await database.Storage.SaveChangesAsync();
        database.Storage.ChangeTracker.Clear();

        var replay = Assert.Single(await database.Service.ProcessCompletedAsync(
            rider, time.AddSeconds(10), time.AddSeconds(20), intermediate, finish));

        Assert.Equal(original.Id, replay.Id);
        Assert.Equal(original.TimeStart, replay.TimeStart);
        Assert.Equal(original.Speed, replay.Speed);
        Assert.Equal(facility, replay.FacilityId);
        Assert.True(replay.RequiredCoursePointsSatisfied);
        Assert.Equal(StatisticsEntryInvalidReason.ManuallyInvalidated, replay.InvalidReason);
        Assert.Equal(owner, replay.StatsProfileUserId);
        Assert.Single(await database.Storage.Set<StatisticsEntry>().ToListAsync());
    }

    [Fact]
    public async Task EqualFinishDifferentCompletionStartsRemainDistinctAttempts()
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var config = CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Fixed", 200);
        var facility = Guid.NewGuid();
        var time = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        database.Storage.AddRange(config,
            CreateSample(rider, config.CoursePointStart, config.CoursePointEnd, time, time.AddSeconds(20), facility),
            CreateSample(rider, config.CoursePointStart, config.CoursePointEnd, time.AddSeconds(1), time.AddSeconds(20), facility));
        await database.Storage.SaveChangesAsync();

        var first = Assert.Single(await database.Service.ProcessCompletedAsync(
            rider, time, time.AddSeconds(20), config.CoursePointStart, config.CoursePointEnd));
        var second = Assert.Single(await database.Service.ProcessCompletedAsync(
            rider, time.AddSeconds(1), time.AddSeconds(20), config.CoursePointStart, config.CoursePointEnd));

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.TimeStart, second.TimeStart);
        Assert.Equal(2, await database.Storage.Set<StatisticsEntry>().CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultiReplayKeepsOriginalWindowAfterLateParentsAndRepetitionEdits(bool requiredPoints)
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var parent = CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Parent", 100);
        if (requiredPoints) parent.RequiredCoursePointIds = [Guid.NewGuid()];
        var multi = CreateMultiConfig(parent, 2, "Double", 200);
        var otherParent = CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Other", 100);
        var time = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        var facility = Guid.NewGuid();
        database.Storage.AddRange(parent, otherParent, multi,
            CreateEntry(rider, parent, time, time.AddSeconds(10), facility),
            CreateEntry(rider, parent, time.AddSeconds(10), time.AddSeconds(20), facility));
        await database.Storage.SaveChangesAsync();
        var original = Assert.Single(await database.Service.ProcessEntryAsync(
            rider, parent.StatisticsItemId, parent.Id, time.AddSeconds(10), time.AddSeconds(20)));
        database.Storage.Add(CreateEntry(rider, parent, time.AddSeconds(11), time.AddSeconds(19), facility));
        multi.Repetitions = 10;
        multi.StatisticsItem.Distance = 999;
        multi.ParentConfigId = otherParent.Id;
        multi.ParentConfig = otherParent;
        await database.Storage.SaveChangesAsync();
        database.Storage.ChangeTracker.Clear();

        var replay = Assert.Single(await database.Service.ProcessEntryAsync(
            rider, parent.StatisticsItemId, parent.Id, time.AddSeconds(10), time.AddSeconds(20)));

        Assert.Equal(original.Id, replay.Id);
        Assert.Equal(original.TimeStart, replay.TimeStart);
        Assert.Equal(original.TimeEnd, replay.TimeEnd);
        Assert.Equal(original.Speed, replay.Speed);
        Assert.Equal(original.IsValid, replay.IsValid);
        Assert.Single(await database.Storage.Set<StatisticsEntry>().Where(e => e.StatisticsItemConfigId == multi.Id).ToListAsync());
    }

    [Fact]
    public async Task MultiEqualFinishDifferentTriggerStartsDoNotCoalesceAndAmbiguousParentsFailExplicitly()
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var parent = CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Parent", 100);
        var multi = CreateMultiConfig(parent, 1, "Single", 100);
        var time = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        var facility = Guid.NewGuid();
        database.Storage.AddRange(parent, multi,
            CreateEntry(rider, parent, time, time.AddSeconds(20), facility),
            CreateEntry(rider, parent, time.AddSeconds(1), time.AddSeconds(20), facility));
        await database.Storage.SaveChangesAsync();

        var first = Assert.Single(await database.Service.ProcessEntryAsync(
            rider, parent.StatisticsItemId, parent.Id, time, time.AddSeconds(20)));
        var second = Assert.Single(await database.Service.ProcessEntryAsync(
            rider, parent.StatisticsItemId, parent.Id, time.AddSeconds(1), time.AddSeconds(20)));
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, await database.Storage.Set<StatisticsEntry>().CountAsync(e => e.StatisticsItemConfigId == multi.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Service.ProcessEntryAsync(
            rider, Guid.NewGuid(), parent.Id, time, time.AddSeconds(20)));

        database.Storage.Add(CreateEntry(rider, parent, time, time.AddSeconds(20), Guid.NewGuid()));
        await database.Storage.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Service.ProcessEntryAsync(
            rider, parent.StatisticsItemId, parent.Id, time, time.AddSeconds(20)));
        Assert.Equal(2, await database.Storage.Set<StatisticsEntry>().CountAsync(e => e.StatisticsItemConfigId == multi.Id));
    }

    [Fact]
    public async Task CompletionWithIndistinguishableTimingSourcesFailsInsteadOfMergingAttempts()
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var config = CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Fixed", 200);
        var facility = Guid.NewGuid();
        var time = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        Sample Source() => new()
        {
            Id = Guid.NewGuid(), TransponderId = rider, FacilityId = facility,
            CoursePointStartId = config.CoursePointStart, CoursePointEndId = config.CoursePointEnd,
            TimeStart = time, TimeEnd = time.AddSeconds(10), Duration = TimeSpan.FromSeconds(10),
            Distance = 200, Speed = 20, TimingPointStartId = Guid.NewGuid(), TimingPointEndId = Guid.NewGuid()
        };
        database.Storage.AddRange(config, Source(), Source());
        await database.Storage.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Service.ProcessCompletedAsync(
            rider, time, time.AddSeconds(10), config.CoursePointStart, config.CoursePointEnd));
        Assert.Empty(await database.Storage.Set<StatisticsEntry>().ToListAsync());
    }
}
