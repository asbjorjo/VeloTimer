using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.Module.Statistics.Service;

public sealed class StatisticsOwnershipService(StatisticsDbContext storage, ITransponderOwnershipReader ownershipReader)
{
    /// <summary>
    /// The only writer of entry owners (plan §5.5): assigns owners to derived entries inside the recompute's transaction, which already holds the
    /// transponder lock (see <see cref="LockTransponderAsync"/>). Owner profiles are locked in UUID order.
    /// </summary>
    public async Task ApplyOwnersAsync(Guid transponderId, IReadOnlyList<StatisticsEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0) return;
        var owners = await ReadOwnersAsync(transponderId, cancellationToken);
        await LockOwnersAsync(owners, cancellationToken);
        await AssignAsync(entries, owners, cancellationToken);
    }

    /// <summary>Serializes recomputes and ownership reconciliation for one transponder across replicas.</summary>
    public Task LockTransponderAsync(Guid transponderId, CancellationToken cancellationToken) =>
        StatisticsLocks.LockTransponderAsync(storage, transponderId, cancellationToken);

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

    private async Task LockOwnersAsync(IReadOnlyList<TransponderOwnershipDTO> owners, CancellationToken cancellationToken)
    {
        foreach (var ownerId in owners.Select(owner => owner.OwnerId).Distinct().Order())
            await AdvisoryLockAsync($"statistics-profile:{ownerId:D}", cancellationToken);
    }

    private Task AdvisoryLockAsync(string key, CancellationToken cancellationToken) =>
        StatisticsLocks.AdvisoryLockAsync(storage, key, cancellationToken);
}
