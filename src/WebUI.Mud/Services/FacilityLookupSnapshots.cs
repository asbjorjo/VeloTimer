using FacilitiesContract = VeloTime.Module.Facilities.Interface.Data;
using FacilitiesClient = VeloTime.Module.Facilities.Client;

namespace VeloTime.WebUI.Mud.Services;

// Private cache payloads only: generated client DTOs and returned contract DTOs
// contain mutable collection properties and must never be shared with callers.
// Preserve reference metadata requires objects to exist before their nested
// properties are read; do not bind these payloads through positional constructors.
internal sealed record FacilityLookupSnapshot
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public Guid[] LayoutIds { get; init; } = [];
    public FacilityLookupSnapshot() { }
    public FacilityLookupSnapshot(Guid id, string name, Guid[] layoutIds) =>
        (Id, Name, LayoutIds) = (id, name, layoutIds);
}

internal sealed record CoursePointLookupSnapshot
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public Guid? TimingPointId { get; init; }
    public CoursePointLookupSnapshot() { }
    public CoursePointLookupSnapshot(Guid id, string name, Guid? timingPointId) =>
        (Id, Name, TimingPointId) = (id, name, timingPointId);
}

internal sealed record SegmentLookupSnapshot
{
    public Guid Id { get; init; }
    public int Order { get; init; }
    public double Length { get; init; }
    public CoursePointLookupSnapshot Start { get; init; } = new();
    public CoursePointLookupSnapshot End { get; init; } = new();
    public SegmentLookupSnapshot() { }
    public SegmentLookupSnapshot(Guid id, int order, double length,
        CoursePointLookupSnapshot start, CoursePointLookupSnapshot end) =>
        (Id, Order, Length, Start, End) = (id, order, length, start, end);
}

internal sealed record LayoutLookupSnapshot
{
    public Guid Id { get; init; }
    public Guid FacilityId { get; init; }
    public SegmentLookupSnapshot[] Segments { get; init; } = [];
    public LayoutLookupSnapshot() { }
    public LayoutLookupSnapshot(Guid id, Guid facilityId, SegmentLookupSnapshot[] segments) =>
        (Id, FacilityId, Segments) = (id, facilityId, segments);
}

internal static class FacilityLookupSnapshots
{
    public static FacilityLookupSnapshot[] Facilities(IEnumerable<FacilitiesClient.FacilityDTO>? response) =>
        (response ?? throw new InvalidDataException("The facility list is missing.")).Select(From).ToArray();

    public static LayoutLookupSnapshot[] Layouts(IEnumerable<FacilitiesClient.CourseLayoutDTO>? response, Guid facilityId) =>
        (response ?? throw new InvalidDataException("The course-layout list is missing."))
            .Select(layout => From(layout, null, facilityId)).ToArray();

    public static FacilityLookupSnapshot From(FacilitiesClient.FacilityDTO facility)
    {
        if (facility is null || facility.Id == Guid.Empty || string.IsNullOrWhiteSpace(facility.Name) ||
            facility.Layouts is null || facility.Layouts.Any(id => id == Guid.Empty))
            throw new InvalidDataException("The facility response is invalid.");
        return new(facility.Id, facility.Name, facility.Layouts.ToArray());
    }

    public static LayoutLookupSnapshot From(FacilitiesClient.CourseLayoutDTO layout) => From(layout, null, null);

    public static LayoutLookupSnapshot From(FacilitiesClient.CourseLayoutDTO layout,
        Guid? expectedId, Guid? expectedFacilityId = null)
    {
        if (layout is null || layout.Id == Guid.Empty || layout.FacilityId == Guid.Empty ||
            (expectedId.HasValue && layout.Id != expectedId.Value) ||
            (expectedFacilityId.HasValue && layout.FacilityId != expectedFacilityId.Value) ||
            layout.Segments is null)
            throw new InvalidDataException("The course-layout response is invalid.");
        foreach (var segment in layout.Segments)
        {
            if (segment is null || segment.Id == Guid.Empty ||
                !double.IsFinite(segment.Length) || segment.Length <= 0 ||
                segment.Start is null || segment.End is null ||
                segment.Start.Id == Guid.Empty || segment.End.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(segment.Start.Name) || string.IsNullOrWhiteSpace(segment.End.Name) ||
                segment.Start.TimingPointId == Guid.Empty || segment.End.TimingPointId == Guid.Empty)
                throw new InvalidDataException("The course-layout segment response is invalid.");
        }
        return new(layout.Id, layout.FacilityId, layout.Segments
            .OrderBy(segment => segment.Order)
            .Select(segment => new SegmentLookupSnapshot(
                segment.Id,
                segment.Order,
                segment.Length,
                new CoursePointLookupSnapshot(segment.Start.Id, segment.Start.Name, segment.Start.TimingPointId),
                new CoursePointLookupSnapshot(segment.End.Id, segment.End.Name, segment.End.TimingPointId)))
            .ToArray());
    }

    public static FacilitiesContract.FacilityDTO ToContract(FacilityLookupSnapshot facility) =>
        new() { Id = facility.Id, Name = facility.Name, Layouts = facility.LayoutIds.ToList() };

    public static FacilitiesContract.CourseLayoutDTO ToContract(LayoutLookupSnapshot layout) =>
        new()
        {
            Id = layout.Id,
            FacilityId = layout.FacilityId,
            Segments = layout.Segments.Select(segment => new FacilitiesContract.SegmentDTO
            {
                Id = segment.Id,
                Order = segment.Order,
                Length = segment.Length,
                Start = ToContract(segment.Start),
                End = ToContract(segment.End)
            }).ToList()
        };

    private static FacilitiesContract.CoursePointDTO ToContract(CoursePointLookupSnapshot point) =>
        new() { Id = point.Id, Name = point.Name, TimingPointId = point.TimingPointId };
}
