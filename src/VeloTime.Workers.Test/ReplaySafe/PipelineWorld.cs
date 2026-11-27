namespace VeloTime.Workers.Test.ReplaySafe;

/// <summary>
/// One passing as the agent observed it. <see cref="SourcePassingId"/> is the agent's local
/// passing ID, stable across agent replays.
/// </summary>
public sealed record PassingInput(string Agent, long SourcePassingId, string Rider, string Loop, DateTime Time);

/// <summary>A directed course segment, by course point name, with its length in metres.</summary>
public sealed record SegmentInput(string From, string To, double Length);

public enum Redelivery
{
    /// <summary>Every message is consumed once.</summary>
    None,
    /// <summary>Every message on every hop is consumed twice in a row (at-least-once delivery).</summary>
    Immediate,
}

/// <summary>
/// A versioned change to a statistics definition, effective from a given instant.
/// Null members are left unchanged.
/// </summary>
public sealed record ItemChange(TimeSpan? MaxDuration = null, string[]? RequiredCoursePoints = null);

/// <summary>
/// The whole Timing → Statistics pipeline (plus the Facilities configuration it reads) running on
/// fresh PostgreSQL databases. The requirement tests talk only to this surface, expressed in
/// names (riders, loops, course points, items) rather than service-internal IDs, so the tests
/// stay the same while each phase of the plan rewires the implementation behind it.
///
/// <para>An implementation throws <see cref="NotSupportedException"/> for configuration the
/// system under test cannot express yet (for example a dated layout revision on v2); the
/// message names the plan problem ID.</para>
/// </summary>
public interface IPipelineWorld : IAsyncDisposable
{
    // ---- Configuration ----------------------------------------------------------------------

    /// <summary>Creates a facility whose course is the given (closed or open) chain of segments.</summary>
    Task CreateFacilityAsync(string facility, IReadOnlyList<SegmentInput> segments);

    /// <summary>Assigns the agent's installation to a facility from <paramref name="validFrom"/> (null = always).</summary>
    Task AssignInstallationAsync(string agent, string facility, DateTime? validFrom = null);

    /// <summary>Associates an agent loop (timing point) with a course point over [from, to).</summary>
    Task AssociateLoopAsync(string agent, string loop, string coursePoint, DateTime? from = null, DateTime? to = null);

    /// <summary>
    /// Moves loops' open-ended associations to other course points from <paramref name="from"/>,
    /// as one edit (loop → new course point).
    /// </summary>
    Task MoveLoopsAsync(string agent, IReadOnlyDictionary<string, string> moves, DateTime from);

    /// <summary>Defines a Simple (point to point) statistic counting from <paramref name="validFrom"/> (null = always).</summary>
    Task DefineSimpleItemAsync(string name, string start, string end, double distance,
        DateTime? validFrom = null, TimeSpan? maxDuration = null);

    /// <summary>Defines a Multi statistic of <paramref name="repetitions"/> consecutive parent entries.</summary>
    Task DefineMultiItemAsync(string name, string parent, int repetitions, double distance);

    /// <summary>Changes a facility's segment lengths from <paramref name="validFrom"/> (a new layout revision).</summary>
    Task ChangeSegmentLengthsAsync(string facility, IReadOnlyList<SegmentInput> segments, DateTime validFrom);

    /// <summary>Changes a statistics definition from <paramref name="validFrom"/> (a new config version).</summary>
    Task ChangeItemAsync(string name, ItemChange change, DateTime validFrom);

    // ---- Input ------------------------------------------------------------------------------

    /// <summary>Delivers one agent passing to Timing. Downstream work is queued until <see cref="DrainAsync"/>.</summary>
    Task DeliverAsync(PassingInput passing);

    /// <summary>Processes every queued message and pending recompute until the pipeline is idle.</summary>
    Task DrainAsync();

    /// <summary>Delivers each passing and drains after each one, so processing order equals arrival order.</summary>
    async Task DeliverEachAsync(IEnumerable<PassingInput> passings)
    {
        foreach (var passing in passings)
        {
            await DeliverAsync(passing);
            await DrainAsync();
        }
    }

    /// <summary>Re-delivers every message every hop has consumed so far, as a broker redelivery would.</summary>
    Task RedeliverHistoryAsync();

    /// <summary>Manually invalidates an entry, as an administrator would.</summary>
    Task InvalidateEntryAsync(Guid entryId, string note);

    // ---- Observation ------------------------------------------------------------------------

    Task<PipelineSnapshot> SnapshotAsync();

    /// <summary>Manual decisions that a recompute could not keep on an unchanged entry.</summary>
    Task<IReadOnlyList<ManualDecisionReviewRow>> ManualDecisionReviewsAsync();

    /// <summary>Messages that failed every delivery attempt (dead-lettered), for diagnostics.</summary>
    IReadOnlyList<string> DeadLetters { get; }
}

public sealed record SampleRow(Guid Id, string Rider, string From, string To, DateTime TimeStart, DateTime TimeEnd,
    double Distance, string Facility)
{
    public string Content(bool withRider = true) =>
        $"{(withRider ? Rider + " " : "")}{From}->{To} {TimeStart:HH:mm:ss.fff}-{TimeEnd:HH:mm:ss.fff} {Distance:0.###}m @{Facility}";
}

public sealed record EntryRow(Guid Id, string Rider, string Item, DateTime TimeStart, DateTime TimeEnd,
    TimeSpan Duration, double? DistanceMetres, bool IsValid, string? InvalidReason, string? ManualNote, string Facility)
{
    public string Content(bool withRider = true) =>
        $"{(withRider ? Rider + " " : "")}{Item} {TimeStart:HH:mm:ss.fff}-{TimeEnd:HH:mm:ss.fff} " +
        $"{Duration.TotalSeconds:0.###}s {DistanceMetres?.ToString("0.###") ?? "-"}m " +
        $"{(IsValid ? "valid" : "invalid:" + InvalidReason)}{(ManualNote is null ? "" : " note:" + ManualNote)} @{Facility}";
}

public sealed record ManualDecisionReviewRow(Guid EntryId, string Rider, string Item, string Note);

public sealed record PipelineSnapshot(IReadOnlyList<SampleRow> Samples, IReadOnlyList<EntryRow> Entries,
    int TimingPassings)
{
    public PipelineSnapshot ForRider(string rider) =>
        new(Samples.Where(s => s.Rider == rider).ToList(), Entries.Where(e => e.Rider == rider).ToList(), -1);

    public PipelineSnapshot Where(Func<DateTime, bool> timeEnd) =>
        new(Samples.Where(s => timeEnd(s.TimeEnd)).ToList(), Entries.Where(e => timeEnd(e.TimeEnd)).ToList(), -1);

    /// <summary>Row content without IDs, one row per line in a canonical order. Compare with Assert.Equal for a readable diff.</summary>
    public string Content(bool withRider = true) => string.Join('\n',
        Samples.Select(s => "sample " + s.Content(withRider)).Order(StringComparer.Ordinal)
            .Concat(Entries.Select(e => "entry " + e.Content(withRider)).Order(StringComparer.Ordinal)));

    /// <summary>Row content including IDs, for "row for row and ID for ID" comparisons.</summary>
    public string WithIds() => string.Join('\n',
        Samples.Select(s => $"sample {s.Id} {s.Content()}").Order(StringComparer.Ordinal)
            .Concat(Entries.Select(e => $"entry {e.Id} {e.Content()}").Order(StringComparer.Ordinal)));
}
