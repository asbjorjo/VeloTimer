using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using SlimMessageBus;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Interface.Messages;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Service;

public sealed class TransponderOwnershipService(TimingDbContext storage, IMessageBus messageBus, ILogger<TransponderOwnershipService> logger)
{
    /// <summary>Registers a transponder and reconciles the selected owner's coverage under a transaction lock.</summary>
    /// <param name="request">The requested period and, when needed, the confirmed ownership snapshot.</param>
    /// <param name="ownerId">The authenticated subject or administrator-selected owner.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A saved outcome, or the unchanged ownership proposal requiring confirmation.</returns>
    public async Task<RegisterTransponderResult> RegisterAsync(RegisterTransponderRequest request, Guid ownerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code)) throw new ArgumentException("Transponder code is required.");
        if (ownerId == Guid.Empty) throw new ArgumentException("A valid owner is required.");
        if (request.OwnedFrom == default) throw new ArgumentException("Registration start is required.");
        var start = request.OwnedFrom.UtcDateTime;
        var end = request.OwnedTo?.UtcDateTime;
        if (end.HasValue && end <= start) throw new ArgumentException("Ownership end must be after its start.");
        var systemId = SystemId(request.Code);
        await using var transaction = await LockAsync(systemId, cancellationToken);
        var transponder = await FindOrCreateAsync(systemId, cancellationToken);
        Validate(transponder, null, ownerId, start, end);

        var affected = transponder.Owners.Where(owner => owner.OwnerId == ownerId &&
            (!owner.OwnedTo.HasValue || owner.OwnedTo > start) && (!end.HasValue || owner.OwnedFrom < end))
            .OrderBy(owner => owner.OwnedFrom).ThenBy(owner => owner.Id).ToList();
        if (affected.Count == 1 && affected[0].OwnedFrom == start && affected[0].OwnedTo == end)
            return new(false, null, []);
        var snapshot = transponder.Owners.OrderBy(owner => owner.Id).Select(ToDto).ToList();
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            systemId, ownerId, start, end, snapshot
        }))));
        var shortens = affected.Any(owner => owner.OwnedFrom < start ||
            end.HasValue && (!owner.OwnedTo.HasValue || owner.OwnedTo > end));
        if ((shortens && request.ConfirmationToken != token) ||
            (request.ConfirmationToken is not null && request.ConfirmationToken != token))
            return new(true, token, affected.Select(ToDto).ToList());

        var before = affected.Select(ToDto).ToList();
        if (storage.Entry(transponder).State == EntityState.Detached) storage.Add(transponder);
        var ownership = affected.FirstOrDefault();
        if (ownership is null)
        {
            ownership = new TransponderOwner { Id = Guid.NewGuid(), TransponderId = transponder.Id, OwnerId = ownerId };
            transponder.Owners.Add(ownership);
            storage.Add(ownership);
        }
        foreach (var duplicate in affected.Skip(1)) storage.Remove(duplicate);
        ownership.OwnedFrom = start;
        ownership.OwnedTo = end;
        await SaveAndEnqueueAsync(transaction, transponder.Id, before, [ToDto(ownership)], cancellationToken);
        return new(false, null, []);
    }

    public async Task<TransponderDTO> ClaimAsync(string code, Guid ownerId, CancellationToken cancellationToken)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("A valid owner is required.");
        var systemId = SystemId(code);
        await using var transaction = await LockAsync(systemId, cancellationToken);
        var transponder = await FindOrCreateAsync(systemId, cancellationToken);
        if (storage.Entry(transponder).State == EntityState.Detached) storage.Add(transponder);

        var now = DateTime.UtcNow;
        if (!transponder.Owners.Any(owner => owner.OwnerId == ownerId && owner.OwnedFrom <= now && (owner.OwnedTo is null || owner.OwnedTo > now)))
        {
            Validate(transponder, null, ownerId, now, null);
            var ownership = new TransponderOwner { Id = Guid.NewGuid(), OwnerId = ownerId, TransponderId = transponder.Id, OwnedFrom = now };
            transponder.Owners.Add(ownership);
            storage.Add(ownership);
            await SaveAndEnqueueAsync(transaction, transponder.Id, [], [ToDto(ownership)], cancellationToken);
        }
        else await transaction.CommitAsync(cancellationToken);

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
        TransponderOwnershipDTO[] before = owner is null ? [] : [ToDto(owner)];
        if (owner is null)
        {
            owner = new TransponderOwner { Id = Guid.NewGuid(), TransponderId = transponder.Id };
            transponder.Owners.Add(owner);
            storage.Add(owner);
        }
        owner.OwnerId = request.OwnerId;
        owner.OwnedFrom = start;
        owner.OwnedTo = end;
        await SaveAndEnqueueAsync(transaction, transponder.Id, before, [ToDto(owner)], cancellationToken);
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
        var before = ToDto(owner);
        if (end)
        {
            var now = DateTime.UtcNow;
            if (owner.OwnedFrom >= now || owner.OwnedTo.HasValue && owner.OwnedTo <= now)
                throw new ArgumentException("Only an active ownership period can be ended.");
            owner.OwnedTo = now;
        }
        else storage.Remove(owner);
        await SaveAndEnqueueAsync(transaction, owner.TransponderId, [before], end ? [ToDto(owner)] : [], cancellationToken);
    }

    private async Task SaveAndEnqueueAsync(IDbContextTransaction transaction, Guid transponderId,
        IReadOnlyList<TransponderOwnershipDTO> before, IReadOnlyList<TransponderOwnershipDTO> after, CancellationToken cancellationToken)
    {
        var original = before.ToDictionary(owner => owner.Id);
        await storage.SaveChangesAsync(cancellationToken);
        var afterIds = after.Select(owner => owner.Id).ToArray();
        // Read persisted values so snapshots reflect database timestamp precision, not unsaved CLR precision.
        var persisted = afterIds.Length == 0 ? [] : await storage.Set<TransponderOwner>().AsNoTracking()
            .Where(owner => afterIds.Contains(owner.Id)).ToListAsync(cancellationToken);
        var updated = persisted.Select(ToDto).ToDictionary(owner => owner.Id);
        var changes = original.Keys.Union(updated.Keys).Order()
            .Select(id => new TransponderOwnershipChange(original.GetValueOrDefault(id), updated.GetValueOrDefault(id)))
            .Where(change => change.Before != change.After).ToList();

        if (changes.Count > 0)
        {
            try
            {
                // The scoped outbox uses this context's connection and its active PostgreSQL transaction.
                await messageBus.Publish(new TransponderOwnershipChanged(Guid.NewGuid(), transponderId, changes), cancellationToken: cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not enqueue ownership changes for transponder {TransponderId}; the transaction will roll back.", transponderId);
                throw new OwnershipChangeEnqueueException(exception);
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static string SystemId(string code)
    {
        var numericId = MylapsX2Transponder.CodeToId(code.Trim().ToUpperInvariant());
        _ = MylapsX2Transponder.IdToCode(numericId);
        return numericId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<Transponder> FindOrCreateAsync(string systemId, CancellationToken cancellationToken) =>
        await storage.Set<Transponder>().Include(transponder => transponder.Owners)
            .SingleOrDefaultAsync(transponder => transponder.System == TimingSystem.MyLaps_X2 && transponder.SystemId == systemId, cancellationToken)
        ?? new MylapsX2Transponder { Id = Guid.NewGuid(), SystemId = systemId };

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