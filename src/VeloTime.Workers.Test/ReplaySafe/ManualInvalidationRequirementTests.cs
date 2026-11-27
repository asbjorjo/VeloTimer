using static VeloTime.Workers.Test.ReplaySafe.Velodrome;

namespace VeloTime.Workers.Test.ReplaySafe;

/// <summary>
/// Plan §5.4 and §8: manual invalidations survive replays and recomputes when the entry still
/// exists unchanged, and are surfaced for review (never silently dropped or moved) when it does not.
/// </summary>
public sealed class ManualInvalidationRequirementTests
{
    [RequirementFact(PendingPhase = "Phase 2 (S1, S2)")]
    public async Task ManualInvalidation_SurvivesReplayRedeliveryAndALatePassingInTheSameSession()
    {
        await using var world = await PipelineWorlds.CreateAsync();
        await SetUpAsync(world);
        var session = Session("R", Day.AddHours(10), laps: 4, firstSourceId: 1);
        // The last intermediate passing of the session is held back and arrives late.
        var lateIndex = session.Count - 2;
        await world.DeliverEachAsync(session.Where((_, i) => i != lateIndex));
        var invalidated = (await world.SnapshotAsync()).Entries
            .Where(e => e.Item == Lap).OrderBy(e => e.TimeStart).ElementAt(1);
        await world.InvalidateEntryAsync(invalidated.Id, "Crash on lap 2");
        var before = await world.SnapshotAsync();

        await world.DeliverEachAsync([session[lateIndex]]);
        await world.DeliverEachAsync(session);
        await world.RedeliverHistoryAsync();

        var after = await world.SnapshotAsync();
        var kept = Assert.Single(after.Entries, e => e.Id == invalidated.Id);
        Assert.False(kept.IsValid);
        Assert.Equal("Crash on lap 2", kept.ManualNote);
        Assert.Equal((invalidated.Item, invalidated.TimeStart, invalidated.TimeEnd, invalidated.Duration),
            (kept.Item, kept.TimeStart, kept.TimeEnd, kept.Duration));
        Assert.Single(after.Entries, e => e.Item == Lap && e.TimeStart == invalidated.TimeStart);
        Assert.Equal(before.Entries.Count(e => e.Item == Lap), after.Entries.Count(e => e.Item == Lap));
    }

    [RequirementFact(PendingPhase = "Phase 2 (§5.4 manual decision review)")]
    public async Task ManualInvalidationOnAnEntryALatePassingReplaces_IsSurfacedForReview()
    {
        await using var world = await PipelineWorlds.CreateAsync();
        await SetUpAsync(world);
        var session = Session("R", Day.AddHours(10), laps: 4, firstSourceId: 1);
        // The finish passing between laps 2 and 3 is missed at first, so laps 2 and 3 look like one long lap.
        var missedFinish = 6;
        Assert.Equal("L1", session[missedFinish].Loop);
        await world.DeliverEachAsync(session.Where((_, i) => i != missedFinish));
        var longLap = (await world.SnapshotAsync()).Entries
            .Where(e => e.Item == Lap).OrderByDescending(e => e.Duration).First();
        Assert.True(longLap.Duration > TimeSpan.FromSeconds(30), "Expected one merged two-lap 'Lap' entry.");
        await world.InvalidateEntryAsync(longLap.Id, "Missed finish passing");

        await world.DeliverEachAsync([session[missedFinish]]);

        var after = await world.SnapshotAsync();
        Assert.DoesNotContain(after.Entries, e => e.Id == longLap.Id);
        Assert.Equal(4, after.Entries.Count(e => e.Item == Lap));
        // The decision is not silently transferred to the replacement laps.
        Assert.All(after.Entries, e => Assert.Null(e.ManualNote));
        var review = Assert.Single(await world.ManualDecisionReviewsAsync());
        Assert.Equal(longLap.Id, review.EntryId);
        Assert.Equal("Missed finish passing", review.Note);
    }
}
