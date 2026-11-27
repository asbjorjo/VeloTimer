using static VeloTime.Workers.Test.ReplaySafe.Velodrome;

namespace VeloTime.Workers.Test.ReplaySafe;

/// <summary>
/// Plan §1.2 and §8: duplicate delivery of any message and an agent replay of its whole history
/// create no new rows and change no IDs.
/// </summary>
public sealed class ReplayRequirementTests
{
    /// <summary>Phase 1: Timing stores each observation once, whatever the order, duplication or replay.</summary>
    [RequirementFact]
    public async Task Timing_RecordsEachPassingOnce_UnderShuffleDuplicationAndReplay()
    {
        await using var world = await PipelineWorlds.CreateAsync(Redelivery.Immediate);
        await SetUpAsync(world);
        var day = TwoSessions("R");

        await world.DeliverEachAsync(day.Shuffled(7));
        await world.DeliverEachAsync(day);
        await world.RedeliverHistoryAsync();

        Assert.True(world.DeadLetters.Count == 0, string.Join("\n", world.DeadLetters.Take(3)));
        Assert.Equal(day.Count, (await world.SnapshotAsync()).TimingPassings);
    }

    [RequirementFact(PendingPhase = "Phase 2 (S1, S4)")]
    public async Task EveryMessageDeliveredTwice_MatchesSingleDelivery()
    {
        await using var once = await PipelineWorlds.CreateAsync();
        await using var twice = await PipelineWorlds.CreateAsync(Redelivery.Immediate);
        foreach (var world in new[] { once, twice })
        {
            await SetUpAsync(world);
            await world.DeliverEachAsync(TwoSessions("R"));
        }

        var expected = await once.SnapshotAsync();
        var actual = await twice.SnapshotAsync();
        Assert.NotEmpty(expected.Entries);
        Assert.True(expected.Content() == actual.Content(),
            OrderIndependenceRequirementTests.Difference("Duplicate delivery", expected.Content(), actual.Content(), twice));
        Assert.Equal(expected.TimingPassings, actual.TimingPassings);
    }

    [RequirementFact(PendingPhase = "Phase 2 (S1, S4)")]
    public async Task LateRedeliveryOfEveryConsumedMessage_ChangesNoRowOrId()
    {
        await using var world = await PipelineWorlds.CreateAsync();
        await SetUpAsync(world);
        await world.DeliverEachAsync(TwoSessions("R"));
        var before = await world.SnapshotAsync();

        await world.RedeliverHistoryAsync();

        var after = await world.SnapshotAsync();
        Assert.NotEmpty(before.Entries);
        Assert.True(before.WithIds() == after.WithIds(),
            OrderIndependenceRequirementTests.Difference("Redelivery", before.WithIds(), after.WithIds(), world));
        Assert.Equal(before.TimingPassings, after.TimingPassings);
    }

    [RequirementFact(PendingPhase = "Phase 2 (T1, S1)")]
    public async Task AgentReplayOfAFullDay_CreatesNoNewRows()
    {
        await using var world = await PipelineWorlds.CreateAsync();
        await SetUpAsync(world);
        var day = new[] { "R1", "R2", "R3" }
            .SelectMany((rider, i) => TwoSessions(rider, firstSourceId: 1 + i * 1_000))
            .OrderBy(p => p.Time).ToList();
        await world.DeliverEachAsync(day);
        var before = await world.SnapshotAsync();

        // The agent resends its whole local history with the same source passing IDs.
        await world.DeliverEachAsync(day);

        var after = await world.SnapshotAsync();
        Assert.NotEmpty(before.Entries);
        Assert.Equal(day.Count, before.TimingPassings);
        Assert.Equal(before.TimingPassings, after.TimingPassings);
        Assert.True(before.WithIds() == after.WithIds(),
            OrderIndependenceRequirementTests.Difference("Agent replay", before.WithIds(), after.WithIds(), world));
    }
}
