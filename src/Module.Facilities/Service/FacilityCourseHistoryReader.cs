using System.Data;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Service;

/// <summary>Reads one facility's layouts and the association history of their course points in one snapshot.</summary>
public sealed class FacilityCourseHistoryReader(FacilityDbContext storage)
{
    /// <summary>Null when the facility does not exist.</summary>
    public async Task<FacilityCourseHistoryDTO?> ReadAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        var isolation = storage.Database.IsNpgsql() ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable;
        var owns = storage.Database.CurrentTransaction is null;
        await using var transaction = owns ? await storage.Database.BeginTransactionAsync(isolation, cancellationToken) : null;
        if (!await storage.Set<Facility>().AnyAsync(facility => facility.Id == facilityId, cancellationToken)) return null;
        var layouts = await storage.Set<CourseLayout>().AsNoTracking()
            .Where(layout => layout.FacilityId == facilityId)
            .OrderBy(layout => layout.Id)
            .Select(layout => new CourseLayoutHistoryDTO
            {
                LayoutId = layout.Id,
                Segments = layout.Segments.OrderBy(segment => segment.Order).Select(segment => new CourseSegmentHistoryDTO
                {
                    Order = segment.Order, StartPointId = segment.Start.Id, EndPointId = segment.End.Id, Length = segment.Length
                }).ToList()
            }).ToListAsync(cancellationToken);
        var points = layouts.SelectMany(layout => layout.Segments).SelectMany(segment => new[] { segment.StartPointId, segment.EndPointId })
            .Distinct().ToArray();
        var associations = (await storage.Set<CoursePointTimingAssociation>().AsNoTracking()
                .Where(association => points.Contains(association.CoursePointId))
                .ToListAsync(cancellationToken))
            .OrderBy(association => association.CoursePointId).ThenBy(association => association.ValidFromUtc)
            .Select(association => new CoursePointTimingAssociationSnapshot(association.TimingPointId, association.CoursePointId,
                association.ValidFromUtc, association.ValidToUtc))
            .ToList();
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new FacilityCourseHistoryDTO { FacilityId = facilityId, Layouts = layouts, Associations = associations };
    }
}
