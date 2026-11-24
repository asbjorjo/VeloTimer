namespace VeloTime.Module.Statistics.Model;

/// <summary>
/// Retained immutable outcome for one triggering parent and Multi configuration.
/// A null result is a completed no-result decision, not pending work.
/// IDs deliberately have no relationships to deletable entries/configurations.
/// </summary>
public class MultiCreationDecision
{
    public Guid ParentEntryId { get; init; }
    public Guid MultiConfigId { get; init; }
    public Guid? ResultEntryId { get; init; }
}
