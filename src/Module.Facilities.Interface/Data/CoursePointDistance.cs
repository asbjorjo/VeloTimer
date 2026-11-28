namespace VeloTime.Module.Facilities.Interface.Data;

public record CoursePointDistance(
    Guid CoursePointStartId,
    Guid CoursePointEndId,
    double Distance,
    Guid FacilityId
)
{
    /// <summary>
    /// The unique course layout used for the resolved segment and closed-loop evidence, when
    /// Facilities can prove both. Null means no authoritative closed-loop evidence is available.
    /// </summary>
    public Guid? CourseLayoutId { get; init; }

    /// <summary>
    /// Full closed-loop geometry length in metres, not the distance between the resolved endpoints.
    /// Present only with CourseLayoutId when that layout proves one finite, positive closed loop.
    /// </summary>
    public double? ClosedLoopDistanceMetres { get; init; }
}
