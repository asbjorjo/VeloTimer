namespace VeloTime.Agent.Interface;

/// <summary>
/// Capability names an agent can list in <see cref="Messages.Events.AgentCapabilitiesV1"/>. Timing keeps and shows
/// names it does not know but never acts on them, so a new capability needs no Timing release to become visible.
/// </summary>
public static class AgentCapabilities
{
    /// <summary>
    /// The agent can send passings for a time range again. One entry per supported mode, each carrying its limits in
    /// <see cref="Messages.Events.AgentCapability.PassingReplay"/>.
    /// </summary>
    public const string PassingReplay = "passing-replay";

    /// <summary>The agent handles <see cref="Messages.Control.PauseAgentCommand"/> and <see cref="Messages.Control.ResumeAgentCommand"/>.</summary>
    public const string PauseResume = "control.pause-resume";
}

/// <summary>Values of <see cref="Messages.Events.PassingReplayCapability.Mode"/> and <see cref="Messages.Control.ReplayPassingsCommandV1.Mode"/>.</summary>
public static class PassingReplayModes
{
    /// <summary>Re-publish passings from the agent's own store (<c>agent.passing</c>).</summary>
    public const string AgentStore = "AgentStore";

    /// <summary>Read passings again from the decoder or timing server behind the agent.</summary>
    public const string Source = "Source";
}

/// <summary>Values of <see cref="Messages.Events.PassingReplayStatusV1.State"/>.</summary>
public static class PassingReplayStates
{
    public const string Accepted = "Accepted";
    public const string Rejected = "Rejected";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

/// <summary>Values of <see cref="Messages.Events.PassingReplayStatusV1.Reason"/> when the agent rejects a request.</summary>
public static class PassingReplayRejectionReasons
{
    public const string UnsupportedMode = "UnsupportedMode";
    public const string RangeTooLong = "RangeTooLong";
    public const string OutOfRetention = "OutOfRetention";
    public const string SourceUnavailable = "SourceUnavailable";
    public const string Expired = "Expired";
    public const string Busy = "Busy";
    /// <summary><c>FromUtc</c> is not before <c>ToUtc</c>.</summary>
    public const string InvalidRange = "InvalidRange";
    /// <summary>Loop IDs were given but the mode does not support a loop filter.</summary>
    public const string LoopFilterUnsupported = "LoopFilterUnsupported";
}

/// <summary>Topic, header and protocol constants shared by agents and the platform for the agent control plane.</summary>
public static class AgentControl
{
    /// <summary>The control protocol version described by this contract set.</summary>
    public const int ProtocolVersion = 1;

    /// <summary>
    /// Topic for commands sent to agents. Each agent provisions its own subscription, named after its agent ID, with a
    /// correlation filter on the <see cref="AgentIdHeader"/> application property.
    /// </summary>
    public const string Topic = "velotime-agent-control";

    /// <summary>Application property naming the agent: set by agents on everything they send, and by the platform on every command.</summary>
    public const string AgentIdHeader = "AgentId";

    /// <summary>
    /// Application property on a replayed <see cref="Messages.Events.PassingObservedV1"/> naming the
    /// <see cref="Messages.Control.ReplayPassingsCommandV1.RequestId"/>. A header, not payload, so payload equality and
    /// Timing's deduplication are unaffected.
    /// </summary>
    public const string ReplayRequestIdHeader = "ReplayRequestId";
}
