namespace VeloTime.Module.Timing.Model;

public class Installation {
    public Guid Id { get; init; }
    /// <summary>
    /// The facility of the open-ended <see cref="FacilityAssignments"/> entry (Guid.Empty when none), kept in
    /// step by <see cref="Service.InstallationFacilityService"/> for listings and filters. Anything that depends on
    /// when a passing happened must resolve the facility from <see cref="FacilityAssignments"/> instead.
    /// </summary>
    public Guid Facility { get; set; } = Guid.Empty;
    public required string AgentId { get; set; }
    public TimingSystem TimingSystem { get; set; } = TimingSystem.Unknown;
    public string Description { get; set; } = string.Empty;
    public DateTime? LastSeen { get; set; }

    /// <summary>When the agent's latest capability report was made (agent clock, UTC); null when it never reported.</summary>
    public DateTime? CapabilitiesReportedAt { get; set; }
    /// <summary>The agent type from the latest capability report, e.g. <c>P3</c>; descriptive, unlike <see cref="TimingSystem"/>.</summary>
    public string? AgentType { get; set; }
    public string? AgentVersion { get; set; }
    public int? ControlProtocolVersion { get; set; }
    /// <summary>The capabilities from the latest report. Only the latest report is kept.</summary>
    public List<AgentCapabilityEntry> AgentCapabilities { get; set; } = [];
    public List<TimingPoint> TimingPoints { get; init; } = new List<TimingPoint>();
    public List<InstallationFacilityAssignment> FacilityAssignments { get; init; } = [];
}
