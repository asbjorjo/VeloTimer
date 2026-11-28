namespace VeloTime.Module.Timing.Model;

/// <summary>
/// One capability from the agent's latest <c>AgentCapabilitiesV1</c> report, stored as JSON on the installation.
/// Unknown names are kept so they can be shown, but nothing acts on them.
/// </summary>
public sealed class AgentCapabilityEntry
{
    public string Name { get; set; } = string.Empty;
    public PassingReplayCapabilityEntry? PassingReplay { get; set; }
}

/// <summary>The limits of one passing replay mode, as last reported by the agent.</summary>
public sealed class PassingReplayCapabilityEntry
{
    public string Mode { get; set; } = string.Empty;
    public DateTime? OldestAvailableUtc { get; set; }
    /// <summary>Longest range one replay request may cover, in seconds; null when unlimited.</summary>
    public double? MaxRangeSeconds { get; set; }
    public bool SupportsLoopFilter { get; set; }
}
