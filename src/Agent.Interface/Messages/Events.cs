namespace VeloTime.Agent.Interface.Messages.Events;

/// <summary>
/// What an agent can do, sent on the agent topic on startup, whenever a capability changes, and when asked with
/// <see cref="Control.ReportCapabilitiesCommandV1"/>. The platform keeps only the latest report per agent, ordered by
/// <paramref name="ReportedAtUtc"/>. <paramref name="AgentType"/> is the constant the agent passes to
/// <c>PassingId.Create</c>, e.g. <c>P3</c>, <c>X2</c> or <c>Dummy</c>. <paramref name="ControlProtocolVersion"/> is
/// <see cref="AgentControl.ProtocolVersion"/>.
/// </summary>
public record AgentCapabilitiesV1(
    DateTime ReportedAtUtc,
    string AgentType,
    string AgentVersion,
    int ControlProtocolVersion,
    IReadOnlyList<AgentCapability> Capabilities);

/// <summary>
/// One capability. <paramref name="Name"/> is one of <see cref="AgentCapabilities"/> (unknown names are allowed).
/// <paramref name="PassingReplay"/> is set for <see cref="AgentCapabilities.PassingReplay"/>.
/// </summary>
public record AgentCapability(string Name, PassingReplayCapability? PassingReplay = null);

/// <summary>
/// The limits of one passing replay mode. <paramref name="Mode"/> is one of <see cref="PassingReplayModes"/>.
/// <paramref name="OldestAvailableUtc"/> is null when unknown; <paramref name="MaxRange"/> is the longest range one
/// request may cover, null when unlimited.
/// </summary>
public record PassingReplayCapability(
    string Mode,
    DateTime? OldestAvailableUtc,
    TimeSpan? MaxRange,
    bool SupportsLoopFilter);

/// <summary>
/// Progress of a <see cref="Control.ReplayPassingsCommandV1"/>. <paramref name="State"/> is one of
/// <see cref="PassingReplayStates"/>; <paramref name="Reason"/> is set for <c>Rejected</c> (one of
/// <see cref="PassingReplayRejectionReasons"/>) and <c>Failed</c>. <paramref name="NewInAgentStore"/> counts passings a
/// <c>Source</c> replay added to the agent store and is 0 for <c>AgentStore</c> replays.
/// </summary>
public record PassingReplayStatusV1(
    Guid RequestId,
    DateTime ReportedAtUtc,
    string State,
    int Read,
    int Published,
    int NewInAgentStore,
    string? Reason);

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