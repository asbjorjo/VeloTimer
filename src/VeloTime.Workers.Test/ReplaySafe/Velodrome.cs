namespace VeloTime.Workers.Test.ReplaySafe;

/// <summary>
/// The standard test world: one 250 m closed-loop velodrome with three loops, and riders doing
/// laps in sessions. Times are whole milliseconds so PostgreSQL's microsecond precision is exact.
/// </summary>
public static class Velodrome
{
    public const string Agent = "agent-velodrome";
    public const string Facility = "Velodrome";
    public const string Finish = "Finish";
    public const string A = "A";
    public const string B = "B";
    public const string Lap = "Lap";
    public const string Opening = "Opening 100";
    public const string TwoLaps = "Two laps";

    public static readonly DateTime Day = new(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Finish → A 100 m → B 50 m → Finish 100 m.</summary>
    public static readonly IReadOnlyList<SegmentInput> Layout =
    [
        new(Finish, A, 100), new(A, B, 50), new(B, Finish, 100)
    ];

    /// <summary>Loop at each course point, in riding order.</summary>
    public static readonly IReadOnlyDictionary<string, string> Loops = new Dictionary<string, string>
    {
        ["L1"] = Finish, ["L2"] = A, ["L3"] = B
    };

    /// <summary>
    /// Creates the facility, installation, loop associations and the standard items:
    /// Lap (Finish → Finish, 250 m), Opening 100 (Finish → A) and Two laps (Multi of Lap).
    /// </summary>
    public static async Task SetUpAsync(IPipelineWorld world, bool associate = true, bool items = true,
        TimeSpan? lapMaxDuration = null)
    {
        await world.CreateFacilityAsync(Facility, Layout);
        await world.AssignInstallationAsync(Agent, Facility);
        if (associate)
            foreach (var (loop, point) in Loops)
                await world.AssociateLoopAsync(Agent, loop, point);
        if (items) await DefineItemsAsync(world, lapMaxDuration);
    }

    public static async Task DefineItemsAsync(IPipelineWorld world, TimeSpan? lapMaxDuration = null)
    {
        await world.DefineSimpleItemAsync(Lap, Finish, Finish, 250, maxDuration: lapMaxDuration);
        await world.DefineSimpleItemAsync(Opening, Finish, A, 100);
        await world.DefineMultiItemAsync(TwoLaps, Lap, 2, 500);
    }

    /// <summary>
    /// One session of <paramref name="laps"/> laps, starting and ending on the finish loop.
    /// Lap times vary slightly and deterministically (about 17–19 s) so every result is distinct.
    /// </summary>
    public static IReadOnlyList<PassingInput> Session(string rider, DateTime start, int laps, long firstSourceId,
        string agent = Agent)
    {
        var passings = new List<PassingInput>();
        var source = firstSourceId;
        var time = start;
        passings.Add(new(agent, source++, rider, "L1", time));
        for (var lap = 0; lap < laps; lap++)
        {
            var lapMs = 17_000 + (lap * 397 + rider.Length * 131) % 2_000;
            passings.Add(new(agent, source++, rider, "L2", time.AddMilliseconds(lapMs * 2 / 5)));
            passings.Add(new(agent, source++, rider, "L3", time.AddMilliseconds(lapMs * 3 / 5)));
            time = time.AddMilliseconds(lapMs);
            passings.Add(new(agent, source++, rider, "L1", time));
        }
        return passings;
    }

    /// <summary>Two sessions an hour apart (well over the 30 minute session gap).</summary>
    public static IReadOnlyList<PassingInput> TwoSessions(string rider, int laps = 4, long firstSourceId = 1,
        string agent = Agent)
    {
        var first = Session(rider, Day.AddHours(10), laps, firstSourceId, agent);
        var second = Session(rider, Day.AddHours(11), laps, firstSourceId + first.Count, agent);
        return [.. first, .. second];
    }

    /// <summary>The same passings for another rider (a fresh transponder), with fresh source IDs.</summary>
    public static IReadOnlyList<PassingInput> As(this IEnumerable<PassingInput> passings, string rider, long firstSourceId) =>
        passings.Select((p, i) => p with { Rider = rider, SourcePassingId = firstSourceId + i }).ToList();

    public static IReadOnlyList<T> Shuffled<T>(this IReadOnlyList<T> items, int seed)
    {
        var random = new Random(seed);
        var result = items.ToArray();
        random.Shuffle(result);
        return result;
    }

    /// <summary>Moves the passing at <paramref name="index"/> to arrive after the next <paramref name="later"/> passings.</summary>
    public static IReadOnlyList<T> Delayed<T>(this IReadOnlyList<T> items, int index, int later = 1)
    {
        var result = items.ToList();
        var item = result[index];
        result.RemoveAt(index);
        result.Insert(Math.Min(index + later, result.Count), item);
        return result;
    }
}
