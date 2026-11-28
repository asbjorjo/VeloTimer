using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;

namespace VeloTime.Module.Statistics.Test;

public sealed class StatisticsManualInvalidationTests
{
    [Fact]
    public void InvalidReasonsRemainAppendOnly()
    {
        Assert.Equal(0, (int)StatisticsEntryInvalidReason.NonPositiveDuration);
        Assert.Equal(1, (int)StatisticsEntryInvalidReason.InvalidContributor);
        Assert.Equal(2, (int)StatisticsEntryInvalidReason.BelowMinimumDuration);
        Assert.Equal(3, (int)StatisticsEntryInvalidReason.AboveMaximumDuration);
        Assert.Equal(4, (int)StatisticsEntryInvalidReason.RequiredCoursePointsNotSatisfied);
        Assert.Equal(5, (int)StatisticsEntryInvalidReason.ManuallyInvalidated);
        Assert.Equal(500, StatisticsEntryManualInvalidationRequest.MaximumNoteLength);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(StatisticsEntryInvalidReason.NonPositiveDuration)]
    [InlineData(StatisticsEntryInvalidReason.InvalidContributor)]
    [InlineData(StatisticsEntryInvalidReason.RequiredCoursePointsNotSatisfied)]
    [InlineData(StatisticsEntryInvalidReason.BelowMinimumDuration)]
    [InlineData(StatisticsEntryInvalidReason.AboveMaximumDuration)]
    public void AutomaticClassificationCannotClearManualStateOrAudit(StatisticsEntryInvalidReason? reason)
    {
        var entry = ManualEntry();
        var snapshot = Snapshot(entry);
        Assert.False(StatisticsValidityEvaluator.Apply(entry, reason));
        Assert.Equal(snapshot, Snapshot(entry));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(StatisticsEntryInvalidReason.RequiredCoursePointsNotSatisfied)]
    [InlineData(StatisticsEntryInvalidReason.InvalidContributor)]
    [InlineData(StatisticsEntryInvalidReason.BelowMinimumDuration)]
    public void ExplicitRestoreReclassifiesWithoutErasingAuditOrMeasurements(StatisticsEntryInvalidReason? reason)
    {
        var entry = ManualEntry();
        var audit = (entry.ManualInvalidationChangedByUserId, entry.ManualInvalidationChangedAtUtc, entry.ManualInvalidationNote);
        var measurements = (entry.TimeStart, entry.TimeEnd, entry.Duration, entry.Speed, entry.RequiredCoursePointsSatisfied);
        Assert.True(StatisticsValidityEvaluator.Apply(entry, reason, allowManualInvalidationOverride: true));
        Assert.Equal(reason is null, entry.IsValid);
        Assert.Equal(reason, entry.InvalidReason);
        Assert.Equal(audit, (entry.ManualInvalidationChangedByUserId, entry.ManualInvalidationChangedAtUtc, entry.ManualInvalidationNote));
        Assert.Equal(measurements, (entry.TimeStart, entry.TimeEnd, entry.Duration, entry.Speed, entry.RequiredCoursePointsSatisfied));
        Assert.False(StatisticsValidityEvaluator.Apply(entry, reason, allowManualInvalidationOverride: true));
    }

    private static StatisticsEntry ManualEntry() => new()
    {
        TimeStart = new(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc),
        TimeEnd = new(2026, 11, 17, 8, 0, 10, DateTimeKind.Utc),
        Duration = TimeSpan.FromSeconds(10), Speed = 25, RequiredCoursePointsSatisfied = false,
        IsValid = false, InvalidReason = StatisticsEntryInvalidReason.ManuallyInvalidated,
        ManualInvalidationChangedByUserId = Guid.NewGuid(), ManualInvalidationChangedAtUtc = DateTimeOffset.UtcNow,
        ManualInvalidationNote = "Retain audit"
    };

    private static object Snapshot(StatisticsEntry entry) =>
        (entry.IsValid, entry.InvalidReason, entry.ManualInvalidationChangedByUserId,
            entry.ManualInvalidationChangedAtUtc, entry.ManualInvalidationNote,
            entry.TimeStart, entry.TimeEnd, entry.Duration, entry.Speed, entry.RequiredCoursePointsSatisfied);
}
