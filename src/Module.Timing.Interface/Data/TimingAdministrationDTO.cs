namespace VeloTime.Module.Timing.Interface.Data;

/// <param name="FacilityValidFromUtc">When a facility change takes effect. Omitted: now, or the installation's
/// whole history when it has never been assigned. Backdating recomputes statistics from that instant.</param>
public sealed record SaveInstallationRequest(Guid? FacilityId, string AgentId, string TimingSystem, string Description,
    DateTime? FacilityValidFromUtc = null);
public sealed record SaveTimingPointRequest(string SystemId, string Description);
public sealed record SaveTransponderRequest(string Label);