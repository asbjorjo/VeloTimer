using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;

namespace VeloTime.Module.Statistics.Test;

public sealed partial class StatisticsServiceTests
{
    [Theory]
    [InlineData("ordered", true)]
    [InlineData("unbounded", true)]
    [InlineData("lap", true)]
    [InlineData("duplicateEdges", true)]
    [InlineData("extraPoint", true)]
    [InlineData("missing", false)]
    [InlineData("reversed", false)]
    [InlineData("gap", false)]
    [InlineData("overlap", false)]
    [InlineData("disconnected", false)]
    [InlineData("otherFacility", false)]
    [InlineData("otherRider", false)]
    [InlineData("equalInstants", false)]
    [InlineData("conflictingEqualInstant", false)]
    [InlineData("conflictingStartBoundary", false)]
    [InlineData("conflictingFinishBoundary", false)]
    [InlineData("repeatedRequired", false)]
    [InlineData("previousFinish", false)]
    [InlineData("latestStartArrival", false)]
    [InlineData("latePriorLap", false)]
    public async Task RequiredPointsUseOneContinuousAttempt(string scenario, bool expected)
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var start = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var end = scenario == "lap" ? start : Guid.NewGuid();
        var other = Guid.NewGuid();
        var time = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
        var config = CreateSimpleConfig(Guid.NewGuid(), start, end, "Required", 100);
        config.RequiredCourseLayoutId = Guid.NewGuid();
        config.RequiredCoursePointIds = [a, b];
        // Both duration bounds and checkpoints fail in invalid scenarios: checkpoints take precedence.
        config.MinDuration = scenario == "unbounded" ? null : TimeSpan.FromSeconds(60);
        var edges = new List<Sample>
        {
            CreateSample(rider, start, a, time, time.AddSeconds(10), facility),
            CreateSample(rider, a, b, time.AddSeconds(10), time.AddSeconds(20), facility),
            CreateSample(rider, b, end, time.AddSeconds(20), time.AddSeconds(30), facility)
        };
        Sample Edge(Guid from, Guid to, int fromSeconds, int toSeconds, Guid? person = null, Guid? place = null) =>
            CreateSample(person ?? rider, from, to, time.AddSeconds(fromSeconds), time.AddSeconds(toSeconds), place ?? facility);
        switch (scenario)
        {
            case "duplicateEdges": edges.AddRange(edges.ToList().Select(e =>
                CreateSample(rider, e.CoursePointStartId, e.CoursePointEndId, e.TimeStart, e.TimeEnd, facility))); break;
            case "extraPoint": edges.RemoveAt(1); edges.AddRange([Edge(a, other, 10, 15), Edge(other, b, 15, 20)]); break;
            case "missing": edges = [Edge(start, b, 0, 20), edges[2]]; break;
            case "reversed": edges = [Edge(start, b, 0, 5), Edge(b, a, 5, 10), Edge(a, b, 10, 20), edges[2]]; break;
            case "gap": edges.RemoveAt(1); break;
            case "overlap": edges.Add(Edge(start, b, 0, 20)); break;
            case "disconnected": edges[1] = Edge(other, b, 10, 20); break;
            case "otherFacility": edges[1] = Edge(a, b, 10, 20, place: Guid.NewGuid()); break;
            case "otherRider": edges[1] = Edge(a, b, 10, 20, person: Guid.NewGuid()); break;
            case "equalInstants": edges = [Edge(start, a, 0, 10), Edge(a, b, 10, 10), Edge(b, b, 10, 20), edges[2]]; break;
            case "conflictingEqualInstant": edges.Add(Edge(a, other, 10, 10)); break;
            case "conflictingStartBoundary": edges.Add(Edge(b, other, -10, 0)); break;
            case "conflictingFinishBoundary": edges.Add(Edge(other, a, 30, 40)); break;
            case "repeatedRequired": edges = [edges[0], Edge(a, a, 10, 15), Edge(a, b, 15, 20), edges[2]]; break;
            case "previousFinish": edges = [edges[0], Edge(a, end, 10, 12), Edge(end, b, 12, 20), edges[2]]; break;
            case "latestStartArrival": edges.Add(Edge(other, start, 10, 15)); break;
            case "latePriorLap":
                edges.RemoveAt(1);
                edges.AddRange([Edge(start, a, -30, -20), Edge(a, b, -20, -10), Edge(b, end, -10, -1)]);
                break;
        }
        database.Storage.Add(config);
        // Deliberately reverse persistence order; timestamps/chain, never row/message order, decide.
        database.Storage.AddRange(edges.AsEnumerable().Reverse());
        await database.Storage.SaveChangesAsync();
        var entry = Assert.Single(await database.Service.ProcessCompletedAsync(rider,
            time.AddSeconds(20), time.AddSeconds(30), b, end));
        Assert.Equal(expected, entry.RequiredCoursePointsSatisfied);
        Assert.Equal(scenario == "unbounded", entry.IsValid);
        Assert.Equal(scenario == "unbounded" ? null : expected ? StatisticsEntryInvalidReason.BelowMinimumDuration
            : StatisticsEntryInvalidReason.RequiredCoursePointsNotSatisfied, entry.InvalidReason);
        Assert.Equal(scenario == "latestStartArrival" ? time.AddSeconds(15) : time, entry.TimeStart);
        Assert.Equal(facility, entry.FacilityId);
        database.Storage.ChangeTracker.Clear();
        Assert.Equal(expected, (await database.Storage.Set<StatisticsEntry>().SingleAsync()).RequiredCoursePointsSatisfied);
    }

    [Fact]
    public async Task LateEvidenceAndDuplicateCompletionReplayOriginalEntryWithoutRepairOrDuplicateRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var rider = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var start = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var end = Guid.NewGuid();
        var time = DateTime.UtcNow;
        var config = CreateSimpleConfig(Guid.NewGuid(), start, end, "Required", 100);
        config.RequiredCoursePointIds = [a, b];
        config.RequiredCourseLayoutId = Guid.NewGuid();
        var multi = CreateMultiConfig(config, 1, "Derived", 100);
        database.Storage.AddRange(config, multi,
            CreateSample(rider, start, a, time, time.AddSeconds(10), facility),
            CreateSample(rider, b, end, time.AddSeconds(20), time.AddSeconds(30), facility));
        await database.Storage.SaveChangesAsync();
        var entry = Assert.Single(await database.Service.ProcessCompletedAsync(rider,
            time.AddSeconds(20), time.AddSeconds(30), b, end));
        Assert.False(entry.RequiredCoursePointsSatisfied);
        var derived = Assert.Single(await database.Service.ProcessEntryAsync(rider,
            config.StatisticsItemId, config.Id, entry.TimeStart, entry.TimeEnd));
        Assert.Equal(StatisticsEntryInvalidReason.InvalidContributor, derived.InvalidReason);
        Assert.True(derived.RequiredCoursePointsSatisfied); // Multi uses contributors, not own checkpoints.
        database.Storage.Add(CreateSample(rider, a, b, time.AddSeconds(10), time.AddSeconds(20), facility));
        await database.Storage.SaveChangesAsync();
        var replay = Assert.Single(await database.Service.ProcessCompletedAsync(rider,
            time.AddSeconds(20), time.AddSeconds(30), b, end));
        Assert.Equal(entry.Id, replay.Id);
        var derivedReplay = Assert.Single(await database.Service.ProcessEntryAsync(rider,
            config.StatisticsItemId, config.Id, replay.TimeStart, replay.TimeEnd));
        Assert.Equal(derived.Id, derivedReplay.Id);
        Assert.Equal(2, await database.Storage.Set<StatisticsEntry>().CountAsync());
        Assert.False(entry.RequiredCoursePointsSatisfied);
        Assert.Equal(StatisticsEntryInvalidReason.RequiredCoursePointsNotSatisfied, entry.InvalidReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrAmbiguousCompletionEvidenceThrowsInsteadOfAccepting(bool ambiguous)
    {
        await using var database = await TestDatabase.CreateAsync();
        var config = CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Required", 100);
        config.RequiredCoursePointIds = [Guid.NewGuid()];
        var rider = Guid.NewGuid();
        var time = DateTime.UtcNow;
        database.Storage.Add(config);
        if (ambiguous)
            foreach (var facility in new[] { Guid.NewGuid(), Guid.NewGuid() })
                database.Storage.Add(CreateSample(rider, config.CoursePointStart, config.CoursePointEnd,
                    time, time.AddSeconds(10), facility));
        await database.Storage.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Service.ProcessCompletedAsync(rider,
            time, time.AddSeconds(10), config.CoursePointStart, config.CoursePointEnd));
        Assert.Empty(await database.Storage.Set<StatisticsEntry>().ToListAsync());
    }

    [Fact]
    public async Task NonpositiveDurationRetainsIndependentCheckpointFailure()
    {
        await using var database = await TestDatabase.CreateAsync();
        var config = CreateSimpleConfig(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Required", 100);
        config.RequiredCoursePointIds = [Guid.NewGuid()];
        var rider = Guid.NewGuid();
        var time = DateTime.UtcNow;
        database.Storage.AddRange(config, CreateSample(rider, config.CoursePointStart,
            config.CoursePointEnd, time, time, Guid.NewGuid()));
        await database.Storage.SaveChangesAsync();
        var entry = Assert.Single(await database.Service.ProcessCompletedAsync(rider,
            time, time, config.CoursePointStart, config.CoursePointEnd));
        Assert.False(entry.RequiredCoursePointsSatisfied);
        Assert.Equal(StatisticsEntryInvalidReason.NonPositiveDuration, entry.InvalidReason);
    }
}
