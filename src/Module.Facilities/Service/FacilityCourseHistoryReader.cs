using System.Data;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Service;

/// <summary>
/// Reads one facility's whole course configuration history in one snapshot: each layout's assignments and
/// revisions with their segments, and the association history of every course point they use.
/// </summary>
public sealed class FacilityCourseHistoryReader(FacilityDbContext storage)
{
    /// <summary>Null when the facility does not exist.</summary>
    public async Task<FacilityCourseHistoryDTO?> ReadAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        var isolation = storage.Database.IsNpgsql() ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable;
        var owns = storage.Database.CurrentTransaction is null;
        await using var transaction = owns ? await storage.Database.BeginTransactionAsync(isolation, cancellationToken) : null;
        if (!await storage.Set<Facility>().AnyAsync(facility => facility.Id == facilityId, cancellationToken)) return null;
        var layouts = await CourseConfigurationService.LayoutsWithHistory(storage).AsNoTracking()
            .Where(layout => layout.FacilityId == facilityId)
            .OrderBy(layout => layout.Id)
            .ToListAsync(cancellationToken);
        var points = CourseConfigurationService.Points(layouts).Select(point => point.Id).ToArray();
        var associations = (await storage.Set<CoursePointTimingAssociation>().AsNoTracking()
                .Where(association => points.Contains(association.CoursePointId))
                .ToListAsync(cancellationToken))
            .OrderBy(association => association.CoursePointId).ThenBy(association => association.ValidFromUtc.HasValue)
            .ThenBy(association => association.ValidFromUtc)
            .Select(association => new CoursePointTimingAssociationSnapshot(association.TimingPointId, association.CoursePointId,
                association.ValidFromUtc, association.ValidToUtc))
            .ToList();
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new FacilityCourseHistoryDTO
        {
            FacilityId = facilityId,
            Layouts = layouts.Select(ToHistory).ToList(),
            Associations = associations
        };
    }

    public static CourseLayoutHistoryDTO ToHistory(CourseLayout layout) => new()
    {
        LayoutId = layout.Id,
        Assignments = CourseConfigurationService.Intervals(layout.Assignments),
        Revisions = layout.Revisions.OrderBy(revision => revision.ValidFromUtc.HasValue).ThenBy(revision => revision.ValidFromUtc)
            .Select(revision => new CourseLayoutRevisionHistoryDTO
            {
                RevisionId = revision.Id,
                ValidFromUtc = revision.ValidFromUtc,
                ValidToUtc = revision.ValidToUtc,
                Segments = revision.Segments.OrderBy(segment => segment.Order).Select(segment => new CourseSegmentHistoryDTO
                {
                    Order = segment.Order, StartPointId = segment.Start.Id, EndPointId = segment.End.Id, Length = segment.Length
                }).ToList()
            }).ToList()
    };
}
