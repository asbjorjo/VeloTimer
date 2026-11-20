using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Service;

public static class TransponderStatusQuery
{
    public static async Task<TransponderStatus?> GetAsync(TimingDbContext storage, Guid id, Guid? ownerId,
        DateTimeOffset now, int ownershipPage, int warningPage, int pageSize, CancellationToken cancellationToken)
    {
        TransponderQuery.Validate(ownershipPage, pageSize);
        TransponderQuery.Validate(warningPage, pageSize);
        var utcNow = now.UtcDateTime;
        var transponder = await storage.Set<Transponder>().AsNoTracking()
            .Where(item => item.Id == id && (!ownerId.HasValue || item.Owners.Any(owner =>
                owner.OwnerId == ownerId && owner.OwnedFrom <= utcNow &&
                (!owner.OwnedTo.HasValue || owner.OwnedTo > utcNow))))
            .SingleOrDefaultAsync(cancellationToken);
        if (transponder is null) return null;

        var ownerships = storage.Set<TransponderOwner>().AsNoTracking()
            .Where(owner => owner.TransponderId == id && (!ownerId.HasValue || owner.OwnerId == ownerId));
        var ownershipCount = await ownerships.CountAsync(cancellationToken);
        var owners = await ownerships.OrderByDescending(owner => owner.OwnedFrom).ThenBy(owner => owner.Id)
            .Skip(ownershipPage * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var passings = storage.Set<Passing>().AsNoTracking()
            .Where(passing => passing.TransponderId == id && passing.Time <= utcNow);
        if (ownerId.HasValue)
            passings = passings.Where(passing => ownerships.Any(owner =>
                owner.OwnedFrom <= passing.Time && (!owner.OwnedTo.HasValue || passing.Time < owner.OwnedTo)));

        var lastSeen = await Sightings(passings.OrderByDescending(passing => passing.Time).ThenBy(passing => passing.Id))
            .FirstOrDefaultAsync(cancellationToken);
        var cutoff = utcNow.AddDays(-7);
        var warnings = passings.Where(passing => passing.Time >= cutoff &&
            (passing.LowBattery || passing.LowStrength || passing.LowHits));
        var count = await warnings.CountAsync(cancellationToken);
        var lowBattery = await warnings.AnyAsync(passing => passing.LowBattery, cancellationToken);
        var lowStrength = await warnings.AnyAsync(passing => passing.LowStrength, cancellationToken);
        var lowHits = await warnings.AnyAsync(passing => passing.LowHits, cancellationToken);
        var warningPageItems = await Sightings(warnings.OrderByDescending(passing => passing.Time).ThenBy(passing => passing.Id)
            .Skip(warningPage * pageSize).Take(pageSize)).ToListAsync(cancellationToken);
        return new(transponder.Id, transponder.Label,
            transponder is MylapsX2Transponder ? MylapsX2Transponder.IdToCode(transponder.SystemId) : transponder.SystemId,
            transponder.SystemId, transponder.System.ToString(), !ownerId.HasValue, lastSeen,
            new DateTimeOffset(cutoff, TimeSpan.Zero), now.ToUniversalTime(),
            lowBattery, lowStrength, lowHits, owners.Select(TransponderOwnershipService.ToDto).ToList(),
            ownershipCount, warningPageItems, count);
    }

    private static IQueryable<TransponderSighting> Sightings(IQueryable<Passing> query) =>
        query.Select(passing => new TransponderSighting(passing.Id,
            new DateTimeOffset(passing.Time, TimeSpan.Zero),
            passing.TimingPoint.Installation.Facility, passing.TimingPoint.InstallationId,
            passing.TimingPoint.Installation.Description, passing.TimingPointId, passing.TimingPoint.Description,
            passing.LowBattery, passing.LowStrength, passing.LowHits));
}
