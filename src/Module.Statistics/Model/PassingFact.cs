namespace VeloTime.Module.Statistics.Model;

/// <summary>
/// Statistics' copy of a passing fact Timing has recorded (<c>PassingRecordedV1</c>). Immutable and
/// idempotent on <see cref="PassingId"/>. Every sample and entry is derived from these rows plus the
/// configuration history, so they are never deleted by a recompute.
/// </summary>
public class PassingFact
{
    public Guid PassingId { get; init; }
    public Guid InstallationId { get; init; }
    public Guid TimingPointId { get; init; }
    public Guid TransponderId { get; init; }
    public DateTime Time { get; init; }
    public bool LowBattery { get; init; }
    public bool LowStrength { get; init; }
    public bool LowHits { get; init; }
    public DateTime RecordedAtUtc { get; init; }
}
