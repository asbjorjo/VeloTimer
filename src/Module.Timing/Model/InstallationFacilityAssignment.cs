namespace VeloTime.Module.Timing.Model;

/// <summary>
/// The facility an installation serves over [<see cref="ValidFromUtc"/>, <see cref="ValidToUtc"/>); null bounds
/// are unbounded and an installation's assignments never overlap (plan §4.1). Statistics resolves each passing's
/// facility from these at the passing's own time. Edits split, close or replace assignments from an effective
/// date and publish <c>InstallationFacilityChangedV1</c>; see <see cref="Service.InstallationFacilityService"/>.
/// </summary>
public class InstallationFacilityAssignment
{
    public Guid Id { get; init; }
    public Guid InstallationId { get; init; }
    public Installation Installation { get; init; } = null!;
    public Guid FacilityId { get; init; }
    public DateTime? ValidFromUtc { get; init; }
    public DateTime? ValidToUtc { get; set; }

    public bool Contains(DateTime instant) =>
        (ValidFromUtc is not { } from || from <= instant) && (ValidToUtc is not { } to || instant < to);
}
