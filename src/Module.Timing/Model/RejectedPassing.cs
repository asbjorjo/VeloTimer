namespace VeloTime.Module.Timing.Model;

public enum PassingRejectionReason
{
    /// <summary>The observation's <c>TransponderType</c> is not one Timing supports.</summary>
    UnsupportedTransponderType = 1,
    /// <summary>The <c>TransponderId</c> is not valid for its transponder type.</summary>
    InvalidTransponderId = 2,
}

/// <summary>
/// An agent observation Timing could not resolve, stored as received so it can be reprocessed once the cause is fixed.
/// No installation, timing point or transponder is resolved for it. One row per (AgentId, SourcePassingId).
/// </summary>
public class RejectedPassing
{
    public Guid Id { get; init; }
    public required string AgentId { get; init; }
    public required Guid SourcePassingId { get; init; }
    public DateTime Time { get; set; }
    public required string TransponderType { get; set; }
    public required string TransponderId { get; set; }
    public required string LoopId { get; set; }
    public bool LowBattery { get; set; }
    public bool LowStrength { get; set; }
    public bool LowHits { get; set; }
    /// <summary>Signal strength in the decoder's own units, as reported. Null when the agent does not send it.</summary>
    public double? Strength { get; set; }
    /// <summary>Hits the decoder counted for the passing. Null when the agent does not send it.</summary>
    public int? Hits { get; set; }
    public PassingRejectionReason Reason { get; set; }
    public DateTime FirstSeenAt { get; init; }
    public DateTime LastSeenAt { get; set; }
    public int Occurrences { get; set; } = 1;
}
