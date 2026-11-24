using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;

namespace VeloTime.Workers.Test;

// Direct domain calls, using the existing migrated PostgreSQL fixture and ownership transaction.
// These tests deliberately do not depend on the pending worker identity integration.
public sealed partial class StatisticsValidityPostgreSqlTests
{
    [PostgreSqlStatisticsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceAttribution_FixedItemsUseCoveringSourceAndOnlyItsFacilityHistory(bool reverse)
    {
        var rider = Guid.NewGuid();
        var covering = Simple();
        var longer = Simple();
        longer.CoursePointEnd = covering.CoursePointEnd;
        var missingStart = Simple();
        missingStart.CoursePointEnd = covering.CoursePointEnd;
        var first = AttributionSample(rider, covering.CoursePointStart, covering.CoursePointEnd, 0, 10, Guid.NewGuid(), 250);
        var second = AttributionSample(rider, covering.CoursePointStart, covering.CoursePointEnd, 0, 10, Guid.NewGuid(), 250);
        await using (var seed = Db())
        {
            seed.AddRange(covering, longer, missingStart, first, second,
                AttributionSample(rider, longer.CoursePointStart, covering.CoursePointStart, -10, 0, first.FacilityId, 100),
                AttributionSample(rider, longer.CoursePointStart, covering.CoursePointStart, -5, 0, second.FacilityId, 100),
                AttributionSample(rider, missingStart.CoursePointStart, covering.CoursePointStart, -2, 0, first.FacilityId, 100));
            await seed.SaveChangesAsync();
        }
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        await AssertAttributionSources(first, second);
        await AssertAmbiguousAttributionCompletion(first, cache);

        var results = new Dictionary<Guid, List<StatisticsEntry>>();
        foreach (var source in reverse ? new[] { second, first } : new[] { first, second })
            results[source.Id] = await CompleteAttribution(source, cache);
        Assert.Equal(3, results[first.Id].Count);
        Assert.Equal(2, results[second.Id].Count); // No start in this facility, not borrowed from the first.
        foreach (var source in new[] { first, second })
        {
            var entries = results[source.Id];
            Assert.All(entries, e => Assert.Equal(source.FacilityId, e.FacilityId));
            var direct = Assert.Single(entries, e => e.StatisticsItemConfigId == covering.Id);
            Assert.Equal(Start, direct.TimeStart);
            Assert.Equal(25, direct.Speed);
            var extended = Assert.Single(entries, e => e.StatisticsItemConfigId == longer.Id);
            Assert.Equal(Start.AddSeconds(source.Id == first.Id ? -10 : -5), extended.TimeStart);
            Assert.Equal(250 / extended.Duration.TotalSeconds, extended.Speed);
            Assert.All(entries, e => Assert.True(e.IsValid));
        }
        Assert.NotEqual(results[first.Id].Single(e => e.StatisticsItemConfigId == covering.Id).Id,
            results[second.Id].Single(e => e.StatisticsItemConfigId == covering.Id).Id);

        // New same-facility starts would change the longer item's duration if replay rederived it.
        await using (var late = Db())
        {
            late.AddRange(
                AttributionSample(rider, longer.CoursePointStart, covering.CoursePointStart, -1, 0, first.FacilityId, 100),
                AttributionSample(rider, longer.CoursePointStart, covering.CoursePointStart, -1, 0, second.FacilityId, 100));
            await late.SaveChangesAsync();
        }
        foreach (var source in new[] { first, second })
            AssertAttributionReplay(results[source.Id], await CompleteAttribution(source, cache));
        await AssertStoredAttribution(results.Values.SelectMany(e => e), 5);
        await AssertAmbiguousAttributionCompletion(first, cache);
    }

    [PostgreSqlStatisticsTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SourceAttribution_RequiredAndLayoutAttemptsDoNotBorrowOtherFacilityEvidence(bool layout, bool reverse)
    {
        var rider = Guid.NewGuid();
        var config = Simple();
        if (layout)
        {
            config.CoursePointEnd = config.CoursePointStart;
            config.StatisticsItem.DistanceMode = StatisticsDistanceMode.CourseLayout;
        }
        var required = Guid.NewGuid();
        var intermediate = Guid.NewGuid();
        config.RequiredCoursePointIds = [required];
        var firstLayout = Guid.NewGuid();
        var secondLayout = Guid.NewGuid();
        var first = AttributionSample(rider, intermediate, config.CoursePointEnd, 10, 20, Guid.NewGuid(), 100,
            layout ? firstLayout : null, layout ? 400 : null);
        var second = AttributionSample(rider, intermediate, config.CoursePointEnd, 10, 20, Guid.NewGuid(), 100,
            layout ? secondLayout : null, layout ? 500 : null);
        await using (var seed = Db())
        {
            seed.AddRange(config, first, second,
                AttributionSample(rider, config.CoursePointStart, required, 0, 5, first.FacilityId, 100,
                    layout ? firstLayout : null, layout ? 400 : null),
                AttributionSample(rider, required, intermediate, 5, 10, first.FacilityId, 200,
                    layout ? firstLayout : null, layout ? 400 : null),
                // A continuous second-facility path, but without the required point.
                AttributionSample(rider, config.CoursePointStart, intermediate, -5, 10, second.FacilityId, 400,
                    layout ? secondLayout : null, layout ? 500 : null));
            await seed.SaveChangesAsync();
        }
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        await AssertAttributionSources(first, second);
        await AssertAmbiguousAttributionCompletion(first, cache);
        var results = new Dictionary<Guid, StatisticsEntry>();
        foreach (var source in reverse ? new[] { second, first } : new[] { first, second })
            results[source.Id] = Assert.Single(await CompleteAttribution(source, cache));
        var valid = results[first.Id];
        var invalid = results[second.Id];
        Assert.NotEqual(valid.Id, invalid.Id);
        Assert.Equal(first.FacilityId, valid.FacilityId);
        Assert.Equal(second.FacilityId, invalid.FacilityId);
        Assert.Equal(Start, valid.TimeStart);
        Assert.Equal(Start.AddSeconds(-5), invalid.TimeStart);
        Assert.True(valid.RequiredCoursePointsSatisfied);
        Assert.True(valid.IsValid);
        Assert.Null(valid.InvalidReason);
        Assert.False(invalid.RequiredCoursePointsSatisfied);
        Assert.False(invalid.IsValid);
        Assert.Equal(StatisticsEntryInvalidReason.RequiredCoursePointsNotSatisfied, invalid.InvalidReason);
        Assert.Equal(layout ? 400d : null, valid.DistanceMetres);
        Assert.Equal(layout ? 500d : null, invalid.DistanceMetres);
        Assert.Equal((layout ? 400 : 250) / 20d, valid.Speed);
        Assert.Equal((layout ? 500 : 250) / 25d, invalid.Speed);

        // Late checkpoint evidence and a newer start must not reassess the second facility's
        // original invalid result, including its earlier start and captured geometry.
        await using (var late = Db())
        {
            late.AddRange(
                AttributionSample(rider, config.CoursePointStart, required, 1, 5, second.FacilityId, 100,
                    layout ? secondLayout : null, layout ? 500 : null),
                AttributionSample(rider, required, intermediate, 5, 10, second.FacilityId, 300,
                    layout ? secondLayout : null, layout ? 500 : null));
            await late.SaveChangesAsync();
        }
        foreach (var source in new[] { first, second })
            AssertAttributionReplay([results[source.Id]], await CompleteAttribution(source, cache));
        await AssertStoredAttribution(results.Values, 2);
        await AssertAmbiguousAttributionCompletion(first, cache);
    }

    [PostgreSqlStatisticsFact]
    public async Task SourceAttribution_MultiUsesExactEqualPayloadParentFacilityAndValidityOnReplay()
    {
        var rider = Guid.NewGuid();
        var parent = Simple();
        var multi = ReplayMulti(parent);
        var first = Entry(parent, 1, rider, Guid.NewGuid(), Start, Start.AddSeconds(20));
        first.IsValid = false;
        first.InvalidReason = StatisticsEntryInvalidReason.BelowMinimumDuration;
        var second = Entry(parent, 2, rider, Guid.NewGuid(), Start, Start.AddSeconds(20));
        await using (var seed = Db())
        {
            seed.AddRange(parent, multi, first, second);
            await seed.SaveChangesAsync();
            await DecisionReceipt(seed, first);
            await DecisionReceipt(seed, second);
        }
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        var firstMulti = Assert.Single(await AggregateAttribution(first, cache));
        var secondMulti = Assert.Single(await AggregateAttribution(second, cache));
        Assert.NotEqual(firstMulti.Id, secondMulti.Id);
        Assert.Equal(first.FacilityId, firstMulti.FacilityId);
        Assert.Equal(second.FacilityId, secondMulti.FacilityId);
        Assert.False(firstMulti.IsValid);
        Assert.Equal(StatisticsEntryInvalidReason.InvalidContributor, firstMulti.InvalidReason);
        Assert.True(secondMulti.IsValid); // Invalid parent in another facility is not a contributor.
        Assert.Null(secondMulti.InvalidReason);
        Assert.Equal(Start, firstMulti.TimeStart);
        Assert.Equal(Start, secondMulti.TimeStart);
        Assert.Equal(Start.AddSeconds(20), firstMulti.TimeEnd);
        Assert.Equal(Start.AddSeconds(20), secondMulti.TimeEnd);
        Assert.Equal(12.5, firstMulti.Speed);
        Assert.Equal(12.5, secondMulti.Speed);
        await using (var db = Db())
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new StatisticsOwnershipService(db, new RequiredPointEmptyOwnershipReader())
                .CreateEntriesAsync(rider, ct => new StatisticsService(db, cache).ProcessEntryAsync(rider,
                    parent.StatisticsItemId, parent.Id, Start, Start.AddSeconds(20), ct), CancellationToken.None));
            Assert.Equal("EntryCreated matches multiple persisted parent entries; an entry ID is required.", error.Message);
        }
        await using (var late = Db())
        {
            // Makes the originally valid Single window have two contributors if reclassified.
            late.Add(Entry(parent, 3, rider, second.FacilityId, Start.AddSeconds(5), Start.AddSeconds(19)));
            await late.SaveChangesAsync();
        }
        AssertAttributionReplay([firstMulti], await AggregateAttribution(first, cache));
        AssertAttributionReplay([secondMulti], await AggregateAttribution(second, cache));
        await AssertStoredAttribution([firstMulti, secondMulti], 5);
    }

    [PostgreSqlStatisticsFact]
    public async Task SourceAttribution_MultiTiedLaterStartCannotExcludeItsTrigger()
    {
        var rider = Guid.NewGuid();
        var parent = Simple();
        var multi = ReplayMulti(parent);
        multi.Repetitions = 2;
        multi.StatisticsItem.Distance = 500;
        var facility = Guid.NewGuid();
        // Both later-starting entries have larger UUIDs: a newest-two query would exclude
        // the trigger. The newest predecessor also belongs to a different facility.
        var trigger = Entry(parent, 1, rider, facility, Start, Start.AddSeconds(20));
        var tied = Entry(parent, 2, rider, facility, Start.AddSeconds(10), Start.AddSeconds(20));
        var decoy = Entry(parent, 3, rider, Guid.NewGuid(), Start.AddSeconds(12), Start.AddSeconds(20));
        decoy.IsValid = false;
        decoy.InvalidReason = StatisticsEntryInvalidReason.BelowMinimumDuration;
        await using (var seed = Db())
        {
            seed.AddRange(parent, trigger, tied, decoy);
            await seed.SaveChangesAsync();
            // These parents were handled before the Multi definition existed.
            await DecisionReceipt(seed, tied);
            await DecisionReceipt(seed, decoy);
            await DecisionReceipt(seed, trigger);
            await using var preServices = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
            var preCache = preServices.GetRequiredService<HybridCache>();
            await ProcessPriorDecision(seed, preCache, tied);
            await ProcessPriorDecision(seed, preCache, decoy);
            seed.Add(multi);
            await seed.SaveChangesAsync();
        }
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        var result = Assert.Single(await AggregateAttribution(trigger, cache));
        Assert.Equal(facility, result.FacilityId);
        Assert.Equal(trigger.TimeStart, result.TimeStart);
        Assert.Equal(trigger.TimeEnd, result.TimeEnd);
        Assert.Equal(TimeSpan.FromSeconds(20), result.Duration);
        Assert.Equal(25, result.Speed);
        Assert.True(result.IsValid); // Exactly two same-facility parents, including the trigger.
        Assert.Null(result.InvalidReason);
        await using (var late = Db())
        {
            late.Add(Entry(parent, 4, rider, facility, Start.AddSeconds(11), Start.AddSeconds(19)));
            await late.SaveChangesAsync();
        }
        AssertAttributionReplay([result], await AggregateAttribution(trigger, cache));
        await AssertStoredAttribution([result], 5);
    }

    [PostgreSqlStatisticsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceAttribution_LayoutCannotBorrowValidGeometryFromEqualPayloadOtherFacility(bool missing)
    {
        var rider = Guid.NewGuid();
        var config = Simple();
        config.CoursePointEnd = config.CoursePointStart;
        config.StatisticsItem.DistanceMode = StatisticsDistanceMode.CourseLayout;
        var intermediate = Guid.NewGuid();
        var layout = Guid.NewGuid();
        var good = AttributionSample(rider, intermediate, config.CoursePointEnd, 10, 20, Guid.NewGuid(), 100, layout, 400);
        var bad = AttributionSample(rider, intermediate, config.CoursePointEnd, 10, 20, Guid.NewGuid(), 100,
            missing ? null : Guid.NewGuid(), missing ? null : 500);
        await using (var seed = Db())
        {
            seed.AddRange(config, good, bad,
                AttributionSample(rider, config.CoursePointStart, intermediate, 0, 10, good.FacilityId, 300, layout, 400),
                // Bad completion either has no geometry, or conflicts with its own path.
                AttributionSample(rider, config.CoursePointStart, intermediate, 0, 10, bad.FacilityId, 300, layout, 400));
            await seed.SaveChangesAsync();
        }
        await using var services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        await AssertAttributionSources(good, bad);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CompleteAttribution(bad, cache));
        Assert.Contains(missing ? "completion has missing" : "path has missing or conflicting", error.Message);
        await using (var verify = Db())
            Assert.Empty(await verify.Set<StatisticsEntry>().ToListAsync());
        var result = Assert.Single(await CompleteAttribution(good, cache));
        Assert.Equal(good.FacilityId, result.FacilityId);
        Assert.Equal(400, result.DistanceMetres);
        Assert.Equal(20, result.Speed);
        Assert.True(result.IsValid);
        await AssertStoredAttribution([result], 1);
    }

    private static Sample AttributionSample(Guid rider, Guid from, Guid to, int start, int end,
        Guid facility, double distance, Guid? layout = null, double? length = null) => new()
    {
        Id = Guid.NewGuid(), TransponderId = rider, FacilityId = facility,
        TimingPointStartId = Guid.NewGuid(), TimingPointEndId = Guid.NewGuid(),
        CoursePointStartId = from, CoursePointEndId = to,
        TimeStart = Start.AddSeconds(start), TimeEnd = Start.AddSeconds(end),
        Duration = TimeSpan.FromSeconds(end - start), Distance = distance, Speed = distance / (end - start),
        CourseLayoutId = layout, ClosedLoopDistanceMetres = length
    };

    private async Task<List<StatisticsEntry>> CompleteAttribution(Sample source, HybridCache cache)
    {
        await using var db = Db();
        return (await new StatisticsOwnershipService(db, new RequiredPointEmptyOwnershipReader())
            .CreateEntriesAsync(source.TransponderId, ct => new StatisticsService(db, cache).ProcessCompletedAsync(
                source.TransponderId, source.TimeStart, source.TimeEnd, source.CoursePointStartId,
                source.CoursePointEndId, ct, source.Id, source.Distance), CancellationToken.None)).ToList();
    }

    private async Task<List<StatisticsEntry>> AggregateAttribution(StatisticsEntry source, HybridCache cache)
    {
        await using var db = Db();
        return (await new StatisticsOwnershipService(db, new RequiredPointEmptyOwnershipReader())
            .CreateEntriesAsync(source.TransponderId, ct => new StatisticsService(db, cache).ProcessEntryAsync(
                source.TransponderId, source.StatisticsItemId, source.StatisticsItemConfigId,
                source.TimeStart, source.TimeEnd, ct, source.Id), CancellationToken.None)).ToList();
    }

    private async Task AssertAmbiguousAttributionCompletion(Sample source, HybridCache cache)
    {
        await using var db = Db();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new StatisticsOwnershipService(db, new RequiredPointEmptyOwnershipReader())
                .CreateEntriesAsync(source.TransponderId, ct => new StatisticsService(db, cache).ProcessCompletedAsync(
                    source.TransponderId, source.TimeStart, source.TimeEnd, source.CoursePointStartId,
                    source.CoursePointEndId, ct, null, source.Distance), CancellationToken.None));
        Assert.Equal("SampleComplete matches multiple persisted source samples; a source sample ID is required.", error.Message);
    }

    private async Task AssertAttributionSources(Sample first, Sample second)
    {
        await using var db = Db();
        var a = await db.Set<Sample>().SingleAsync(s => s.Id == first.Id);
        var b = await db.Set<Sample>().SingleAsync(s => s.Id == second.Id);
        Assert.Equal((a.TransponderId, a.TimeStart, a.TimeEnd, a.CoursePointStartId, a.CoursePointEndId, a.Distance),
            (b.TransponderId, b.TimeStart, b.TimeEnd, b.CoursePointStartId, b.CoursePointEndId, b.Distance));
        Assert.NotEqual(a.Id, b.Id);
        Assert.NotEqual(a.FacilityId, b.FacilityId);
        Assert.NotEqual(a.TimingPointStartId, b.TimingPointStartId);
        Assert.NotEqual(a.TimingPointEndId, b.TimingPointEndId);
        Assert.All(new[] { a, b }, s =>
        {
            Assert.Equal(DateTimeKind.Utc, s.TimeStart.Kind);
            Assert.Equal(DateTimeKind.Utc, s.TimeEnd.Kind);
            Assert.Equal(s.TimeEnd - s.TimeStart, s.Duration);
            Assert.Equal(s.Distance / s.Duration.TotalSeconds, s.Speed);
        });
    }

    private static void AssertAttributionReplay(IEnumerable<StatisticsEntry> expected, IEnumerable<StatisticsEntry> actual)
    {
        var stored = actual.ToDictionary(e => e.Id);
        Assert.Equal(expected.Count(), stored.Count);
        foreach (var entry in expected)
        {
            var replay = stored[entry.Id];
            Assert.Equal((entry.FacilityId, entry.TimeStart, entry.TimeEnd, entry.Duration, entry.Speed,
                    entry.DistanceMetres, entry.RequiredCoursePointsSatisfied, entry.IsValid, entry.InvalidReason),
                (replay.FacilityId, replay.TimeStart, replay.TimeEnd, replay.Duration, replay.Speed,
                    replay.DistanceMetres, replay.RequiredCoursePointsSatisfied, replay.IsValid, replay.InvalidReason));
        }
    }

    private async Task AssertStoredAttribution(IEnumerable<StatisticsEntry> expected, int totalRows)
    {
        await using var db = Db();
        var rows = await db.Set<StatisticsEntry>().AsNoTracking().ToListAsync();
        Assert.Equal(totalRows, rows.Count);
        var ids = expected.Select(e => e.Id).ToHashSet();
        AssertAttributionReplay(expected, rows.Where(e => ids.Contains(e.Id)));
        Assert.All(rows, e =>
        {
            Assert.Equal(DateTimeKind.Utc, e.TimeStart.Kind);
            Assert.Equal(DateTimeKind.Utc, e.TimeEnd.Kind);
            Assert.Equal(e.TimeEnd - e.TimeStart, e.Duration);
        });
    }
}
