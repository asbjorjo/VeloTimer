namespace VeloTime.Agent.Interface.Messages.Control;

/// <summary>Sent on <see cref="AgentControl.Topic"/>. Handled only by agents that advertise <see cref="AgentCapabilities.PauseResume"/>.</summary>
public record PauseAgentCommand(string AgentId);

/// <summary>Sent on <see cref="AgentControl.Topic"/>. Handled only by agents that advertise <see cref="AgentCapabilities.PauseResume"/>.</summary>
public record ResumeAgentCommand(string AgentId);

/// <summary>
/// Asks an agent to send <see cref="Events.PassingObservedV1"/> again for every passing in the half-open range
/// [<paramref name="FromUtc"/>, <paramref name="ToUtc"/>) of passing time. The agent re-checks the request against its
/// own current capabilities and answers with <see cref="Events.PassingReplayStatusV1"/>; it rejects a command received
/// after <paramref name="ExpiresAtUtc"/>. <paramref name="Mode"/> is one of <see cref="PassingReplayModes"/>.
/// <paramref name="LoopIds"/> is set only when the agent advertised <c>SupportsLoopFilter</c>.
/// </summary>
public record ReplayPassingsCommandV1(
    Guid RequestId,
    string AgentId,
    string Mode,
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyList<string>? LoopIds,
    DateTime ExpiresAtUtc);

/// <summary>Asks an agent to send a fresh <see cref="Events.AgentCapabilitiesV1"/>.</summary>
public record ReportCapabilitiesCommandV1(Guid RequestId, string AgentId);
