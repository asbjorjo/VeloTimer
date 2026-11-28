namespace VeloTime.Module.Timing.Interface.Data;

/// <summary>Asks an installation's agent to send passings in [<see cref="FromUtc"/>, <see cref="ToUtc"/>) again.</summary>
public class CreatePassingReplayRequest
{
    /// <summary><c>AgentStore</c> or <c>Source</c>; must be advertised by the agent.</summary>
    public string Mode { get; set; } = string.Empty;
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    /// <summary>Loop IDs (timing point system IDs) to limit the replay to; only when the mode supports a loop filter.</summary>
    public List<string>? LoopIds { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// A passing replay and its progress. <see cref="State"/> is <c>Pending</c>, <c>Accepted</c>, <c>Running</c>,
/// <c>Completed</c>, <c>Failed</c>, <c>Rejected</c> or <c>Expired</c>. <see cref="Read"/> and <see cref="Published"/> are
/// the agent's counts; <see cref="Recorded"/>, <see cref="Duplicate"/>, <see cref="Conflict"/> and
/// <see cref="Rejected"/> are what Timing made of the replayed passings.
/// </summary>
public class PassingReplayRequestDTO
{
    public Guid RequestId { get; set; }
    public Guid InstallationId { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public List<string> LoopIds { get; set; } = [];
    public Guid? RequestedByUserId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string State { get; set; } = string.Empty;
    /// <summary>Set for <c>Rejected</c> (an agent or platform reason code) and <c>Failed</c>.</summary>
    public string? Reason { get; set; }
    public DateTime? StatusReportedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int Read { get; set; }
    public int Published { get; set; }
    public int NewInAgentStore { get; set; }
    public int Recorded { get; set; }
    public int Duplicate { get; set; }
    public int Conflict { get; set; }
    public int Rejected { get; set; }
}
