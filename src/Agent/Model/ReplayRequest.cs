namespace VeloTime.Agent.Model;

/// <summary>
/// A <c>ReplayPassingsCommandV1</c> this agent has handled, kept so a redelivered command reports its earlier result
/// instead of running again, and so a replay interrupted by a restart is resumed.
/// </summary>
public class ReplayRequest
{
    public required Guid RequestId { get; init; }
    public required string Mode { get; init; }
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    /// <summary>Loop filter; empty for all loops.</summary>
    public List<string> LoopIds { get; init; } = [];
    public DateTime ReceivedAtUtc { get; init; }
    /// <summary>One of <c>PassingReplayStates</c>.</summary>
    public required string State { get; set; }
    public int Read { get; set; }
    public int Published { get; set; }
    public string? Reason { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public bool IsFinished => State is Interface.PassingReplayStates.Completed or Interface.PassingReplayStates.Failed
        or Interface.PassingReplayStates.Rejected;
}
