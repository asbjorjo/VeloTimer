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
}
