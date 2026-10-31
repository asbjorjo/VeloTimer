namespace VeloTime.Module.Facilities.Interface.Data;

public sealed record SaveFacilityRequest(string Name);
public sealed record SaveCoursePointRequest(Guid Id, string Name);
public sealed record SaveCourseSegmentRequest(Guid Id, Guid StartId, Guid EndId, double Length);
public sealed record SaveCourseLayoutRequest(IReadOnlyList<SaveCoursePointRequest> Points, IReadOnlyList<SaveCourseSegmentRequest> Segments);
public sealed record SetCoursePointTimingLinkRequest(Guid? TimingPointId);