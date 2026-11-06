namespace VeloTime.Module.Timing.Interface.Data;

public sealed record SaveInstallationRequest(Guid? FacilityId, string AgentId, string TimingSystem, string Description);
public sealed record SaveTimingPointRequest(string SystemId, string Description);
public sealed record SaveTransponderRequest(string Label);