using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Module.Statistics.Service;

public sealed class StatisticsOwnershipService(StatisticsDbContext storage, ITransponderOwnershipReader ownershipReader,
    StatisticsHeadRelease heads)
{
    private const int BatchSize = 500;

    public async Task<IReadOnlyList<StatisticsEntry>> CreateEntriesAsync(Guid transponderId,
        Func<CancellationToken, Task<IEnumerable<StatisticsEntry>>> create, CancellationToken cancellationToken,
        bool? enqueueSingle = null, SampleComplete? completion = null)
        => await CreateEntriesCoreAsync(transponderId, (_, ct) => create(ct), cancellationToken,
            enqueueSingle, completion, null, null);

    public async Task<IReadOnlyList<StatisticsEntry>> CreateEntriesFromSourceAsync(Guid transponderId,
        Func<Guid, CancellationToken, Task<IEnumerable<StatisticsEntry>>> create, CancellationToken cancellationToken,
        bool? enqueueSingle = null, SampleComplete? completion = null, EntryCreated? parent = null,
        IReadOnlyDictionary<string, object>? headers = null)
    {
        if ((completion is null) == (parent is null))
            throw new ArgumentException("Exactly one source message is required.");
        return await CreateEntriesCoreAsync(transponderId, create, cancellationToken, enqueueSingle, completion, parent, headers);
    }

    private async Task<IReadOnlyList<StatisticsEntry>> CreateEntriesCoreAsync(Guid transponderId,
        Func<Guid, CancellationToken, Task<IEnumerable<StatisticsEntry>>> create, CancellationToken cancellationToken,
        bool? enqueueSingle, SampleComplete? completion, EntryCreated? parent, IReadOnlyDictionary<string, object>? headers)
    {
        await using var transaction = await LockAsync(transponderId, cancellationToken);
        var completionSource = completion is null
            ? null
            : await RequireCompletionSourceAsync(transponderId, completion, cancellationToken);
        var parentSource = parent is null ? null : await RequireParentSourceAsync(transponderId, parent, cancellationToken);
        if (completionSource is not null)
        {
            if (headers is not null && completionSource.SmbEnqueuedAtUtc.HasValue && completion?.SampleId != completionSource.SampleId)
                throw new InvalidOperationException("Enqueued SampleComplete requires its stable sample identity.");
            ValidateHeaders(headers, nameof(SampleComplete), completionSource.SampleId,
                completionSource.SmbEnqueuedAtUtc, completionSource.EntriesCreatedAtUtc, null);
        }
        if (parentSource is not null)
        {
            if (headers is not null && parentSource.SmbEnqueuedAtUtc.HasValue && parent?.EntryId != parentSource.EntryId)
                throw new InvalidOperationException("Enqueued EntryCreated requires its stable entry identity.");
            ValidateHeaders(headers, nameof(EntryCreated), parentSource.EntryId,
                parentSource.SmbEnqueuedAtUtc, parentSource.MultiDecisionsCreatedAtUtc, parentSource.Single);
        }
        // Validate immutable identity/payload/headers even for late duplicates, but do
        // not consult live definitions, owners, deleted domain objects or release again.
        if (completionSource?.EntriesCreatedAtUtc is not null || parentSource?.MultiDecisionsCreatedAtUtc is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return [];
        }
        if (parentSource is not null && await storage.Set<EntryCreatedOutbox>().AnyAsync(earlier =>
                earlier.TransponderId == transponderId && earlier.Id < parentSource.Id &&
                earlier.MultiDecisionsCreatedAtUtc == null, cancellationToken))
            throw new InvalidOperationException("EntryCreated is blocked by an earlier unprocessed local receipt.");
        var owners = await ReadOwnersAsync(transponderId, cancellationToken);
        var entries = (await create(completionSource?.SampleId ?? parentSource?.EntryId ?? Guid.Empty, cancellationToken)).ToList();
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
        // Empty results are final completion too; redelivery must not discover later definitions.
        if (completionSource is not null)
            completionSource.EntriesCreatedAtUtc ??= DateTime.UtcNow;
        if (parentSource is not null)
            parentSource.MultiDecisionsCreatedAtUtc ??= DateTime.UtcNow;
        await storage.SaveChangesAsync(cancellationToken);
        // StatisticsService itself may append derived EntryCreated receipts. Select
        // heads only after ALL its work, ownership and completion have been saved.
        await heads.ReleaseAsync(transponderId, cancellationToken);
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
        if (!source.EntriesCreatedAtUtc.HasValue && await storage.Set<SampleCompleteOutbox>().AnyAsync(earlier =>
                earlier.TransponderId == transponderId && earlier.Id < source.Id &&
                earlier.EntriesCreatedAtUtc == null, cancellationToken))
            throw new InvalidOperationException(
                $"SampleComplete for transponder {transponderId:D} is blocked by an earlier unprocessed local sample.");

        return source;
    }

    private async Task<EntryCreatedOutbox> RequireParentSourceAsync(Guid transponderId, EntryCreated message, CancellationToken cancellationToken)
    {
        var start = StatisticsStoragePrecision.Timestamp(message.TimeStart);
        var end = StatisticsStoragePrecision.Timestamp(message.TimeEnd);
        if (message.TransponderId != transponderId || message.EntryId == Guid.Empty)
            throw new InvalidOperationException("Invalid EntryCreated source identity or payload.");
        var query = storage.Set<EntryCreatedOutbox>().AsQueryable();
        if (message.EntryId.HasValue)
            query = query.Where(entry => entry.EntryId == message.EntryId.Value);
        else
            query = query.Where(entry => entry.TransponderId == transponderId &&
                entry.ConfigItemId == message.ConfigItemId && entry.TimeStart == start && entry.TimeEnd == end);
        var matches = await query.Take(2).ToListAsync(cancellationToken);
        if (matches.Count != 1)
            throw new InvalidOperationException("EntryCreated has no unique retained parent receipt; maintenance reconciliation is required.");
        var receipt = matches[0];
        if (receipt.EntryId == Guid.Empty || receipt.TransponderId != transponderId || receipt.TimeStart != start ||
            receipt.TimeEnd != end || receipt.StatisticsItemId != message.StatisticsItemId || receipt.ConfigItemId != message.ConfigItemId)
            throw new InvalidOperationException("EntryCreated has no matching retained parent receipt; maintenance reconciliation is required.");
        if (receipt.MultiDecisionVersion != 1)
            throw new InvalidOperationException("EntryCreated has legacy/unknown Multi decisions; maintenance reconciliation is required.");
        if (!message.EntryId.HasValue)
        {
            var ids = await storage.Set<StatisticsEntry>().AsNoTracking().Where(entry =>
                    entry.TransponderId == transponderId && entry.StatisticsItemConfigId == message.ConfigItemId &&
                    entry.TimeStart == start && entry.TimeEnd == end)
                .Select(entry => entry.Id).Take(2).ToListAsync(cancellationToken);
            if (ids.Count != 1 || ids[0] != receipt.EntryId)
                throw new InvalidOperationException("Legacy EntryCreated has no unique persisted parent matching its retained receipt.");
        }
        return receipt;
    }

    private static void ValidateHeaders(IReadOnlyDictionary<string, object>? headers, string messageName, Guid id,
        DateTime? enqueued, DateTime? completed, bool? single)
    {
        // Direct domain callers are not broker deliveries. Both production consumers
        // always supply their context; missing context fails in the handler.
        if (headers is null) return;
        if (!enqueued.HasValue && !completed.HasValue)
            throw new InvalidOperationException("Unenqueued/legacy pending receipt cannot be processed; reconcile before activation.");
        if (enqueued.HasValue || headers.ContainsKey("MessageName"))
        {
            if (!headers.TryGetValue("MessageName", out var name) || name is not string text || text != messageName)
                throw new InvalidOperationException("Statistics MessageName conflicts with the receipt.");
        }
        if (enqueued.HasValue || headers.ContainsKey(StatisticsHeadRelease.ReceiptHeader))
        {
            if (!headers.TryGetValue(StatisticsHeadRelease.ReceiptHeader, out var value) ||
                value is not string key || !Guid.TryParseExact(key, "D", out var receiptId) || receiptId != id)
                throw new InvalidOperationException("StatisticsReceiptId conflicts with the receipt.");
        }
        var hasSingle = headers.TryGetValue("Single", out var flag);
        if (single == true ? !hasSingle || flag is not true : hasSingle)
            throw new InvalidOperationException("Single header conflicts with the receipt.");
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
