namespace VeloTime.WebUI.Mud.Client.Services;

public interface IFacilityService
{
    Task<IEnumerable<FacilityView>> GetFacilitiesAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<CourseLayoutView>> GetFacilityLayoutsAsync(Guid facilityId, CancellationToken cancellationToken = default);
    Task<IEnumerable<InstallationView>> GetInstallationsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CourseLayoutDetailView> GetCourseLayoutDetailAsync(Guid layoutId, CancellationToken cancellationToken = default);
}
