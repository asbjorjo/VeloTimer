using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Module.Statistics.Service;

public sealed class StatisticsOwnershipService(StatisticsDbContext storage, ITransponderOwnershipReader ownershipReader)
{
    private const int BatchSize = 500;

    public async Task<IReadOnlyList<StatisticsEntry>> CreateEntriesAsync(Guid transponderId,
        Func<CancellationToken, Task<IEnumerable<StatisticsEntry>>> create, CancellationToken cancellationToken)
    {
        await using var transaction = await LockAsync(transponderId, cancellationToken);
        var owners = await ReadOwnersAsync(transponderId, cancellationToken);
        var entries = (await create(cancellationToken)).ToList();
        await LockOwnersAsync(owners, cancellationToken);
        await AssignAsync(entries, owners, cancellationToken);
        await storage.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return entries;
    }

    public async Task<int> ReconcileAsync(TransponderOwnershipChanged message, CancellationToken cancellationToken)
    {
        var ranges = message.Changes.SelectMany(change => new[] { change.Before, change.After })
            .OfType<TransponderOwnershipDTO>().Distinct().ToList();
        foreach (var range in ranges) Validate(range, message.TransponderId);
        if (ranges.Count == 0) throw new ArgumentException("Ownership changes must contain at least one period.", nameof(message));

        await using var transaction = await LockAsync(message.TransponderId, cancellationToken);
        var owners = await ReadOwnersAsync(message.TransponderId, cancellationToken);
        await LockOwnersAsync(owners, cancellationToken);
        var transponderEntries = storage.Set<StatisticsEntry>().Where(entry => entry.TransponderId == message.TransponderId);
        var affected = transponderEntries.Where(_ => false);
        foreach (var range in ranges)
        {
            var start = range.OwnedFrom.UtcDateTime;
            var end = range.OwnedTo?.UtcDateTime;
            affected = affected.Union(transponderEntries.Where(entry =>
                entry.TimeEnd >= start && (!end.HasValue || entry.TimeStart < end.Value)));
        }

        var updated = 0;
        for (var offset = 0; ; offset += BatchSize)
        {
            var batch = await affected.OrderBy(entry => entry.Id).Skip(offset).Take(BatchSize).ToListAsync(cancellationToken);
            if (batch.Count == 0) break;
            updated += await AssignAsync(batch, owners, cancellationToken);
            await storage.SaveChangesAsync(cancellationToken);
            foreach (var entry in batch) storage.Entry(entry).State = EntityState.Detached;
        }
        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    private async Task<IReadOnlyList<TransponderOwnershipDTO>> ReadOwnersAsync(Guid transponderId, CancellationToken cancellationToken)
    {
        var owners = await ownershipReader.ReadAsync(transponderId, cancellationToken);
        foreach (var owner in owners) Validate(owner, transponderId);
        return owners;
    }

    private static void Validate(TransponderOwnershipDTO owner, Guid transponderId)
    {
        if (owner.TransponderId != transponderId || owner.OwnerId == Guid.Empty ||
            owner.OwnedTo.HasValue && owner.OwnedTo <= owner.OwnedFrom)
            throw new InvalidOperationException("Invalid transponder ownership period.");
    }

    private async Task<int> AssignAsync(IEnumerable<StatisticsEntry> entries,
        IReadOnlyList<TransponderOwnershipDTO> owners, CancellationToken cancellationToken)
    {
        var updated = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = owners.Where(owner => entry.TimeStart >= owner.OwnedFrom.UtcDateTime &&
                (!owner.OwnedTo.HasValue || entry.TimeStart < owner.OwnedTo.Value.UtcDateTime &&
                    entry.TimeEnd <= owner.OwnedTo.Value.UtcDateTime))
                .Select(owner => owner.OwnerId).Distinct().ToList();
            if (matches.Count > 1) throw new InvalidOperationException($"Conflicting owners for statistics entry {entry.Id}.");
            Guid? ownerId = matches.Count == 0 ? null : matches[0];
            if (ownerId.HasValue && await storage.Set<StatsProfile>().FindAsync([ownerId.Value], cancellationToken) is null)
                storage.Add(new StatsProfile { UserId = ownerId.Value, ProfileName = $"Rider {ownerId.Value:D}", IsPublic = true });
            if (entry.StatsProfileUserId == ownerId) continue;
            entry.StatsProfileUserId = ownerId;
            updated++;
        }
        return updated;
    }

    private async Task<IDbContextTransaction> LockAsync(Guid transponderId, CancellationToken cancellationToken)
    {
        var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Both subscriptions must serialize creation and reconciliation across processor replicas.
            await AdvisoryLockAsync($"statistics-transponder:{transponderId:D}", cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private async Task LockOwnersAsync(IReadOnlyList<TransponderOwnershipDTO> owners, CancellationToken cancellationToken)
    {
        foreach (var ownerId in owners.Select(owner => owner.OwnerId).Distinct().Order())
            await AdvisoryLockAsync($"statistics-profile:{ownerId:D}", cancellationToken);
    }

    private async Task AdvisoryLockAsync(string key, CancellationToken cancellationToken)
    {
        if (storage.Database.IsNpgsql())
            await storage.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }
}
