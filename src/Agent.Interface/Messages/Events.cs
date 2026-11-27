namespace VeloTime.Agent.Interface.Messages.Events;

public record AgentStartedEvent(DateTime Time, string AgentId);

/// <summary>
/// A passing observed by an agent. <paramref name="SourcePassingId"/> is the agent's stable local passing ID and must
/// be a UUID; together with the AgentId message header it identifies the observation, so redelivery and replay are idempotent.
/// <paramref name="TransponderType"/> is one of <see cref="TransponderTypes"/>: the chip type, not the decoder.
/// </summary>
public record PassingObservedV1(Guid SourcePassingId, DateTime Time, string TransponderType, string TransponderId, string LoopId)
{
    public bool LowBattery { get; init; }
    public bool LowStrength { get; init; }
    public bool LowHits { get; init; }
}
/// <summary><paramref name="TransponderType"/> is one of <see cref="TransponderTypes"/>.</summary>
public record TransponderStatusEvent(DateTime Time, string TransponderType, string TransponderId, bool LowBattery, bool LowStrength);
public record LoopStatusEvent(DateTime Time, string LoopId, double Noise);

public record SystemConfigEvent(string SystemId, string Sport, string Description, bool IsActive);
public record LoopConfigEvent(string LoopId, string Name, int Order, string SystemId);
public record SegmentConfigEvent(string SegmentId, string Name, string StartLoopId, string EndLoopId, double Length, string SystemId);

public record InstallationLayoutEvent(List<SystemConfigEvent> Systems, List<LoopConfigEvent> TimingLoops, List<SegmentConfigEvent> Segments);