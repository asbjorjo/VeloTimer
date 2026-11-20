namespace VeloTime.Module.Facilities.Model;

/// <summary>
/// A Facilities-owned mapping over [ValidFromUtc, ValidToUtc).
/// Null bounds are unbounded; two null bounds preserve an all-time legacy mapping.
/// TimingPointId is an external identity, not a cross-service foreign key.
/// </summary>
public class CoursePointTimingAssociation
{
    private DateTimeOffset? validFromUtc;
    private DateTimeOffset? validToUtc;

    public Guid Id { get; init; }
    public Guid CoursePointId { get; set; }
    public CoursePoint CoursePoint { get; set; } = default!;
    public Guid TimingPointId { get; set; }

    public DateTimeOffset? ValidFromUtc
    {
        get => validFromUtc;
        set => validFromUtc = value?.ToUniversalTime();
    }

    public DateTimeOffset? ValidToUtc
    {
        get => validToUtc;
        set => validToUtc = value?.ToUniversalTime();
    }
}
