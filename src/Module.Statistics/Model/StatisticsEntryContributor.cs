namespace VeloTime.Module.Statistics.Model;

/// <summary>Immutable selected Multi window, ordered newest contributor first.</summary>
public class StatisticsEntryContributor
{
    public Guid StatisticsEntryId { get; init; }
    public StatisticsEntry StatisticsEntry { get; init; } = default!;
    public int Position { get; init; }
    public Guid ContributorEntryId { get; init; }
    public StatisticsEntry ContributorEntry { get; init; } = default!;
}
