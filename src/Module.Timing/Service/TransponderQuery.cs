using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Service;

public static class TransponderQuery
{
    public static void Validate(int page, int pageSize)
    {
        if (page < 0 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            throw new ArgumentException("Page must be nonnegative and page size must be between 1 and 100, without offset overflow.");
    }

    public static async Task<TransponderPage> ListAsync(TimingDbContext storage, TransponderPageRequest request,
        Guid? ownerId, CancellationToken cancellationToken, bool statusAdministration = false)
    {
        Validate(request.Page, request.PageSize);
        ValidateSort(request.SortBy);
        var filters = request.Filters ?? new TransponderColumnFilters();
        if (filters.Status.HasValue && !Enum.IsDefined(filters.Status.Value))
            throw new ArgumentException("The transponder status filter is invalid.");

        var transponders = Visible(storage, ownerId);
        if (request.MatchingTransponderIds is { } ids)
            transponders = transponders.Where(transponder => ids.Contains(transponder.Id));
        else if (!string.IsNullOrEmpty(request.Search))
        {
            var search = request.Search.ToUpperInvariant();
            transponders = transponders.Where(transponder =>
                transponder.Label.ToUpper().Contains(search) || transponder.SystemId.ToUpper().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(filters.Label))
        {
            var label = filters.Label.Trim().ToUpperInvariant();
            transponders = transponders.Where(transponder => transponder.Label.ToUpper().Contains(label));
        }
        if (!string.IsNullOrWhiteSpace(filters.System))
        {
            var system = filters.System.Trim();
            var matchingSystems = Enum.GetNames<TimingSystem>()
                .Where(name => name.Contains(system, StringComparison.OrdinalIgnoreCase))
                .Select(name => Enum.Parse<TimingSystem>(name))
                .ToArray();
            transponders = transponders.Where(transponder => matchingSystems.Contains(transponder.System));
        }

        var now = DateTime.UtcNow;
        var owners = storage.Set<TransponderOwner>().AsNoTracking()
            .Where(owner => !ownerId.HasValue || owner.OwnerId == ownerId);
        // Keep unassigned rows, but do not turn filtered historical ownerships into unassigned rows.
        var rows = from transponder in transponders
                   join owner in owners on transponder.Id equals owner.TransponderId into ownerships
                   from owner in ownerships.DefaultIfEmpty()
                   where request.IncludeHistory || owner == null ||
                       owner.OwnedFrom <= now && (!owner.OwnedTo.HasValue || owner.OwnedTo > now)
                   select new { Transponder = transponder, Owner = owner,
                       CanViewStatus = statusAdministration || !ownerId.HasValue || transponder.Owners.Any(period => period.OwnerId == ownerId &&
                           period.OwnedFrom <= now && (!period.OwnedTo.HasValue || period.OwnedTo > now)) };
        if (request.MatchingOwnerIds is { } matchingOwnerIds)
            rows = rows.Where(row => row.Owner != null && matchingOwnerIds.Contains(row.Owner.OwnerId));

        var ownedFromStart = filters.OwnedFrom?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var ownedToStart = filters.OwnedTo?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        if (ownedFromStart.HasValue)
            rows = rows.Where(row => row.Owner != null && row.Owner.OwnedFrom >= ownedFromStart &&
                row.Owner.OwnedFrom < ownedFromStart.Value.AddDays(1));
        if (ownedToStart.HasValue)
            rows = rows.Where(row => row.Owner != null && row.Owner.OwnedTo >= ownedToStart &&
                row.Owner.OwnedTo < ownedToStart.Value.AddDays(1));
        if (filters.Status is { } status)
            rows = status switch
            {
                TransponderOwnershipStatus.Unassigned => rows.Where(row => row.Owner == null),
                TransponderOwnershipStatus.Scheduled => rows.Where(row => row.Owner != null && row.Owner.OwnedFrom > now),
                TransponderOwnershipStatus.Ended => rows.Where(row => row.Owner != null && row.Owner.OwnedFrom <= now &&
                    row.Owner.OwnedTo.HasValue && row.Owner.OwnedTo <= now),
                TransponderOwnershipStatus.Active => rows.Where(row => row.Owner != null && row.Owner.OwnedFrom <= now &&
                    (!row.Owner.OwnedTo.HasValue || row.Owner.OwnedTo > now)),
                _ => rows
            };
        if (filters.LastSeen.HasValue)
        {
            var start = filters.LastSeen.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var end = start.AddDays(1);
            rows = rows.Where(row => storage.Set<Passing>().Where(passing =>
                    passing.TransponderId == row.Transponder.Id)
                .Select(passing => (DateTime?)passing.Time).Max() >= start &&
                storage.Set<Passing>().Where(passing => passing.TransponderId == row.Transponder.Id)
                    .Select(passing => (DateTime?)passing.Time).Max() < end);
        }

        var total = await rows.CountAsync(cancellationToken);
        var orderedRows = (request.SortBy, request.SortDescending) switch
        {
            ("system", false) => rows.OrderBy(row => row.Transponder.System).ThenBy(row => row.Transponder.SystemId),
            ("system", true) => rows.OrderByDescending(row => row.Transponder.System).ThenByDescending(row => row.Transponder.SystemId),
            ("lastSeen", false) => rows.OrderBy(row => storage.Set<Passing>().Where(passing => passing.TransponderId == row.Transponder.Id)
                    .Select(passing => (DateTime?)passing.Time).Max() == null)
                .ThenBy(row => storage.Set<Passing>().Where(passing => passing.TransponderId == row.Transponder.Id)
                    .Select(passing => (DateTime?)passing.Time).Max()),
            ("lastSeen", true) => rows.OrderBy(row => storage.Set<Passing>().Where(passing => passing.TransponderId == row.Transponder.Id)
                    .Select(passing => (DateTime?)passing.Time).Max() == null)
                .ThenByDescending(row => storage.Set<Passing>().Where(passing => passing.TransponderId == row.Transponder.Id)
                    .Select(passing => (DateTime?)passing.Time).Max()),
            ("ownedLastSeen", _) => rows
                .OrderByDescending(row => ownerId.HasValue && row.Transponder.Owners.Any(owner =>
                    owner.OwnerId == ownerId.Value && owner.OwnedFrom <= now &&
                    (!owner.OwnedTo.HasValue || owner.OwnedTo > now)))
                .ThenBy(row => storage.Set<Passing>().Where(passing => passing.TransponderId == row.Transponder.Id)
                    .Select(passing => (DateTime?)passing.Time).Max() == null)
                .ThenByDescending(row => storage.Set<Passing>().Where(passing => passing.TransponderId == row.Transponder.Id)
                    .Select(passing => (DateTime?)passing.Time).Max()),
            ("ownedFrom", false) => rows.OrderBy(row => row.Owner == null ? (DateTime?)null : row.Owner.OwnedFrom),
            ("ownedFrom", true) => rows.OrderByDescending(row => row.Owner == null ? (DateTime?)null : row.Owner.OwnedFrom),
            ("ownedTo", false) => rows.OrderBy(row => row.Owner == null ? (DateTime?)null : row.Owner.OwnedTo),
            ("ownedTo", true) => rows.OrderByDescending(row => row.Owner == null ? (DateTime?)null : row.Owner.OwnedTo),
            ("status", false) => rows.OrderBy(row => row.Owner == null ? "Unassigned" : row.Owner.OwnedFrom > now ? "Scheduled" :
                row.Owner.OwnedTo.HasValue && row.Owner.OwnedTo <= now ? "Ended" : "Active"),
            ("status", true) => rows.OrderByDescending(row => row.Owner == null ? "Unassigned" : row.Owner.OwnedFrom > now ? "Scheduled" :
                row.Owner.OwnedTo.HasValue && row.Owner.OwnedTo <= now ? "Ended" : "Active"),
            ("label", false) => rows.OrderBy(row => row.Transponder.Label),
            ("label", true) => rows.OrderByDescending(row => row.Transponder.Label),
            (null or "", false) => rows.OrderBy(row => row.Transponder.Label),
            (null or "", true) => rows.OrderByDescending(row => row.Transponder.Label),
            _ => rows.OrderBy(row => row.Transponder.Label)
        };
        var page = await orderedRows.ThenBy(row => row.Transponder.Label).ThenBy(row => row.Transponder.Id)
            .ThenBy(row => row.Owner == null ? (DateTime?)null : row.Owner.OwnedFrom)
            .ThenBy(row => row.Owner == null ? (Guid?)null : row.Owner.Id)
            .Skip(request.Page * request.PageSize).Take(request.PageSize).ToListAsync(cancellationToken);
        var pageIds = page.Select(row => row.Transponder.Id).Distinct().ToArray();
        var lastSeen = !ownerId.HasValue && pageIds.Length > 0
            ? await storage.Set<Passing>().AsNoTracking().Where(passing => pageIds.Contains(passing.TransponderId))
                .GroupBy(passing => passing.TransponderId)
                .Select(group => new { Id = group.Key, Time = group.Max(passing => passing.Time) })
                .ToDictionaryAsync(item => item.Id, item => item.Time, cancellationToken)
            : new Dictionary<Guid, DateTime>();
        return new(page.Select(row => new TransponderRow(
            new(row.Transponder.Id, row.Transponder.System.ToString(), row.Transponder.SystemId, row.Transponder.Label, [],
                lastSeen.TryGetValue(row.Transponder.Id, out var time)
                    ? new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc)) : null, row.CanViewStatus),
            row.Owner is null ? null : TransponderOwnershipService.ToDto(row.Owner))).ToList(), total);
    }

    public static async Task<TransponderLookupPage> LookupAsync(TimingDbContext storage, int page, int pageSize,
        string? search, bool includeOwners, Guid? ownerId, CancellationToken cancellationToken)
    {
        Validate(page, pageSize);
        var query = Visible(storage, ownerId);
        if (!string.IsNullOrEmpty(search))
        {
            var text = search.ToUpperInvariant();
            query = query.Where(transponder => transponder.Label.ToUpper().Contains(text) || transponder.SystemId.ToUpper().Contains(text));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(transponder => transponder.Label).ThenBy(transponder => transponder.Id)
            .Skip(page * pageSize).Take(pageSize)
            .Select(transponder => new { transponder.Id, transponder.Label, transponder.SystemId }).ToListAsync(cancellationToken);
        var ids = items.Select(item => item.Id).ToArray();
        var owners = includeOwners && ids.Length > 0
            ? await storage.Set<TransponderOwner>().AsNoTracking()
                .Where(owner => ids.Contains(owner.TransponderId) && (!ownerId.HasValue || owner.OwnerId == ownerId))
                .OrderBy(owner => owner.Id).Select(owner => new { owner.TransponderId, owner.OwnerId }).ToListAsync(cancellationToken)
            : [];
        return new(items.Select(item => new TransponderLookupItem(item.Id, item.Label, item.SystemId,
            owners.Where(owner => owner.TransponderId == item.Id).Select(owner => owner.OwnerId).ToList())).ToList(), total);
    }

    private static IQueryable<Transponder> Visible(TimingDbContext storage, Guid? ownerId) =>
        storage.Set<Transponder>().AsNoTracking()
            .Where(transponder => !ownerId.HasValue || transponder.Owners.Any(owner => owner.OwnerId == ownerId));

    private static void ValidateSort(string? sortBy)
    {
        if (sortBy is not (null or "" or "label" or "system" or "lastSeen" or "ownedLastSeen" or "ownedFrom" or "ownedTo" or "status"))
            throw new ArgumentException("The transponder sort column is invalid.");
    }
}
