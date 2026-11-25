namespace VeloTime.Module.Statistics.Model;

/// <summary>Retained publication receipt and immutable payload, independent of entry/config lifetime.</summary>
public class EntryCreatedOutbox
{
    public long Id { get; set; }
    public Guid EntryId { get; init; }
    public Guid TransponderId { get; init; }
    public DateTime TimeStart { get; init; }
    public DateTime TimeEnd { get; init; }
    public Guid StatisticsItemId { get; init; }
    public Guid ConfigItemId { get; init; }
    public bool Single { get; init; }
    public DateTime? DeliveredAtUtc { get; set; }
    /// <summary>0: legacy/unknown decisions; 1: writer participates in the durable Multi protocol.
    /// Must be explicitly set to 1 by the upgraded creator, never inferred on replay.</summary>
    public int MultiDecisionVersion { get; init; }
    /// <summary>All eligible Multi configs decided atomically, including an empty config set.
    /// Independent of broker delivery; once set, later configs cannot add outputs on replay.</summary>
    public DateTime? MultiDecisionsCreatedAtUtc { get; set; }
    /// <summary>UTC instant this receipt was atomically published to the processor SMB outbox;
    /// independent of transport delivery and retained after SMB cleanup.</summary>
    public DateTime? SmbEnqueuedAtUtc { get; set; }
}
