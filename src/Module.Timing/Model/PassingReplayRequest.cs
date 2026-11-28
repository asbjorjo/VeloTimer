namespace VeloTime.Module.Timing.Model;

/// <summary>
/// An admin's request for an agent to send passings again (docs/passing-replay-plan.md). The agent reports progress
/// with <c>PassingReplayStatusV1</c>; Timing counts what happened to each replayed passing from the
/// <c>ReplayRequestId</c> header.
/// </summary>
public class PassingReplayRequest
{
    /// <summary>The command's <c>RequestId</c>.</summary>
    public Guid Id { get; init; }
    public Guid InstallationId { get; init; }
    public required string AgentId { get; init; }
    /// <summary><c>AgentStore</c> or <c>Source</c>.</summary>
    public required string Mode { get; init; }
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    /// <summary>Loop IDs (timing point system IDs) to replay; empty for all loops.</summary>
    public List<string> LoopIds { get; init; } = [];
    public Guid? RequestedByUserId { get; init; }
    public string? Note { get; init; }
    public DateTime CreatedAt { get; init; }
    /// <summary>When the command stops being deliverable; a request still pending then is marked expired.</summary>
    public DateTime ExpiresAt { get; init; }

    public PassingReplayRequestState State { get; set; } = PassingReplayRequestState.Pending;
    /// <summary>Agent clock time of the status last applied; older statuses are ignored.</summary>
    public DateTime? StatusReportedAt { get; set; }
    public string? Reason { get; set; }

    // Counts reported by the agent.
    public int Read { get; set; }
    public int Published { get; set; }
    public int NewInAgentStore { get; set; }

    // What Timing made of the replayed passings.
    public int Recorded { get; set; }
    public int Duplicate { get; set; }
    public int Conflict { get; set; }
    public int Rejected { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public enum PassingReplayRequestState
{
    /// <summary>Stored and the command is in the outbox; the agent has not answered.</summary>
    Pending,
    Accepted,
    Running,
    Completed,
    Failed,
    Rejected,
    /// <summary>No answer from the agent before the command expired.</summary>
    Expired
}
