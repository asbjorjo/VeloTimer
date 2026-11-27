using static VeloTime.Workers.Test.ReplaySafe.Velodrome;

namespace VeloTime.Workers.Test.ReplaySafe;

/// <summary>
/// Plan §1.3–1.4 and §8: backdating a config item to time X recomputes exactly the results with
/// events at or after X, and they then match a fresh in-order run that had the new config from the
/// start. Each test compares an "edited" world (data first, backdated edit after) with a "fresh"
/// world (same final config history first, data after), and checks results before X are untouched.
/// </summary>
public sealed class BackdatedConfigRequirementTests
{
    /// <summary>The edit takes effect at the start of the second session.</summary>
    private static readonly DateTime X = Day.AddHours(11);

    private static IReadOnlyList<PassingInput> Data() =>
        [.. TwoSessions("R1"), .. TwoSessions("R2", firstSourceId: 1_000)];

    [RequirementFact]
    public async Task MissingAssociationAddedLater_ProducesTheSamplesItWouldHaveProduced()
    {
        // L3 is first left unassociated: its passings are stored but cannot produce samples (§5.3).
        await AssertBackdatedEditMatchesFreshRun(
            setUp: async w =>
            {
                await SetUpAsync(w, associate: false);
                await w.AssociateLoopAsync(Velodrome.Agent, "L1", Finish);
                await w.AssociateLoopAsync(Velodrome.Agent, "L2", A);
            },
            edit: w => w.AssociateLoopAsync(Velodrome.Agent, "L3", B),
            unaffectedBefore: null);
    }

    [RequirementFact]
    public async Task LoopsSwappedFromX_RecomputeOnlyResultsFromX()
    {
        // The installer reports L2 and L3 were cabled the wrong way round from X.
        await AssertBackdatedEditMatchesFreshRun(
            setUp: w => SetUpAsync(w),
            edit: w => w.MoveLoopsAsync(Velodrome.Agent, new Dictionary<string, string> { ["L2"] = B, ["L3"] = A }, X),
            unaffectedBefore: X);
    }

    [RequirementFact]
    public async Task SegmentLengthsRevisedFromX_RecomputeOnlyResultsFromX()
    {
        await AssertBackdatedEditMatchesFreshRun(
            setUp: w => SetUpAsync(w),
            edit: w => w.ChangeSegmentLengthsAsync(Facility,
                [new(Finish, A, 90), new(A, B, 60), new(B, Finish, 100)], X),
            unaffectedBefore: X);
    }

    [RequirementFact(PendingPhase = "Phase 4 (C2)")]
    public async Task InstallationMovedToAnotherFacilityFromX_AttributesOnlyLaterResultsToIt()
    {
        const string indoor = "Indoor";
        await AssertBackdatedEditMatchesFreshRun(
            setUp: async w =>
            {
                await SetUpAsync(w);
                await w.CreateFacilityAsync(indoor,
                    [new("Indoor finish", "Indoor A", 100), new("Indoor A", "Indoor B", 50), new("Indoor B", "Indoor finish", 100)]);
            },
            edit: async w =>
            {
                await w.AssignInstallationAsync(Velodrome.Agent, indoor, X);
                await w.MoveLoopsAsync(Velodrome.Agent, new Dictionary<string, string>
                {
                    ["L1"] = "Indoor finish", ["L2"] = "Indoor A", ["L3"] = "Indoor B"
                }, X);
            },
            unaffectedBefore: X,
            check: snapshot => Assert.Contains(snapshot.Samples, s => s.Facility == indoor));
    }

    [RequirementFact(PendingPhase = "Phase 4 (C3, decision §6.1: dated limits)")]
    public async Task LapThresholdTightenedFromX_InvalidatesOnlyLaterLaps()
    {
        await AssertBackdatedEditMatchesFreshRun(
            setUp: w => SetUpAsync(w),
            edit: w => w.ChangeItemAsync(Lap, new ItemChange(MaxDuration: TimeSpan.FromSeconds(17.5)), X),
            unaffectedBefore: X,
            check: snapshot =>
            {
                Assert.Contains(snapshot.Entries, e => e.Item == Lap && e.TimeEnd >= X && !e.IsValid);
                Assert.All(snapshot.Entries.Where(e => e.Item == Lap && e.TimeEnd < X), e => Assert.True(e.IsValid));
            });
    }

    [RequirementFact]
    public async Task ItemDefinedAfterTheData_CountsHistoryLikeAFreshRun()
    {
        await AssertBackdatedEditMatchesFreshRun(
            setUp: w => SetUpAsync(w, items: false),
            edit: w => DefineItemsAsync(w),
            unaffectedBefore: null);
    }

    /// <param name="unaffectedBefore">When set, results ending before it must be the same as before the edit.</param>
    private static async Task AssertBackdatedEditMatchesFreshRun(Func<IPipelineWorld, Task> setUp,
        Func<IPipelineWorld, Task> edit, DateTime? unaffectedBefore, Action<PipelineSnapshot>? check = null)
    {
        var data = Data();
        await using var edited = await PipelineWorlds.CreateAsync();
        await setUp(edited);
        await edited.DeliverEachAsync(data);
        var original = await edited.SnapshotAsync();
        await edit(edited);
        await edited.DrainAsync();
        var actual = await edited.SnapshotAsync();

        await using var fresh = await PipelineWorlds.CreateAsync();
        await setUp(fresh);
        await edit(fresh);
        await fresh.DeliverEachAsync(data);
        var expected = await fresh.SnapshotAsync();

        Assert.NotEmpty(expected.Entries);
        check?.Invoke(expected);
        Assert.True(expected.Content() == actual.Content(),
            OrderIndependenceRequirementTests.Difference("The backdated edit", expected.Content(), actual.Content(), edited));
        if (unaffectedBefore is { } x)
        {
            var before = original.Where(end => end < x);
            var after = actual.Where(end => end < x);
            Assert.True(before.WithIds() == after.WithIds(),
                OrderIndependenceRequirementTests.Difference("Results before X", before.WithIds(), after.WithIds(), edited));
        }
    }
}
