using VeloTime.Module.Facilities.Interface.Data;

namespace VeloTime.Module.Facilities.Interface.Client;

public interface IFacitiliesClient
{
    Task CreateCourseLayout(CourseLayoutDTO Layout);
    Task<double> DistanceBetweenCoursePoints(Guid Start, Guid End);
    Task<CoursePointDistance> DistanceBetweenTimingPoints(Guid Start, Guid End);
    Task<CoursePointDTO> GetCoursePointById(Guid coursePointId);
    Task<CoursePointDTO> GetCoursePointByTimingPointId(Guid timingPointId);
    Task<FacilityDTO> GetFacilityAsync(Guid facilityId);
    Task<ICollection<FacilityDTO>> GetAllFacilitiesAsync(CancellationToken token);
    Task<ICollection<CourseLayoutDTO>> GetFacilityCourseLayoutsAsync(Guid facilityId, CancellationToken token);
    Task<CourseLayoutDTO> GetCourseLayoutAsync(Guid layoutId, CancellationToken cancellationToken);
}
