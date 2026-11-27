using static VeloTime.Workers.Test.ReplaySafe.Velodrome;

namespace VeloTime.Workers.Test.ReplaySafe;

/// <summary>
/// Plan §1.1 and §8: a passing may arrive after later passings for the same rider, and the stored
/// samples and entries must equal what in-order arrival produces. Each arrival order is ridden by
/// its own rider in one world, so the comparison is per rider with the rider label removed.
/// </summary>
public sealed class OrderIndependenceRequirementTests
{
    /// <summary>Checks the pipeline driver and the scenario before the stricter requirements. Passed on v2.</summary>
    [RequirementFact]
    public async Task InOrderArrival_OneSession_ProducesEveryLapAndSplit()
    {
        await using var world = await PipelineWorlds.CreateAsync();
        await SetUpAsync(world);

        await world.DeliverEachAsync(Session("R", Day.AddHours(10), laps: 4, firstSourceId: 1));

        var snapshot = await world.SnapshotAsync();
        Assert.True(world.DeadLetters.Count == 0, string.Join("\n", world.DeadLetters.Take(3)));
        Assert.Equal(13, snapshot.TimingPassings);
        Assert.Equal(12, snapshot.Samples.Count);
        Assert.Equal(4, snapshot.Entries.Count(e => e.Item == Lap));
        Assert.Equal(4, snapshot.Entries.Count(e => e.Item == Opening));
        Assert.Equal(3, snapshot.Entries.Count(e => e.Item == TwoLaps));
        Assert.All(snapshot.Entries.Where(e => e.Item == Lap), e => Assert.InRange(e.Duration.TotalSeconds, 17, 19));
        Assert.All(snapshot.Samples, s => Assert.Equal(Facility, s.Facility));
    }

    [RequirementFact]
    public async Task InOrderArrival_TwoSessions_NoSampleOrLapSpansTheGap()
    {
        await using var world = await PipelineWorlds.CreateAsync();
        await SetUpAsync(world);

        await world.DeliverEachAsync(TwoSessions("R"));

        var snapshot = await world.SnapshotAsync();
        Assert.True(world.DeadLetters.Count == 0, string.Join("\n", world.DeadLetters.Take(3)));
        // 2 sessions × 4 laps, three segments per lap; the hour between sessions is not a sample.
        Assert.Equal(24, snapshot.Samples.Count);
        Assert.Equal(8, snapshot.Entries.Count(e => e.Item == Lap));
        Assert.Equal(8, snapshot.Entries.Count(e => e.Item == Opening));
        Assert.All(snapshot.Entries.Where(e => e.Item == Lap), e => Assert.InRange(e.Duration.TotalSeconds, 17, 19));
    }

    [RequirementFact]
    public async Task MultiWindows_NeverSpanTwoSessions()
    {
        await using var world = await PipelineWorlds.CreateAsync();
        await SetUpAsync(world);

        await world.DeliverEachAsync(TwoSessions("R"));

        var twoLaps = (await world.SnapshotAsync()).Entries.Where(e => e.Item == TwoLaps).ToList();
        Assert.NotEmpty(twoLaps);
        Assert.All(twoLaps, e => Assert.True(e.Duration < TimeSpan.FromMinutes(30),
            $"{e.Content()} chains laps from two sessions."));
    }

    [RequirementFact]
    public async Task LatePassing_P1P3P2_MatchesInOrderArrival()
    {
        await using var world = await PipelineWorlds.CreateAsync();
        await SetUpAsync(world);
        var inOrder = TwoSessions("R-ordered");
        var late = new Dictionary<string, IReadOnlyList<PassingInput>>
        {
            // One finish passing in the middle of a session arrives after the next passing.
            ["R-late-finish"] = inOrder.As("R-late-finish", 1_000).Delayed(3),
            // An intermediate loop passing arrives after the following finish passing.
            ["R-late-split"] = inOrder.As("R-late-split", 2_000).Delayed(5),
            // The first passing of a session arrives after the whole session.
            ["R-late-start"] = inOrder.As("R-late-start", 3_000).Delayed(0, 12),
        };

        await world.DeliverEachAsync(inOrder);
        foreach (var passings in late.Values) await world.DeliverEachAsync(passings);

        var snapshot = await world.SnapshotAsync();
        var expected = snapshot.ForRider("R-ordered").Content(withRider: false);
        Assert.NotEmpty(expected);
        foreach (var rider in late.Keys)
            Assert.True(expected == snapshot.ForRider(rider).Content(withRider: false),
                Difference(rider, expected, snapshot.ForRider(rider).Content(withRider: false), world));
    }

    /// <summary>Property test: a fixed sample of permutations, so failures are reproducible by seed.</summary>
    [RequirementFact]
    public async Task ShuffledArrival_MatchesInOrderArrival_ForEveryPermutation()
    {
        await using var world = await PipelineWorlds.CreateAsync();
        await SetUpAsync(world);
        var inOrder = TwoSessions("R-ordered");
        var permutations = new Dictionary<string, IReadOnlyList<PassingInput>>
        {
            ["R-reversed"] = inOrder.As("R-reversed", 10_000).Reverse().ToList(),
        };
        foreach (var seed in Enumerable.Range(1, 10))
            permutations[$"R-seed-{seed}"] = inOrder.As($"R-seed-{seed}", 10_000 + seed * 100).Shuffled(seed);

        await world.DeliverEachAsync(inOrder);
        foreach (var passings in permutations.Values) await world.DeliverEachAsync(passings);

        var snapshot = await world.SnapshotAsync();
        var expected = snapshot.ForRider("R-ordered").Content(withRider: false);
        Assert.NotEmpty(expected);
        var failed = permutations.Keys
            .Where(rider => snapshot.ForRider(rider).Content(withRider: false) != expected)
            .ToList();
        Assert.True(failed.Count == 0,
            $"{failed.Count} of {permutations.Count} arrival orders differ from in-order arrival: {string.Join(", ", failed)}.\n" +
            (failed.Count > 0 ? Difference(failed[0], expected, snapshot.ForRider(failed[0]).Content(withRider: false), world) : ""));
    }

    internal static string Difference(string label, string expected, string actual, IPipelineWorld world)
    {
        var expectedLines = expected.Split('\n').ToHashSet();
        var actualLines = actual.Split('\n').ToHashSet();
        return $"{label} differs from the expected result.\n" +
               $"Missing:\n  {string.Join("\n  ", expectedLines.Except(actualLines))}\n" +
               $"Unexpected:\n  {string.Join("\n  ", actualLines.Except(expectedLines))}\n" +
               $"Dead letters: {world.DeadLetters.Count}\n  {string.Join("\n  ", world.DeadLetters.Take(5))}";
    }
}
