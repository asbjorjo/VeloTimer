namespace VeloTime.Module.Timing.Model;

public enum PassingConflictReason
{
    /// <summary>The agent re-sent a known SourcePassingId with a different payload.</summary>
    SourcePassingIdPayloadMismatch = 1,
    /// <summary>A different SourcePassingId reported the same transponder at the same timing point and instant.</summary>
    DuplicateObservation = 2,
}

/// <summary>
/// A rejected passing observation kept for review. The recorded passing is never changed by a conflict;
/// the rejected payload is stored as received (after UTC and storage-precision normalisation).
/// </summary>
public class PassingConflict
{
    public Guid Id { get; init; }
    public Guid InstallationId { get; init; }
    public required Guid SourcePassingId { get; init; }
    public PassingConflictReason Reason { get; init; }
    /// <summary>The recorded passing the observation conflicts with.</summary>
    public Guid ExistingPassingId { get; init; }
    public Guid TransponderId { get; init; }
    public Guid TimingPointId { get; init; }
    public DateTime Time { get; init; }
    public bool LowBattery { get; init; }
    public bool LowStrength { get; init; }
    public bool LowHits { get; init; }
    /// <summary>Signal strength in the decoder's own units, as reported. Null when the agent does not send it.</summary>
    public double? Strength { get; init; }
    /// <summary>Hits the decoder counted for the passing. Null when the agent does not send it.</summary>
    public int? Hits { get; init; }
    public DateTime FirstSeenAt { get; init; }
    public DateTime LastSeenAt { get; set; }
    public int Occurrences { get; set; } = 1;
}
