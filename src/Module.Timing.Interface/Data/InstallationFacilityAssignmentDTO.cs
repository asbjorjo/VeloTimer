namespace VeloTime.Module.Timing.Interface.Data;

/// <summary>An installation's facility over [ValidFromUtc, ValidToUtc); null bounds are unbounded.</summary>
public sealed record InstallationFacilityAssignmentDTO(Guid Id, Guid InstallationId, Guid FacilityId,
    DateTime? ValidFromUtc, DateTime? ValidToUtc);
