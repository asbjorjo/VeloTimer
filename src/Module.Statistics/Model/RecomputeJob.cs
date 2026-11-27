namespace VeloTime.Module.Statistics.Model;

/// <summary>
/// An administrator's "recompute range" request (plan §7, Phase 5). The job inserts one
/// <see cref="RecomputeRequest"/> with <see cref="RecomputeReason.Manual"/> whose <c>CausationId</c> is the job ID;
/// the worker's per-transponder requests keep that causation, so the job's progress is the number of its
/// requests still pending. The job row itself is never read by the recompute.
/// </summary>
public class RecomputeJob
{
    public Guid Id { get; init; }
    public Guid? TransponderId { get; init; }
    public Guid? InstallationId { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public string? Note { get; init; }
    public Guid? RequestedByUserId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    /// <summary>Transponders the job covers; known once its range request is split (immediately for one transponder).</summary>
    public int? TotalTransponders { get; set; }
    /// <summary>Set by the worker when the last of the job's requests commits. Best effort: progress is read from the requests.</summary>
    public DateTime? CompletedAtUtc { get; set; }
}
