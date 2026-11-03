using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;

namespace VeloTime.Module.Statistics.Test;

public sealed class StatisticsValidityTests
{
    [Theory]
    [InlineData(null, null, null, null, null, null)]
    [InlineData(10, 20, null, null, 10, 20)]
    [InlineData(10, 20, 5, 30, 10, 20)]
    [InlineData(10, 20, 12, 18, 12, 18)]
    [InlineData(null, 20, 12, null, 12, 20)]
    [InlineData(10, null, null, 18, 10, 18)]
    [InlineData(10, 20, 20, 20, 20, 20)]
    public void EffectiveBoundsIntersectHardGlobals(int? globalMin, int? globalMax, int? localMin,
        int? localMax, int? expectedMin, int? expectedMax)
    {
        var actual = StatisticsDurationBounds.Effective(Duration(globalMin), Duration(globalMax),
            Duration(localMin), Duration(localMax));
        Assert.Equal(new StatisticsDurationBounds(Duration(expectedMin), Duration(expectedMax)), actual);
        Assert.False(actual.IsDisjoint);
    }

    [Theory]
    [InlineData(99, StatisticsEntryInvalidReason.BelowMinimumDuration)]
    [InlineData(100, null)]
    [InlineData(200, null)]
    [InlineData(201, StatisticsEntryInvalidReason.AboveMaximumDuration)]
    [InlineData(0, StatisticsEntryInvalidReason.NonPositiveDuration)]
    [InlineData(-1, StatisticsEntryInvalidReason.NonPositiveDuration)]
    public void InclusiveBoundariesAndAdjacentMicroseconds(long microseconds, StatisticsEntryInvalidReason? reason)
    {
        Assert.Equal(reason, StatisticsValidityEvaluator.Evaluate(TimeSpan.FromTicks(microseconds * 10), true,
            new(TimeSpan.FromTicks(1000), TimeSpan.FromTicks(2000))));
    }

    [Fact]
    public void ReclassificationRetainsMeasurementsAndOwnershipAndRestoresValidity()
    {
        var start = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);
        var entry = new StatisticsEntry
        {
            TimeStart = start, TimeEnd = start.AddSeconds(10), Duration = TimeSpan.FromSeconds(10),
            Speed = 25, FacilityId = Guid.NewGuid(), TransponderId = Guid.NewGuid(),
            StatsProfileUserId = Guid.NewGuid()
        };
        var original = (entry.TimeStart, entry.TimeEnd, entry.Duration, entry.Speed,
            entry.FacilityId, entry.TransponderId, entry.StatsProfileUserId);
        Assert.True(StatisticsValidityEvaluator.Apply(entry, StatisticsEntryInvalidReason.InvalidContributor));
        Assert.False(entry.IsValid);
        Assert.False(StatisticsValidityEvaluator.Apply(entry, StatisticsEntryInvalidReason.InvalidContributor));
        Assert.True(StatisticsValidityEvaluator.Apply(entry, null));
        Assert.True(entry.IsValid);
        Assert.Null(entry.InvalidReason);
        Assert.Equal(original, (entry.TimeStart, entry.TimeEnd, entry.Duration, entry.Speed,
            entry.FacilityId, entry.TransponderId, entry.StatsProfileUserId));
        Assert.Equal(StatisticsEntryInvalidReason.InvalidContributor,
            StatisticsValidityEvaluator.Evaluate(entry.Duration, false, StatisticsDurationBounds.Unbounded));
        Assert.Equal(StatisticsEntryInvalidReason.NonPositiveDuration,
            StatisticsValidityEvaluator.Evaluate(TimeSpan.Zero, false, StatisticsDurationBounds.Unbounded));
        Assert.True(StatisticsDurationBounds.Effective(Duration(10), Duration(20), Duration(21), null).IsDisjoint);
    }

    private static TimeSpan? Duration(int? seconds) => seconds is null ? null : TimeSpan.FromSeconds(seconds.Value);
}
