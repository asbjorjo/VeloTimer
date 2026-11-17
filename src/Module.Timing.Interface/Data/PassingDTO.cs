namespace VeloTime.Module.Timing.Interface.Data;

/// <summary>
/// Raw passings for one transponder within inclusive UTC From/To bounds (at most seven days).
/// Ordered by Time, then Id. At most 500 passings are returned; Truncated means more matched.
/// An unknown transponder or a window without matches returns an empty list, not 404.
/// </summary>
public sealed record TransponderPassingsDTO(
    Guid TransponderId,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<PassingDTO> Passings,
    bool Truncated);

/// <summary>
/// One raw Timing passing with a UTC timestamp, timing point and installation identities
/// and current descriptions, facility identity, and recorded signal warning flags.
/// </summary>
public sealed record PassingDTO(
    Guid Id,
    DateTimeOffset Time,
    Guid TimingPointId,
    string TimingPointDescription,
    Guid InstallationId,
    string InstallationDescription,
    Guid FacilityId,
    bool LowBattery,
    bool LowStrength,
    bool LowHits);
