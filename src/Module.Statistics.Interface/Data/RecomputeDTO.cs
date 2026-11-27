namespace VeloTime.Module.Statistics.Interface.Data;

/// <summary>
/// An administrator's request to recompute Statistics over a range (plan Phase 5). Null bounds are unbounded,
/// so leaving both out recomputes all history. <see cref="TransponderId"/> narrows the job to one transponder;
/// <see cref="InstallationId"/> narrows it to transponders with passings recorded at that installation. Times are UTC.
/// </summary>
public sealed record CreateRecomputeJobRequest(
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    Guid? TransponderId,
    Guid? InstallationId,
    string? Note);

public enum RecomputeJobStatus
{
    /// <summary>The job's range has not been split into transponder work yet.</summary>
    Queued = 0,
    /// <summary>Some of the job's transponders are still waiting or retrying.</summary>
    Running = 1,
    /// <summary>Every transponder in the job has been recomputed.</summary>
    Completed = 2
}

/// <summary>
/// A recompute job and its progress. Remaining counts transponders still waiting, including ones that are
/// retrying after a failure (Failing). Total is null until the job's range has been split. Times are UTC.
/// </summary>
public sealed record RecomputeJobDTO(
    Guid Id,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    Guid? TransponderId,
    Guid? InstallationId,
    string? Note,
    Guid? RequestedByUserId,
    DateTimeOffset CreatedAtUtc,
    RecomputeJobStatus Status,
    int? TotalTransponders,
    int RemainingTransponders,
    int FailingTransponders,
    string? LastError,
    DateTimeOffset? CompletedAtUtc);

/// <summary>Pending recompute requests for one reason (Passing, Definition, Configuration or Manual).</summary>
public sealed record RecomputeBacklogReasonDTO(string Reason, int Requests);

/// <summary>
/// Operational state of the Statistics projection. TransponderRequests and RangeRequests are pending recompute
/// requests (a range request is split into transponder requests before it runs). FailingRequests are retrying
/// after an error, with the most recent error in LastError. UnresolvedPassings are passing facts without a
/// facility or course point at their time; ManualDecisionReviews are manual invalidations a recompute could not keep.
/// </summary>
public sealed record RecomputeBacklogDTO(
    int TransponderRequests,
    int RangeRequests,
    int FailingRequests,
    DateTimeOffset? OldestRequestCreatedAtUtc,
    string? LastError,
    IReadOnlyList<RecomputeBacklogReasonDTO> ByReason,
    int UnresolvedPassings,
    int ManualDecisionReviews,
    DateTimeOffset? OldestManualDecisionReviewAtUtc);

/// <summary>The backlog plus the most recent recompute jobs, newest first.</summary>
public sealed record RecomputeOverviewDTO(RecomputeBacklogDTO Backlog, IReadOnlyList<RecomputeJobDTO> Jobs);
