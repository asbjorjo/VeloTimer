namespace VeloTime.Module.Facilities.Interface.Data;

public record CoursePointDistance(
    Guid CoursePointStartId,
    Guid CoursePointEndId,
    double Distance,
    Guid FacilityId
);
