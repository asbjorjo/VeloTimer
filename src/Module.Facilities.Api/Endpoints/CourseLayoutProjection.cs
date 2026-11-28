using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Service;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Api.Endpoints;

/// <summary>Projects layouts (loaded with their history) to how they are at one instant.</summary>
internal static class CourseLayoutProjection
{
    public static async Task<List<CourseLayoutDTO>> ToDtosAsync(FacilityDbContext storage, IReadOnlyList<CourseLayout> layouts,
        DateTimeOffset at, CancellationToken cancellationToken)
    {
        var revisions = layouts.ToDictionary(layout => layout.Id, layout => layout.RevisionAt(at));
        var mappings = await new TimingAssociationService(storage).ResolveManyAsync(
            revisions.Values.OfType<CourseLayoutRevision>().SelectMany(revision => revision.Segments)
                .SelectMany(segment => new[] { segment.Start.Id, segment.End.Id }),
            at, cancellationToken);
        return layouts.Select(layout =>
        {
            var revision = revisions[layout.Id];
            return new CourseLayoutDTO
            {
                Id = layout.Id,
                FacilityId = layout.FacilityId,
                RevisionId = revision?.Id,
                RevisionValidFromUtc = revision?.ValidFromUtc,
                RevisionValidToUtc = revision?.ValidToUtc,
                InUse = layout.InUseAt(at),
                Segments = revision?.Segments.OrderBy(segment => segment.Order).Select(segment => new SegmentDTO
                {
                    Id = segment.Id,
                    Order = segment.Order,
                    Length = segment.Length,
                    Start = Point(segment.Start, mappings),
                    End = Point(segment.End, mappings)
                }).ToList() ?? []
            };
        }).ToList();
    }

    private static CoursePointDTO Point(CoursePoint point, IReadOnlyDictionary<Guid, Guid?> mappings) => new()
    {
        Id = point.Id,
        Name = point.Name ?? string.Empty,
        TimingPointId = mappings.GetValueOrDefault(point.Id)
    };
}
