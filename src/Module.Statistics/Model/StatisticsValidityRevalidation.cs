namespace VeloTime.Module.Statistics.Model;

/// <summary>
/// One coalescing checkpoint per item, including configuration-scoped edits.
/// Writers must lock this row in their transaction; Generation guards stale batches,
/// never public reads. See Storage/ValidityPersistence.md for the writer protocol.
/// </summary>
public class StatisticsValidityRevalidation
{
    public Guid StatisticsItemId { get; init; }
    public StatisticsItem StatisticsItem { get; init; } = default!;
    public long Generation { get; set; } = 1;
    public bool IsPending { get; set; } = true;
    public StatisticsValidityRevalidationPhase Phase { get; set; }
    public Guid? LastEntryId { get; set; }
}

public enum StatisticsValidityRevalidationPhase
{
    Simple,
    Multi
}
