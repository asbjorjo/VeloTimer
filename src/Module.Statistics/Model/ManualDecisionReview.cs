namespace VeloTime.Module.Statistics.Model;

/// <summary>
/// A manual invalidation a recompute could not keep (plan §5.4): its entry disappeared or its measured
/// values changed. The decision is recorded here for an administrator instead of being dropped or moved
/// to another entry. Entry fields are a snapshot of the entry as it was invalidated.
/// </summary>
public class ManualDecisionReview
{
    public Guid Id { get; init; }
    public Guid EntryId { get; init; }
    public ManualDecisionReviewOutcome Outcome { get; init; }
    public Guid TransponderId { get; init; }
    public Guid StatisticsItemId { get; init; }
    public Guid StatisticsItemConfigId { get; init; }
    public Guid FacilityId { get; init; }
    public DateTime TimeStart { get; init; }
    public DateTime TimeEnd { get; init; }
    public TimeSpan Duration { get; init; }
    public double? DistanceMetres { get; init; }
    public Guid? ChangedByUserId { get; init; }
    public DateTimeOffset? ChangedAtUtc { get; init; }
    public string? Note { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public enum ManualDecisionReviewOutcome
{
    /// <summary>The recompute no longer derives the entry.</summary>
    Removed = 0,
    /// <summary>The entry still exists, but its measured values changed, so the decision was lifted.</summary>
    Changed = 1
}
