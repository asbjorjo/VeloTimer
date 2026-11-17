using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    [PostgreSqlStatisticsFact]
    public async Task ThresholdChangesReadCheckpointOutcomeEvenWhenAnotherReasonWasStored()
    {
        var config = Simple();
        var entries = new List<StatisticsEntry>();
        await using (var db = Db())
        {
            db.Add(config);
            foreach (var reason in new[] { StatisticsEntryInvalidReason.NonPositiveDuration,
                StatisticsEntryInvalidReason.BelowMinimumDuration, StatisticsEntryInvalidReason.RequiredCoursePointsNotSatisfied })
            {
                var duration = reason == StatisticsEntryInvalidReason.NonPositiveDuration
                    ? TimeSpan.Zero : TimeSpan.FromSeconds(10);
                var entry = new StatisticsEntry
                {
                    StatisticsItemId = config.StatisticsItemId, StatisticsItemConfigId = config.Id,
                    TransponderId = Guid.NewGuid(), FacilityId = Guid.NewGuid(),
                    TimeStart = Start, TimeEnd = Start + duration, Duration = duration, Speed = 25,
                    RequiredCoursePointsSatisfied = false, IsValid = false, InvalidReason = reason
                };
                db.Add(entry);
                entries.Add(entry);
            }
            await db.SaveChangesAsync();
        }
        foreach (var minimum in new TimeSpan?[] { TimeSpan.FromSeconds(20), null, TimeSpan.FromSeconds(5) })
        {
            await Change(config.StatisticsItemId, minimum);
            await Drain(config.StatisticsItemId, 1);
            await using var verify = Db();
            var rows = await verify.Set<StatisticsEntry>().AsNoTracking().ToListAsync();
            Assert.Equal(3, rows.Count);
            Assert.All(rows, row =>
            {
                Assert.False(row.IsValid);
                Assert.False(row.RequiredCoursePointsSatisfied);
                Assert.Equal(row.Duration == TimeSpan.Zero ? StatisticsEntryInvalidReason.NonPositiveDuration
                    : StatisticsEntryInvalidReason.RequiredCoursePointsNotSatisfied, row.InvalidReason);
            });
        }
    }
}
