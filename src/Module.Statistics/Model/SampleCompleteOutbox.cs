namespace VeloTime.Module.Statistics.Model;

/// <summary>Durable, immutable SampleComplete payload. Delivery is at least once.</summary>
public class SampleCompleteOutbox
{
    public long Id { get; set; }
    public Guid SampleId { get; init; }
    public Guid TransponderId { get; init; }
    public DateTime TimeStart { get; init; }
    public DateTime TimeEnd { get; init; }
    public Guid CoursePointStart { get; init; }
    public Guid CoursePointEnd { get; init; }
    public double Distance { get; init; }
    public DateTime? DeliveredAtUtc { get; set; }
}
