namespace VeloTime.Agent.Model;

public class Passing : IModel
{
    /// <summary>
    /// The agent's passing ID; sent to Timing as <c>PassingObservedV1.SourcePassingId</c>. Derive it with
    /// <see cref="Identity.PassingId.Create(string, ReadOnlySpan{byte})"/> so resends and replays keep the same ID.
    /// </summary>
    public required Guid Id { get; init; }
    public DateTime Time { get; init; }
    public required string TransponderType { get; init; }
    public required string TransponderId { get; init; }
    public required string LoopId { get; init; }
    public bool LowBattery { get; init; }
    public bool LowStrength { get; init; }
    public bool LowHits { get; init; }
    /// <summary>Signal strength in the decoder's own units, as reported. Null when the decoder does not report it.</summary>
    public double? Strength { get; init; }
    /// <summary>Hits the decoder counted for the passing. Null when the decoder does not report it.</summary>
    public int? Hits { get; init; }

    /// <summary>
    /// The passing ID as reported by the decoder or SDK (for example a P3 passing number or an X2 SDK passing ID), kept
    /// for tracing a passing back to its source. Optional, not unique across decoders or counter resets, not the
    /// passing key and not sent to Timing.
    /// </summary>
    public string? HardwarePassingId { get; init; }
}
