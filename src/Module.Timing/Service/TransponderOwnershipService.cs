using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Service;

public sealed class TransponderOwnershipService(TimingDbContext storage)
{
    public async Task<TransponderDTO> ClaimAsync(string code, Guid ownerId, CancellationToken cancellationToken)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("A valid owner is required.");
        var numericId = MylapsX2Transponder.CodeToId(code.Trim().ToUpperInvariant());
        _ = MylapsX2Transponder.IdToCode(numericId);
        var systemId = numericId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await using var transaction = await LockAsync(systemId, cancellationToken);
        var transponder = await storage.Set<Transponder>().Include(transponder => transponder.Owners)
            .SingleOrDefaultAsync(transponder => transponder.System == TimingSystem.MyLaps_X2 && transponder.SystemId == systemId, cancellationToken);
        if (transponder is null)
        {
            transponder = new MylapsX2Transponder { Id = Guid.NewGuid(), SystemId = systemId };
            storage.Add(transponder);
        }

        var now = DateTime.UtcNow;
        if (!transponder.Owners.Any(owner => owner.OwnerId == ownerId && owner.OwnedFrom <= now && (owner.OwnedTo is null || owner.OwnedTo > now)))
        {
            Validate(transponder, null, ownerId, now, null);
            var ownership = new TransponderOwner { Id = Guid.NewGuid(), OwnerId = ownerId, TransponderId = transponder.Id, OwnedFrom = now };
            transponder.Owners.Add(ownership);
            storage.Add(ownership);
            await storage.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return ToDto(transponder, ownerId);
    }

    public async Task SaveAsync(Guid? id, SaveTransponderOwnershipRequest request, CancellationToken cancellationToken)
    {
        var identity = await storage.Set<Transponder>().AsNoTracking().FirstOrDefaultAsync(transponder => transponder.Id == request.TransponderId, cancellationToken)
            ?? throw new KeyNotFoundException();
        await using var transaction = await LockAsync(identity.SystemId, cancellationToken);
        var transponder = await storage.Set<Transponder>().Include(transponder => transponder.Owners)
            .SingleAsync(transponder => transponder.Id == request.TransponderId, cancellationToken);
        var owner = id.HasValue ? transponder.Owners.SingleOrDefault(owner => owner.Id == id) ?? throw new KeyNotFoundException() : null;
        var start = request.OwnedFrom.UtcDateTime;
        var end = request.OwnedTo?.UtcDateTime;
        Validate(transponder, id, request.OwnerId, start, end);
        if (owner is null)
        {
            owner = new TransponderOwner { Id = Guid.NewGuid(), TransponderId = transponder.Id };
            transponder.Owners.Add(owner);
            storage.Add(owner);
        }
        owner.OwnerId = request.OwnerId;
        owner.OwnedFrom = start;
        owner.OwnedTo = end;
        await storage.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RemoveOrEndAsync(Guid id, Guid? requiredOwnerId, bool end, CancellationToken cancellationToken)
    {
        var ownership = await storage.Set<TransponderOwner>().AsNoTracking().Include(owner => owner.Transponder)
            .FirstOrDefaultAsync(owner => owner.Id == id && (!requiredOwnerId.HasValue || owner.OwnerId == requiredOwnerId.Value), cancellationToken)
            ?? throw new KeyNotFoundException();
        await using var transaction = await LockAsync(ownership.Transponder.SystemId, cancellationToken);
        var owner = await storage.Set<TransponderOwner>()
            .FirstOrDefaultAsync(owner => owner.Id == id && (!requiredOwnerId.HasValue || owner.OwnerId == requiredOwnerId.Value), cancellationToken)
            ?? throw new KeyNotFoundException();
        if (end)
        {
            var now = DateTime.UtcNow;
            if (owner.OwnedFrom >= now || owner.OwnedTo.HasValue && owner.OwnedTo <= now)
                throw new ArgumentException("Only an active ownership period can be ended.");
            owner.OwnedTo = now;
        }
        else storage.Remove(owner);
        await storage.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<IDbContextTransaction> LockAsync(string systemId, CancellationToken cancellationToken)
    {
        var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (storage.Database.IsNpgsql())
            {
                var key = $"transponder-ownership:{systemId}";
                await storage.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
            }
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private static void Validate(Transponder transponder, Guid? excludedId, Guid ownerId, DateTime start, DateTime? end)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("A valid owner is required.");
        if (end.HasValue && end <= start) throw new ArgumentException("Ownership end must be after its start.");
        if (transponder.Owners.Any(owner => owner.Id != excludedId && owner.OwnerId != ownerId &&
            (!owner.OwnedTo.HasValue || owner.OwnedTo > start) && (!end.HasValue || owner.OwnedFrom < end)))
            throw new InvalidOperationException("The ownership period overlaps an existing owner.");
    }

    public static TransponderDTO ToDto(Transponder transponder, Guid? ownerId = null) => new(transponder.Id, transponder.System.ToString(), transponder.SystemId, transponder.Label,
        transponder.Owners.Where(owner => !ownerId.HasValue || owner.OwnerId == ownerId).OrderByDescending(owner => owner.OwnedFrom).Select(ToDto).ToList());

    public static TransponderOwnershipDTO ToDto(TransponderOwner owner) => new(owner.Id, owner.TransponderId, owner.OwnerId,
        new DateTimeOffset(DateTime.SpecifyKind(owner.OwnedFrom, DateTimeKind.Utc)), owner.OwnedTo.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(owner.OwnedTo.Value, DateTimeKind.Utc)) : null);
}