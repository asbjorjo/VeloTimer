namespace VeloTime.Module.Timing.Interface.Data;

/// <summary>The latest capability report an installation's agent sent (see <c>docs/passing-replay-plan.md</c>).</summary>
public class AgentCapabilitiesDTO
{
    /// <summary>When the agent made the report (agent clock, UTC).</summary>
    public DateTime ReportedAtUtc { get; set; }
    public string AgentType { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
    public int ControlProtocolVersion { get; set; }
    public List<AgentCapabilityDTO> Capabilities { get; set; } = [];
}

/// <summary>
/// One advertised capability. <see cref="Name"/> is an agent capability name such as <c>passing-replay</c> or
/// <c>control.pause-resume</c>; names the platform does not know are listed but not acted on.
/// </summary>
public class AgentCapabilityDTO
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Set for <c>passing-replay</c> entries.</summary>
    public PassingReplayCapabilityDTO? PassingReplay { get; set; }
}

/// <summary>The limits of one passing replay mode.</summary>
public class PassingReplayCapabilityDTO
{
    /// <summary><c>AgentStore</c> or <c>Source</c>.</summary>
    public string Mode { get; set; } = string.Empty;
    /// <summary>Oldest passing time the mode can replay (UTC); null when unknown.</summary>
    public DateTime? OldestAvailableUtc { get; set; }
    /// <summary>Longest range one request may cover, in seconds; null when unlimited.</summary>
    public double? MaxRangeSeconds { get; set; }
    public bool SupportsLoopFilter { get; set; }
}

/// <summary>Returned when a capability report was requested from the agent.</summary>
public class CapabilityReportRequestedDTO
{
    /// <summary>The <c>ReportCapabilitiesCommandV1.RequestId</c> sent to the agent.</summary>
    public Guid RequestId { get; set; }
}
