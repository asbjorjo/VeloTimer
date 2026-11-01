using Facilities = VeloTime.Module.Facilities.Interface.Data;
using Timing = VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

public interface IInfrastructureAdministrationService
{
    Task<IReadOnlyList<Facilities.FacilityDTO>> GetFacilitiesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Facilities.CourseLayoutDTO>> GetLayoutsAsync(Guid facilityId, CancellationToken cancellationToken = default);
    Task<Facilities.FacilityDTO> SaveFacilityAsync(Guid? id, Facilities.SaveFacilityRequest request, CancellationToken cancellationToken = default);
    Task DeleteFacilityAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Facilities.CourseLayoutDTO> SaveLayoutAsync(Guid facilityId, Guid? id, Facilities.SaveCourseLayoutRequest request, CancellationToken cancellationToken = default);
    Task DeleteLayoutAsync(Guid facilityId, Guid id, CancellationToken cancellationToken = default);
    Task SetTimingLinkAsync(Guid pointId, Guid? timingPointId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Timing.InstallationDTO>> GetInstallationsAsync(CancellationToken cancellationToken = default);
    Task<Timing.InstallationDTO> SaveInstallationAsync(Guid? id, Timing.SaveInstallationRequest request, CancellationToken cancellationToken = default);
    Task DeleteInstallationAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Timing.TimingPointDTO> SaveTimingPointAsync(Guid installationId, Guid? id, Timing.SaveTimingPointRequest request, CancellationToken cancellationToken = default);
    Task DeleteTimingPointAsync(Guid installationId, Guid id, CancellationToken cancellationToken = default);
}