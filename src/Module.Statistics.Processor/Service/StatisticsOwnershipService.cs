using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Module.Statistics.Service;

public sealed class StatisticsOwnershipService(StatisticsDbContext storage, ITransponderOwnershipReader ownershipReader)
{
    private const int BatchSize = 500;

    public async Task<IReadOnlyList<StatisticsEntry>> CreateEntriesAsync(Guid transponderId,
        Func<CancellationToken, Task<IEnumerable<StatisticsEntry>>> create, CancellationToken cancellationToken,
        bool? enqueueSingle = null, SampleComplete? completion = null)
        => await CreateEntriesCoreAsync(transponderId, (_, ct) => create(ct), cancellationToken,
            enqueueSingle, completion, null);

    public async Task<IReadOnlyList<StatisticsEntry>> CreateEntriesFromSourceAsync(Guid transponderId,
        Func<Guid, CancellationToken, Task<IEnumerable<StatisticsEntry>>> create, CancellationToken cancellationToken,
        bool? enqueueSingle = null, SampleComplete? completion = null, EntryCreated? parent = null)
    {
        if ((completion is null) == (parent is null))
            throw new ArgumentException("Exactly one source message is required.");
        return await CreateEntriesCoreAsync(transponderId, create, cancellationToken, enqueueSingle, completion, parent);
    }

    private async Task<IReadOnlyList<StatisticsEntry>> CreateEntriesCoreAsync(Guid transponderId,
        Func<Guid, CancellationToken, Task<IEnumerable<StatisticsEntry>>> create, CancellationToken cancellationToken,
        bool? enqueueSingle, SampleComplete? completion, EntryCreated? parent)
    {
        await using var transaction = await LockAsync(transponderId, cancellationToken);
        var completionSource = completion is null
            ? null
            : await RequireCompletionSourceAsync(transponderId, completion, cancellationToken);
        var parentId = parent is null ? (Guid?)null : await RequireParentSourceAsync(transponderId, parent, cancellationToken);
        var owners = await ReadOwnersAsync(transponderId, cancellationToken);
        var entries = (await create(completionSource?.SampleId ?? parentId ?? Guid.Empty, cancellationToken)).ToList();
        await LockOwnersAsync(owners, cancellationToken);
        await AssignAsync(entries, owners, cancellationToken);
        if (enqueueSingle.HasValue)
        {
            // The transponder advisory lock serializes receipt lookup and sequence allocation
            // across both entry-producing subscriptions and all processor replicas.
            foreach (var entry in entries.DistinctBy(e => e.Id))
            {
                var existing = await storage.Set<EntryCreatedOutbox>()
                    .SingleOrDefaultAsync(o => o.EntryId == entry.Id, cancellationToken);
                if (existing is not null)
                {
                    if (existing.TransponderId != entry.TransponderId || existing.TimeStart != entry.TimeStart ||
                        existing.TimeEnd != entry.TimeEnd || existing.StatisticsItemId != entry.StatisticsItemId ||
                        existing.ConfigItemId != entry.StatisticsItemConfigId || existing.Single != enqueueSingle.Value)
                        throw new InvalidOperationException($"Conflicting EntryCreated payload for entry {entry.Id}.");
                    continue;
                }
                storage.Add(new EntryCreatedOutbox
                {
                    EntryId = entry.Id, TransponderId = entry.TransponderId,
                    TimeStart = entry.TimeStart, TimeEnd = entry.TimeEnd,
                    StatisticsItemId = entry.StatisticsItemId, ConfigItemId = entry.StatisticsItemConfigId,
                    Single = enqueueSingle.Value, MultiDecisionVersion = 1
                });
            }
        }
        // Empty results are still completed creation, not a durable "ignore future definitions" receipt.
        if (completionSource is not null)
            completionSource.EntriesCreatedAtUtc ??= DateTime.UtcNow;
        await storage.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return entries;
    }

    private async Task<SampleCompleteOutbox> RequireCompletionSourceAsync(Guid transponderId,
        SampleComplete completion, CancellationToken cancellationToken)
    {
        var start = StatisticsStoragePrecision.Timestamp(completion.TimeStart);
        var end = StatisticsStoragePrecision.Timestamp(completion.TimeEnd);
        if (completion.TransponderId != transponderId || completion.SampleId == Guid.Empty || !double.IsFinite(completion.Distance))
            throw new InvalidOperationException("Invalid SampleComplete source identity or payload.");
        var query = storage.Set<SampleCompleteOutbox>().AsQueryable();
        if (completion.SampleId.HasValue)
            query = query.Where(source => source.SampleId == completion.SampleId.Value);
        else
            query = query.Where(source =>
                source.TransponderId == transponderId && source.TimeStart == start && source.TimeEnd == end &&
                source.CoursePointStart == completion.CoursePointStart &&
                source.CoursePointEnd == completion.CoursePointEnd && source.Distance == completion.Distance);
        var matches = await query.OrderBy(source => source.Id).Take(2).ToListAsync(cancellationToken);

        if (matches.Count == 0)
            throw new InvalidOperationException(
                $"SampleComplete for transponder {transponderId:D} has no matching local outbox receipt; " +
                "external or legacy messages require maintenance reconciliation.");
        if (matches.Count != 1)
            throw new InvalidOperationException(
                $"SampleComplete for transponder {transponderId:D} matches multiple local outbox receipts.");

        var source = matches[0];
        if (source.SampleId == Guid.Empty || source.TransponderId != transponderId ||
            StatisticsStoragePrecision.Timestamp(source.TimeStart) != start || StatisticsStoragePrecision.Timestamp(source.TimeEnd) != end ||
            source.CoursePointStart != completion.CoursePointStart || source.CoursePointEnd != completion.CoursePointEnd ||
            source.Distance != completion.Distance)
            throw new InvalidOperationException("SampleComplete conflicts with its local outbox receipt.");
        if (!completion.SampleId.HasValue)
        {
            // A unique receipt is not enough: another matching sample may have lost its receipt.
            var sampleIds = await storage.Set<Sample>().AsNoTracking().Where(sample =>
                    sample.TransponderId == transponderId && sample.TimeStart == start && sample.TimeEnd == end &&
                    sample.CoursePointStartId == completion.CoursePointStart &&
                    sample.CoursePointEndId == completion.CoursePointEnd && sample.Distance == completion.Distance)
                .Select(sample => sample.Id).Take(2).ToListAsync(cancellationToken);
            if (sampleIds.Count != 1 || sampleIds[0] != source.SampleId)
                throw new InvalidOperationException("SampleComplete has no unique persisted source sample matching its receipt.");
        }
        if (await storage.Set<SampleCompleteOutbox>().AnyAsync(earlier =>
                earlier.TransponderId == transponderId && earlier.Id < source.Id &&
                earlier.EntriesCreatedAtUtc == null, cancellationToken))
            throw new InvalidOperationException(
                $"SampleComplete for transponder {transponderId:D} is blocked by an earlier unprocessed local sample.");

        return source;
    }

    private async Task<Guid> RequireParentSourceAsync(Guid transponderId, EntryCreated message, CancellationToken cancellationToken)
    {
        var start = StatisticsStoragePrecision.Timestamp(message.TimeStart);
        var end = StatisticsStoragePrecision.Timestamp(message.TimeEnd);
        if (message.TransponderId != transponderId || message.EntryId == Guid.Empty)
            throw new InvalidOperationException("Invalid EntryCreated source identity or payload.");
        var query = storage.Set<StatisticsEntry>().AsNoTracking().AsQueryable();
        if (message.EntryId.HasValue)
            query = query.Where(entry => entry.Id == message.EntryId.Value);
        else
            query = query.Where(entry => entry.TransponderId == transponderId &&
                entry.StatisticsItemConfigId == message.ConfigItemId && entry.TimeStart == start && entry.TimeEnd == end);
        var matches = await query.Take(2).ToListAsync(cancellationToken);
        if (matches.Count != 1)
            throw new InvalidOperationException("EntryCreated has no unique persisted parent entry; maintenance reconciliation is required.");
        var entry = matches[0];
        if (entry.Id == Guid.Empty || entry.TransponderId != transponderId || entry.StatisticsItemId != message.StatisticsItemId ||
            entry.StatisticsItemConfigId != message.ConfigItemId || entry.TimeStart != start || entry.TimeEnd != end)
            throw new InvalidOperationException("EntryCreated conflicts with its persisted parent entry.");
        var receipt = await storage.Set<EntryCreatedOutbox>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.EntryId == entry.Id, cancellationToken);
        if (receipt is null || receipt.TransponderId != transponderId || receipt.TimeStart != start ||
            receipt.TimeEnd != end || receipt.StatisticsItemId != message.StatisticsItemId || receipt.ConfigItemId != message.ConfigItemId)
            throw new InvalidOperationException("EntryCreated has no matching retained parent receipt; maintenance reconciliation is required.");
        return entry.Id;
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
            // Local receipt insertion, completion processing and ownership reconciliation
            // serialize per transponder across processor replicas.
            await StatisticsTransponderLock.AcquireAsync(storage.Database, transponderId, cancellationToken);
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
