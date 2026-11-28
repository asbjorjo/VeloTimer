namespace VeloTime.Agent.Capabilities;

/// <summary>Agent identity and capability reporting settings, bound from the <c>Agent</c> configuration section.</summary>
public sealed class AgentOptions
{
    public const string Section = "Agent";

    /// <summary>The agent ID, from <c>VELOTIME_AGENT</c>. Set by <c>ConfigureAgent</c>.</summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// The constant the agent passes to <see cref="Identity.PassingId.Create(string, ReadOnlySpan{byte})"/>, e.g.
    /// <c>P3</c>, <c>X2</c> or <c>Dummy</c>. Pass it to <c>ConfigureAgent</c> or set <c>Agent:AgentType</c>.
    /// </summary>
    public string AgentType { get; set; } = "Unknown";

    /// <summary>Reported agent version; defaults to the entry assembly's informational version.</summary>
    public string? AgentVersion { get; set; }

    /// <summary>Send <c>AgentCapabilitiesV1</c> on startup, on change and on request. On by default.</summary>
    public bool ReportCapabilities { get; set; } = true;

    /// <summary>How long to wait before retrying a report that could not be enqueued.</summary>
    public TimeSpan ReportRetryDelay { get; set; } = TimeSpan.FromSeconds(30);
}
