using Facilities = VeloTime.Module.Facilities.Interface.Data;
using Timing = VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

public interface IInfrastructureAdministrationService
{
    Task<IReadOnlyList<Facilities.FacilityDTO>> GetFacilitiesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Facilities.CourseLayoutDTO>> GetLayoutsAsync(Guid facilityId, CancellationToken cancellationToken = default);
    Task<Facilities.FacilityDTO> SaveFacilityAsync(Guid? id, Facilities.SaveFacilityRequest request, CancellationToken cancellationToken = default);
    Task DeleteFacilityAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Facilities.CourseLayoutDTO> CreateLayoutAsync(Guid facilityId, Facilities.SaveCourseLayoutRequest request, CancellationToken cancellationToken = default);
    Task<Facilities.CourseLayoutHistoryDTO> GetLayoutHistoryAsync(Guid facilityId, Guid id, CancellationToken cancellationToken = default);
    /// <summary>Throws <see cref="CourseLayoutRevisionConflictException"/> when the interval overlaps revisions that need confirmation.</summary>
    Task<Facilities.SetCourseLayoutRevisionResponse> SetLayoutRevisionAsync(Guid facilityId, Guid id, Facilities.SetCourseLayoutRevisionRequest request, CancellationToken cancellationToken = default);
    Task<Facilities.SetFacilityLayoutAssignmentResponse> SetLayoutAssignmentAsync(Guid facilityId, Guid id, Facilities.SetFacilityLayoutAssignmentRequest request, CancellationToken cancellationToken = default);
    Task DeleteLayoutAsync(Guid facilityId, Guid id, CancellationToken cancellationToken = default);
    Task SetTimingLinkAsync(Guid pointId, Guid? timingPointId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Facilities.CoursePointTimingAssociationSnapshot>> GetCoursePointTimingAssociationsAsync(Guid id, CancellationToken cancellationToken = default);
    Task SetCoursePointTimingAssociationAsync(Guid id, Facilities.SetCoursePointTimingAssociationRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Timing.InstallationDTO>> GetInstallationsAsync(CancellationToken cancellationToken = default);
    Task<Timing.InstallationDTO> SaveInstallationAsync(Guid? id, Timing.SaveInstallationRequest request, CancellationToken cancellationToken = default);
    Task DeleteInstallationAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Timing.TimingPointDTO> SaveTimingPointAsync(Guid installationId, Guid? id, Timing.SaveTimingPointRequest request, CancellationToken cancellationToken = default);
    Task DeleteTimingPointAsync(Guid installationId, Guid id, CancellationToken cancellationToken = default);
}

public sealed class TimingAssociationConflictException(
    Facilities.TimingAssociationConflictPreview preview,
    Exception? innerException = null)
    : HttpRequestException("The timing-point association conflicts with an existing schedule.", innerException,
        System.Net.HttpStatusCode.Conflict)
{
    public Facilities.TimingAssociationConflictPreview Preview { get; } = preview;
}

public sealed class CourseLayoutRevisionConflictException(
    Facilities.CourseLayoutRevisionConflictPreview preview,
    Exception? innerException = null)
    : HttpRequestException("The layout revision replaces existing revisions and needs confirmation.", innerException,
        System.Net.HttpStatusCode.Conflict)
{
    public Facilities.CourseLayoutRevisionConflictPreview Preview { get; } = preview;
}
