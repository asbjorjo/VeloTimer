using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Service.Recompute;

namespace VeloTime.Module.Statistics.Test;

public sealed partial class StatisticsServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CourseLayoutLap_DirectOrMultipleSegments_MeasuresTheLoopDeterministically(bool multipleSegments)
    {
        var rider = Guid.NewGuid();
        var finish = Guid.NewGuid();
        var intermediate = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var layout = Guid.NewGuid();
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var config = LayoutLapConfig(finish);
        var samples = new List<Sample>();
        if (multipleSegments)
            samples.Add(LapSample(rider, finish, intermediate, start, start.AddSeconds(10), facility, layout, 333.33, 100));
        var segmentStart = multipleSegments ? start.AddSeconds(10) : start;
        var segmentPoint = multipleSegments ? intermediate : finish;
        samples.Add(LapSample(rider, segmentPoint, finish, segmentStart, start.AddSeconds(25), facility, layout, 333.33,
            multipleSegments ? 233.33 : 333.33));

        var entry = Assert.Single(Derive(config, samples));
        Assert.Equal(start, entry.TimeStart);
        Assert.Equal(start.AddSeconds(25), entry.TimeEnd);
        Assert.Equal(TimeSpan.FromSeconds(25), entry.Duration);
        Assert.Equal(333.33, entry.DistanceMetres);
        Assert.Equal(333.33 / 25, entry.Speed, 8);
        Assert.Equal(facility, entry.FacilityId);
        Assert.True(entry.IsValid);

        // Deriving the same facts again yields the same row.
        var again = Assert.Single(Derive(config, samples.AsEnumerable().Reverse()));
        Assert.Equal(entry.Id, again.Id);
        Assert.Equal(entry.Speed, again.Speed);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("conflicting-layout")]
    [InlineData("conflicting-distance")]
    public void CourseLayoutLap_RejectsMissingOrConflictingPathEvidence(string problem)
    {
        var rider = Guid.NewGuid();
        var finish = Guid.NewGuid();
        var intermediate = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var layout = Guid.NewGuid();
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var config = LayoutLapConfig(finish);
        var rejections = new List<DerivationRejection>();

        Assert.Empty(Derive(config, rejections, [
            LapSample(rider, finish, intermediate, start, start.AddSeconds(10), facility,
                problem == "missing" ? null : problem == "conflicting-layout" ? Guid.NewGuid() : layout,
                problem == "missing" ? null : problem == "conflicting-distance" ? 250 : 333.33, 100),
            LapSample(rider, intermediate, finish, start.AddSeconds(10), start.AddSeconds(25), facility, layout, 333.33, 233.33)]));
        var rejection = Assert.Single(rejections);
        Assert.Contains("layout/length evidence", rejection.Reason);
        Assert.Equal(config.Id, rejection.ConfigId);
    }

    [Fact]
    public void CourseLayoutLap_TwoWrapsWithoutAnInteriorFinish_AreRejected()
    {
        var rider = Guid.NewGuid();
        var finish = Guid.NewGuid();
        var at300 = Guid.NewGuid();
        var at200 = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var layout = Guid.NewGuid();
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var config = LayoutLapConfig(finish);
        var rejections = new List<DerivationRejection>();

        // On a 400 m loop these observed edges wrap twice: 0->300, 300->200, 200->0.
        Assert.Empty(Derive(config, rejections, [
            LapSample(rider, finish, at300, start, start.AddSeconds(10), facility, layout, 400, 300),
            LapSample(rider, at300, at200, start.AddSeconds(10), start.AddSeconds(20), facility, layout, 400, 300),
            LapSample(rider, at200, finish, start.AddSeconds(20), start.AddSeconds(30), facility, layout, 400, 200)]));
        Assert.Contains("exactly one closed-loop distance", Assert.Single(rejections).Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CourseLayoutLap_EquivalentDuplicateEdgesAreCountedOnceAndConflictsRejected(bool conflict)
    {
        var rider = Guid.NewGuid();
        var finish = Guid.NewGuid();
        var intermediate = Guid.NewGuid();
        var facility = Guid.NewGuid();
        var layout = Guid.NewGuid();
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var config = LayoutLapConfig(finish);
        var rejections = new List<DerivationRejection>();

        var entries = Derive(config, rejections, [
            LapSample(rider, finish, intermediate, start, start.AddSeconds(10), facility, layout, 400, 300),
            LapSample(rider, finish, intermediate, start, start.AddSeconds(10), facility, layout, 400, conflict ? 299 : 300),
            LapSample(rider, intermediate, finish, start.AddSeconds(10), start.AddSeconds(20), facility, layout, 400, 100)]);

        if (conflict)
        {
            Assert.Empty(entries);
            Assert.Contains("conflicting sample-distance evidence", Assert.Single(rejections).Reason);
        }
        else
        {
            var entry = Assert.Single(entries);
            Assert.Equal(400, entry.DistanceMetres);
            Assert.Equal(20, entry.Speed);
            Assert.Empty(rejections);
        }
    }

    [Fact]
    public void CourseLayoutLap_WithMultiDependents_IsRejectedNotFatal()
    {
        var finish = Guid.NewGuid();
        var layout = Guid.NewGuid();
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var config = LayoutLapConfig(finish);
        var rejections = new List<DerivationRejection>();

        Assert.Empty(EntryDerivation.Simple(config, StatisticsDurationBounds.Unbounded,
            [LapSample(Guid.NewGuid(), finish, finish, start, start.AddSeconds(20), Guid.NewGuid(), layout, 400, 400)],
            hasMultiDependents: true, rejections));
        Assert.Contains("Multi statistics are not supported", Assert.Single(rejections).Reason);
    }

    private static SimpleStatisticsItemConfig LayoutLapConfig(Guid finish)
    {
        var config = CreateSimpleConfig(Guid.NewGuid(), finish, finish, "Lap", 0);
        config.StatisticsItem.DistanceMode = StatisticsDistanceMode.CourseLayout;
        return config;
    }

    private static Sample LapSample(Guid rider, Guid from, Guid to, DateTime start, DateTime end,
        Guid facility, Guid? layout, double? length, double segmentDistance)
    {
        var sample = CreateSample(rider, from, to, start, end, facility);
        sample.Distance = segmentDistance;
        sample.Speed = segmentDistance / (end - start).TotalSeconds;
        sample.CourseLayoutId = layout;
        sample.ClosedLoopDistanceMetres = length;
        return sample;
    }
}
