namespace VeloTime.Module.Statistics.Model;

/// <summary>
/// A dirty range to recompute (plan §3.3). A null <see cref="TransponderId"/> means every transponder
/// with facts in the range; the worker splits it into one request per transponder. Null bounds are
/// unbounded. The worker coalesces requests per transponder, expands them to whole sessions and deletes
/// them in the same transaction as the recomputed rows.
/// </summary>
public class RecomputeRequest
{
    public long Id { get; init; }
    public Guid? TransponderId { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public RecomputeReason Reason { get; init; }
    public Guid? CausationId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    /// <summary>Failed attempts so far; the worker backs off until <see cref="NotBeforeUtc"/>.</summary>
    public int Attempts { get; set; }
    public DateTime? NotBeforeUtc { get; set; }
    public string? LastError { get; set; }
}

public enum RecomputeReason
{
    Passing = 0,
    Definition = 1,
    Configuration = 2,
    Manual = 3
}
