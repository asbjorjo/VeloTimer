using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VeloTime.Module.Common;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Interface.Messages;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Service;

/// <summary>Reads and edits Facilities-owned effective-time coursepoint mappings.</summary>
public sealed class TimingAssociationService
{
    private readonly FacilityDbContext storage;
    private readonly IDataProtector? confirmationProtector;

    public TimingAssociationService(FacilityDbContext storage, IDataProtectionProvider? protectionProvider = null)
    {
        this.storage = storage;
        confirmationProtector = protectionProvider?.CreateProtector("VeloTime.Facilities.TimingAssociationConfirmation.v1");
    }

    public static DateTimeOffset Normalize(DateTimeOffset value)
    {
        try
        {
            var utc = value.ToUniversalTime();
            return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new TimingAssociationValidationException("Association bounds must be finite representable UTC instants.", exception);
        }
    }

    public static bool Contains(CoursePointTimingAssociation association, DateTimeOffset at) =>
        (!association.ValidFromUtc.HasValue || association.ValidFromUtc.Value <= at) &&
        (!association.ValidToUtc.HasValue || at < association.ValidToUtc.Value);

    public Task<Guid?> ResolveTimingPointIdAsync(Guid coursePointId, DateTimeOffset at, CancellationToken cancellationToken) =>
        storage.Set<CoursePointTimingAssociation>().AsNoTracking()
            .Where(association => association.CoursePointId == coursePointId &&
                (!association.ValidFromUtc.HasValue || association.ValidFromUtc <= at) &&
                (!association.ValidToUtc.HasValue || at < association.ValidToUtc))
            .Select(association => (Guid?)association.TimingPointId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<CoursePoint?> FindCoursePointByTimingPointIdAsync(Guid timingPointId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var association = await storage.Set<CoursePointTimingAssociation>().AsNoTracking()
            .Where(candidate => candidate.TimingPointId == timingPointId &&
                (!candidate.ValidFromUtc.HasValue || candidate.ValidFromUtc <= at) &&
                (!candidate.ValidToUtc.HasValue || at < candidate.ValidToUtc))
            .Select(candidate => candidate.CoursePoint)
            .SingleOrDefaultAsync(cancellationToken);
        return association;
    }

    /// <summary>Resolves both endpoints against one snapshot, even across backdated edits.</summary>
    public async Task<(CoursePoint? Start, CoursePoint? End)> ResolveDistanceEndpointsAsync(
        Guid start, Guid end, DateTimeOffset startAt, DateTimeOffset endAt, CancellationToken cancellationToken)
    {
        startAt = Normalize(startAt);
        endAt = Normalize(endAt);
        // Do not nest transactions or silently accept a caller's read-committed snapshot.
        var current = storage.Database.CurrentTransaction;
        if (current is not null && current.GetDbTransaction().IsolationLevel is not
            (System.Data.IsolationLevel.RepeatableRead or System.Data.IsolationLevel.Serializable))
            throw new InvalidOperationException("Distance endpoint resolution requires a repeatable snapshot.");
        // PostgreSQL supports repeatable read; SQLite uses serializable for the same guarantee.
        await using var transaction = current is null && storage.Database.IsRelational()
            ? await storage.Database.BeginTransactionAsync(storage.Database.IsNpgsql()
                ? System.Data.IsolationLevel.RepeatableRead
                : System.Data.IsolationLevel.Serializable, cancellationToken)
            : null;
        var startPoint = await FindCoursePointByTimingPointIdAsync(start, startAt, cancellationToken);
        var endPoint = start == end && startAt == endAt
            ? startPoint
            : await FindCoursePointByTimingPointIdAsync(end, endAt, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return (startPoint, endPoint);
    }

    public async Task<IReadOnlyDictionary<Guid, Guid?>> ResolveManyAsync(
        IEnumerable<Guid> coursePointIds, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var ids = coursePointIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, Guid?>();
        var mappings = await storage.Set<CoursePointTimingAssociation>().AsNoTracking()
            .Where(association => ids.Contains(association.CoursePointId) &&
                (!association.ValidFromUtc.HasValue || association.ValidFromUtc <= at) &&
                (!association.ValidToUtc.HasValue || at < association.ValidToUtc))
            .Select(association => new { association.CoursePointId, association.TimingPointId })
            .ToListAsync(cancellationToken);
        return mappings.ToDictionary(mapping => mapping.CoursePointId, mapping => (Guid?)mapping.TimingPointId);
    }

    public async Task<IReadOnlyList<CoursePointTimingAssociationSnapshot>> GetHistoryAsync(
        Guid coursePointId, CancellationToken cancellationToken)
    {
        var snapshots = await storage.Set<CoursePointTimingAssociation>().AsNoTracking()
            .Where(association => association.CoursePointId == coursePointId)
            .Select(association => new CoursePointTimingAssociationSnapshot(
                association.TimingPointId, association.CoursePointId,
                association.ValidFromUtc, association.ValidToUtc))
            .ToListAsync(cancellationToken);
        // Contract order: lower bound ascending with null (-infinity) first, then upper bound
        // ascending with null (+infinity) last. One coursepoint's history is small, so ordering
        // in memory keeps the null placement explicit and provider-independent.
        return OrderSnapshots(snapshots);
    }

    /// <summary>
    /// Protects the exact approved conflict state: the requested change, every destination
    /// overlap, and every complete source association of the requested coursepoint that overlaps
    /// the requested interval. Source state is bound internally only; the preview shape is unchanged.
    /// </summary>
    public string CreateConfirmationToken(TimingAssociationRequestedChange requested,
        IReadOnlyList<TimingAssociationDestinationConflict> conflicts,
        IReadOnlyList<CoursePointTimingAssociationSnapshot> sourceState)
    {
        var state = JsonSerializer.Serialize(new ConfirmationState(requested, conflicts, sourceState));
        return RequireConfirmationProtector().Protect(state);
    }

    public bool ConfirmationMatches(string? token, TimingAssociationRequestedChange requested,
        IReadOnlyList<TimingAssociationDestinationConflict> conflicts,
        IReadOnlyList<CoursePointTimingAssociationSnapshot> sourceState)
    {
        if (string.IsNullOrEmpty(token)) return false;
        try
        {
            var actual = RequireConfirmationProtector().Unprotect(token);
            var expected = JsonSerializer.Serialize(new ConfirmationState(requested, conflicts, sourceState));
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public async Task<TimingAssociationEditResult> SetIntervalAsync(Guid coursePointId,
        SetCoursePointTimingAssociationRequest request, DateTimeOffset occurredAtUtc,
        IAdministrationReferences references,
        Func<CoursePointTimingAssociationsChangedV1, CancellationToken, Task> enqueueEvent,
        CancellationToken cancellationToken)
        => await SetIntervalCoreAsync(coursePointId, request, occurredAtUtc, references,
            enqueueEvent, false, cancellationToken);

    /// <summary>
    /// Reads a fresh, consistent confirmation state after a failed edit has rolled back and
    /// disposed its transaction. Never retries the mutation or uses failed tracked entities.
    /// </summary>
    public async Task<TimingAssociationEditResult> GetConcurrencyPreviewAsync(Guid coursePointId,
        SetCoursePointTimingAssociationRequest request, IAdministrationReferences references,
        CancellationToken cancellationToken)
    {
        if (storage.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Concurrency recovery requires the failed transaction to be disposed.");
        storage.ChangeTracker.Clear();
        return await SetIntervalCoreAsync(coursePointId, request, default, references,
            null, true, cancellationToken);
    }

    private async Task<TimingAssociationEditResult> SetIntervalCoreAsync(Guid coursePointId,
        SetCoursePointTimingAssociationRequest request, DateTimeOffset occurredAtUtc,
        IAdministrationReferences references,
        Func<CoursePointTimingAssociationsChangedV1, CancellationToken, Task>? enqueueEvent,
        bool previewOnly, CancellationToken cancellationToken)
    {
        if (request.TimingPointId == Guid.Empty)
            throw new TimingAssociationValidationException("TimingPointId must be null or a non-empty ID.");
        if (request.ValidFromUtc.HasValue) request = request with { ValidFromUtc = Normalize(request.ValidFromUtc.Value) };
        if (request.ValidToUtc.HasValue) request = request with { ValidToUtc = Normalize(request.ValidToUtc.Value) };
        if (request.ValidFromUtc == DateTimeOffset.MinValue || request.ValidToUtc == DateTimeOffset.MinValue)
            throw new TimingAssociationValidationException("Association bounds must not use the minimum UTC instant, which PostgreSQL stores as negative infinity.");
        if (request.ValidFromUtc.HasValue && request.ValidToUtc.HasValue &&
            request.ValidFromUtc.Value >= request.ValidToUtc.Value)
            throw new TimingAssociationValidationException("ValidFromUtc must be earlier than ValidToUtc.");

        var from = request.ValidFromUtc;
        var to = request.ValidToUtc;
        var requested = new TimingAssociationRequestedChange(coursePointId, request.TimingPointId, from, to);
        // Recovery must see source and destination from one snapshot, including writers that
        // do not participate in our advisory lock. A subsequent confirmation rechecks both
        // under the normal write lock; this read-only path never upgrades to a mutation.
        await using var transaction = previewOnly
            ? await storage.Database.BeginTransactionAsync(storage.Database.IsNpgsql()
                ? System.Data.IsolationLevel.RepeatableRead
                : System.Data.IsolationLevel.Serializable, cancellationToken)
            : await storage.Database.BeginTransactionAsync(cancellationToken);
        if (!previewOnly) await AcquireWriteLockAsync(cancellationToken);

        var point = await storage.Set<CoursePoint>().AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == coursePointId, cancellationToken);
        if (point is null) throw new KeyNotFoundException("The coursepoint does not exist.");
        var facilityId = await PointFacilityAsync(coursePointId, cancellationToken);
        if (request.TimingPointId is { } timingPointId)
        {
            var target = await references.GetAsync("timing", "timingpoints", timingPointId, cancellationToken);
            if (target is null || !facilityId.HasValue || target.FacilityId != facilityId.Value)
                throw new TimingAssociationValidationException("The timing point must belong to an installation in the same facility.");
        }

        var associations = previewOnly
            ? storage.Set<CoursePointTimingAssociation>().AsNoTracking()
            : storage.Set<CoursePointTimingAssociation>();
        var overlaps = request.TimingPointId is { } destinationId
            ? await associations
                .Where(association => association.TimingPointId == destinationId &&
                    association.CoursePointId != coursePointId &&
                    (!association.ValidToUtc.HasValue || !from.HasValue || association.ValidToUtc > from) &&
                    (!to.HasValue || !association.ValidFromUtc.HasValue || association.ValidFromUtc < to))
                .ToListAsync(cancellationToken)
            : [];
        var conflicts = overlaps.Select(association =>
        {
            var overlap = Intersect(from, to, association.ValidFromUtc, association.ValidToUtc);
            return new TimingAssociationDestinationConflict(ToSnapshot(association), overlap.From, overlap.To);
        }).OrderBy(conflict => conflict.OverlapValidFromUtc.HasValue)
            .ThenBy(conflict => conflict.OverlapValidFromUtc)
            .ThenByDescending(conflict => conflict.OverlapValidToUtc.HasValue)
            .ThenBy(conflict => conflict.OverlapValidToUtc)
            .ToList();

        // Read the requested coursepoint's overlapping source rows in the same locked transaction
        // so a confirmation is rejected when either side changed after the preview was issued.
        var source = await associations
            .Where(association => association.CoursePointId == coursePointId &&
                (!association.ValidToUtc.HasValue || !from.HasValue || association.ValidToUtc > from) &&
                (!to.HasValue || !association.ValidFromUtc.HasValue || association.ValidFromUtc < to))
            .ToListAsync(cancellationToken);
        var sourceState = OrderSnapshots(source.Select(ToSnapshot));

        if (previewOnly)
            return await FreshPreviewAsync("confirmation-stale", requested, conflicts, sourceState, transaction, cancellationToken);

        if (request.ConfirmationToken is not null)
        {
            if (!ConfirmationMatches(request.ConfirmationToken, requested, conflicts, sourceState))
                return await FreshPreviewAsync("confirmation-stale", requested, conflicts, sourceState, transaction, cancellationToken);
        }
        else if (conflicts.Count > 0)
        {
            return await FreshPreviewAsync("destination-occupied", requested, conflicts, sourceState, transaction, cancellationToken);
        }

        await DeferExclusionConstraintsAsync(cancellationToken);

        var deltas = new List<CoursePointTimingAssociationChange>();
        var destinationChanges = new List<(CoursePointTimingAssociation Association, Interval Overlap)>();
        foreach (var association in overlaps)
        {
            var overlap = Intersect(from, to, association.ValidFromUtc, association.ValidToUtc);
            destinationChanges.Add((association, overlap));
            deltas.Add(new CoursePointTimingAssociationChange(
                Snapshot(association.TimingPointId, association.CoursePointId, overlap.From, overlap.To), null));
        }

        var sameMappingIntervals = source
            .Where(association => request.TimingPointId == association.TimingPointId)
            .Select(association => Intersect(from, to, association.ValidFromUtc, association.ValidToUtc))
            .ToList();
        foreach (var association in source.Where(association => request.TimingPointId != association.TimingPointId))
        {
            var overlap = Intersect(from, to, association.ValidFromUtc, association.ValidToUtc);
            deltas.Add(new CoursePointTimingAssociationChange(
                Snapshot(association.TimingPointId, coursePointId, overlap.From, overlap.To),
                request.TimingPointId is { } replacement
                    ? Snapshot(replacement, coursePointId, overlap.From, overlap.To)
                    : null));
        }

        if (request.TimingPointId is { } assignedTimingPoint)
        {
            var gaps = Subtract(new Interval(from, to), source.Select(association =>
                Intersect(from, to, association.ValidFromUtc, association.ValidToUtc)).ToList());
            foreach (var gap in gaps)
                deltas.Add(new CoursePointTimingAssociationChange(null,
                    Snapshot(assignedTimingPoint, coursePointId, gap.From, gap.To)));
        }

        // A mapping that already has the requested value throughout the interval is a true no-op.
        if (deltas.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return new TimingAssociationEditResult(null, [], null, facilityId);
        }
        if (!facilityId.HasValue)
            throw new TimingAssociationValidationException("A coursepoint with association history must remain attached to its facility.");
        var sourceToReplace = source.Where(association => request.TimingPointId != association.TimingPointId).ToList();
        var changedAssociations = sourceToReplace.Concat(overlaps).DistinctBy(association => association.Id).ToList();
        foreach (var association in changedAssociations)
        {
            var (left, right) = Remainder(association, from, to);
            storage.Remove(association);
            if (left is { } leftWindow) storage.Add(CreateAssociation(association, leftWindow));
            if (right is { } rightWindow) storage.Add(CreateAssociation(association, rightWindow));
        }

        if (request.TimingPointId is { } newTimingPointId)
        {
            foreach (var window in Subtract(new Interval(from, to), sameMappingIntervals))
            {
                storage.Add(new CoursePointTimingAssociation
                {
                    Id = Guid.NewGuid(),
                    CoursePointId = coursePointId,
                    TimingPointId = newTimingPointId,
                    ValidFromUtc = window.From,
                    ValidToUtc = window.To
                });
            }
        }

        // Bounds are already normalized to PostgreSQL's microsecond precision, so the event
        // snapshots exactly describe the persisted temporal slices.
        await storage.SaveChangesAsync(cancellationToken);
        var changeId = Guid.NewGuid();
        var message = new CoursePointTimingAssociationsChangedV1(
            Guid.NewGuid(), changeId, Normalize(occurredAtUtc), facilityId ?? Guid.Empty, deltas);
        await enqueueEvent!(message, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TimingAssociationEditResult(changeId, deltas, null, facilityId);
    }

    private async Task<TimingAssociationEditResult> FreshPreviewAsync(string code,
        TimingAssociationRequestedChange requested, IReadOnlyList<TimingAssociationDestinationConflict> conflicts,
        IReadOnlyList<CoursePointTimingAssociationSnapshot> sourceState,
        IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        var preview = new TimingAssociationConflictPreview(code, requested, conflicts,
            CreateConfirmationToken(requested, conflicts, sourceState));
        await transaction.RollbackAsync(cancellationToken);
        return new TimingAssociationEditResult(null, [], preview, null);
    }

    private async Task AcquireWriteLockAsync(CancellationToken cancellationToken)
    {
        if (storage.Database.IsNpgsql())
            await storage.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(731905124)", cancellationToken);
    }

    private async Task DeferExclusionConstraintsAsync(CancellationToken cancellationToken)
    {
        if (storage.Database.IsNpgsql())
            await storage.Database.ExecuteSqlRawAsync(
                "SET CONSTRAINTS facility.ex_timing_association_timing_point, facility.ex_timing_association_course_point DEFERRED",
                cancellationToken);
    }

    private async Task<Guid?> PointFacilityAsync(Guid coursePointId, CancellationToken cancellationToken) =>
        await storage.Set<CourseSegment>().Where(segment =>
                segment.Start.Id == coursePointId || segment.End.Id == coursePointId)
            .Select(segment => (Guid?)segment.CourseLayout.FacilityId).Distinct()
            .SingleOrDefaultAsync(cancellationToken);

    private static CoursePointTimingAssociation CreateAssociation(CoursePointTimingAssociation original, Interval window) => new()
    {
        Id = Guid.NewGuid(),
        CoursePointId = original.CoursePointId,
        TimingPointId = original.TimingPointId,
        ValidFromUtc = window.From,
        ValidToUtc = window.To
    };

    private static (Interval? Left, Interval? Right) Remainder(
        CoursePointTimingAssociation association, DateTimeOffset? from, DateTimeOffset? to)
    {
        Interval? left = from.HasValue &&
            (!association.ValidFromUtc.HasValue || association.ValidFromUtc.Value < from.Value)
            ? new Interval(association.ValidFromUtc, from)
            : null;
        Interval? right = to.HasValue &&
            (!association.ValidToUtc.HasValue || to.Value < association.ValidToUtc.Value)
            ? new Interval(to, association.ValidToUtc)
            : null;
        return (left, right);
    }

    private static Interval Intersect(DateTimeOffset? leftFrom, DateTimeOffset? leftTo,
        DateTimeOffset? rightFrom, DateTimeOffset? rightTo) =>
        new(MaxLower(leftFrom, rightFrom), MinUpper(leftTo, rightTo));

    private static DateTimeOffset? MaxLower(DateTimeOffset? left, DateTimeOffset? right) =>
        !left.HasValue ? right : !right.HasValue ? left : left.Value >= right.Value ? left : right;

    private static DateTimeOffset? MinUpper(DateTimeOffset? left, DateTimeOffset? right) =>
        !left.HasValue ? right : !right.HasValue ? left : left.Value <= right.Value ? left : right;

    private static List<Interval> Subtract(Interval window, IReadOnlyList<Interval> cuts)
    {
        var relevant = cuts.Select(cut => Intersect(window.From, window.To, cut.From, cut.To))
            .OrderBy(cut => cut.From.HasValue)
            .ThenBy(cut => cut.From)
            .ToList();
        var result = new List<Interval>();
        var cursor = window.From;
        foreach (var cut in relevant)
        {
            if (cut.To.HasValue && cursor.HasValue && cut.To.Value <= cursor.Value) continue;
            if (cut.From.HasValue && (!cursor.HasValue || cursor.Value < cut.From.Value))
                result.Add(new Interval(cursor, cut.From));
            if (!cut.To.HasValue)
            {
                cursor = null;
                break;
            }
            if (!cursor.HasValue || cut.To.Value > cursor.Value) cursor = cut.To;
        }

        var lastCutCoversInfinity = relevant.Any(cut => !cut.To.HasValue);
        if (!lastCutCoversInfinity && (!window.To.HasValue || !cursor.HasValue || cursor.Value < window.To.Value))
            result.Add(new Interval(cursor, window.To));
        return result.Where(interval => !interval.From.HasValue || !interval.To.HasValue ||
            interval.From.Value < interval.To.Value).ToList();
    }

    private static IReadOnlyList<CoursePointTimingAssociationSnapshot> OrderSnapshots(
        IEnumerable<CoursePointTimingAssociationSnapshot> snapshots) =>
        snapshots.OrderBy(snapshot => snapshot.ValidFromUtc.HasValue)
            .ThenBy(snapshot => snapshot.ValidFromUtc)
            .ThenByDescending(snapshot => snapshot.ValidToUtc.HasValue)
            .ThenBy(snapshot => snapshot.ValidToUtc)
            .ThenBy(snapshot => snapshot.TimingPointId)
            .ToList();

    private static CoursePointTimingAssociationSnapshot ToSnapshot(CoursePointTimingAssociation association) =>
        Snapshot(association.TimingPointId, association.CoursePointId, association.ValidFromUtc, association.ValidToUtc);

    private static CoursePointTimingAssociationSnapshot Snapshot(Guid timingPointId, Guid coursePointId,
        DateTimeOffset? from, DateTimeOffset? to) => new(timingPointId, coursePointId, from, to);

    private IDataProtector RequireConfirmationProtector() =>
        confirmationProtector ?? throw new InvalidOperationException(
            "A configured ASP.NET Core data-protection provider is required for conflict confirmations.");

    private readonly record struct Interval(DateTimeOffset? From, DateTimeOffset? To);
    private sealed record ConfirmationState(TimingAssociationRequestedChange Requested,
        IReadOnlyList<TimingAssociationDestinationConflict> Conflicts,
        IReadOnlyList<CoursePointTimingAssociationSnapshot> Source);
}

public sealed record TimingAssociationEditResult(Guid? ChangeId,
    IReadOnlyList<CoursePointTimingAssociationChange> Changes,
    TimingAssociationConflictPreview? ConflictPreview, Guid? FacilityId);

public sealed class TimingAssociationValidationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
