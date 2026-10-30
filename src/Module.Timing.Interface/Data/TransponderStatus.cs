namespace VeloTime.Module.Timing.Interface.Data;

public sealed record TransponderSighting(
    Guid Id, DateTimeOffset Time, Guid FacilityId, Guid InstallationId, string Installation,
    Guid TimingPointId, string TimingPoint, bool LowBattery, bool LowStrength, bool LowHits);

public sealed record TransponderStatus(
    Guid Id, string Label, string Code, string SystemId, string System, bool Administration,
    TransponderSighting? LastSeen, DateTimeOffset WarningWindowStart, DateTimeOffset WarningWindowEnd,
    bool LowBattery, bool LowStrength, bool LowHits,
    IReadOnlyList<TransponderOwnershipDTO> Ownerships, int OwnershipCount,
    IReadOnlyList<TransponderSighting> Warnings, int WarningCount);
