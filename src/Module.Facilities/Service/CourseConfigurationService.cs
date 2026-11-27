using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VeloTime.Module.Facilities.Interface.Course;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Interface.Messages;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Service;

/// <summary>
/// Reads and edits a facility's time-bound layout configuration: when each layout is in use
/// (<see cref="FacilityLayoutAssignment"/>) and its geometry over time (<see cref="CourseLayoutRevision"/>).
/// Every effective edit enqueues one <see cref="CourseConfigChangedV1"/> in the edit's transaction
/// (docs/replay-safe-statistics-plan.md §4.1). Edits close, split or insert versions; they never change a
/// revision's segments in place.
/// </summary>
public sealed class CourseConfigurationService
{
    /// <summary>Shared with timing-association edits: both change one facility's course configuration.</summary>
    private const long WriteLockKey = 731905124;

    private readonly FacilityDbContext storage;
    private readonly IDataProtector? confirmationProtector;

    public CourseConfigurationService(FacilityDbContext storage, IDataProtectionProvider? protectionProvider = null)
    {
        this.storage = storage;
        confirmationProtector = protectionProvider?.CreateProtector("VeloTime.Facilities.CourseLayoutRevisionConfirmation.v1");
    }

    // ---- Reading ---------------------------------------------------------------------------------

    /// <summary>Layouts with their assignments and revisions (with segments and points) loaded.</summary>
    public static IQueryable<CourseLayout> LayoutsWithHistory(FacilityDbContext storage) => storage.Set<CourseLayout>()
        .Include(layout => layout.Assignments)
        .Include(layout => layout.Revisions).ThenInclude(revision => revision.Segments).ThenInclude(segment => segment.Start)
        .Include(layout => layout.Revisions).ThenInclude(revision => revision.Segments).ThenInclude(segment => segment.End)
        .AsSplitQuery();

    /// <summary>The geometry of every layout in use at <paramref name="at"/>, as the shared distance rules take it.</summary>
    public static List<CourseLayoutGeometry> GeometryAt(IEnumerable<CourseLayout> layouts, DateTimeOffset at) =>
        layouts.Where(layout => layout.InUseAt(at))
            .Select(layout => (layout, revision: layout.RevisionAt(at)))
            .Where(pair => pair.revision is not null)
            .Select(pair => Geometry(pair.layout.Id, pair.revision!))
            .ToList();

    public static CourseLayoutGeometry Geometry(Guid layoutId, CourseLayoutRevision revision) =>
        new(layoutId, revision.Segments.OrderBy(segment => segment.Order)
            .Select(segment => new CourseSegmentGeometry(segment.Order, segment.Start.Id, segment.End.Id, segment.Length))
            .ToList());

    /// <summary>Every course point used by any revision of the facility's layouts, with no duplicates.</summary>
    public static IEnumerable<CoursePoint> Points(IEnumerable<CourseLayout> layouts) =>
        layouts.SelectMany(layout => layout.Revisions).SelectMany(revision => revision.Segments)
            .SelectMany(segment => new[] { segment.Start, segment.End }).DistinctBy(point => point.Id);

    // ---- Geometry validation ---------------------------------------------------------------------

    /// <summary>The error a client should see for invalid geometry, or null when it is valid.</summary>
    public async Task<string?> ValidateGeometryAsync(Guid facilityId, IReadOnlyList<SaveCoursePointRequest>? points,
        IReadOnlyList<SaveCourseSegmentRequest>? segments, CancellationToken cancellationToken)
    {
        if (points is null || segments is null || segments.Count == 0) return "A layout must have at least one segment.";
        if (points.Count > 1000 || segments.Count > 1000) return "A layout can contain at most 1000 points and segments.";
        if (points.Any(point => point.Id == Guid.Empty || string.IsNullOrWhiteSpace(point.Name)) ||
            points.Select(point => point.Id).Distinct().Count() != points.Count)
            return "Each coursepoint needs a unique ID and a name.";
        if (segments.Any(segment => !double.IsFinite(segment.Length) || segment.Length <= 0))
            return "Each segment needs a positive finite length in metres.";
        var usedIds = segments.SelectMany(segment => new[] { segment.StartId, segment.EndId }).ToHashSet();
        var pointIds = points.Select(point => point.Id).ToArray();
        if (!usedIds.SetEquals(pointIds)) return "Every segment endpoint must refer to a point in this layout; remove unused points.";
        if (segments.Zip(segments.Skip(1)).Any(pair => pair.First.EndId != pair.Second.StartId))
            return "Segments must form a connected course in their displayed order.";
        if (segments.Select(segment => segment.StartId).Distinct().Count() != segments.Count ||
            segments.Select(segment => segment.EndId).Distinct().Count() != segments.Count)
            return "A coursepoint may only occur once along the course, except for closing a lap.";
        if (await storage.Set<CourseSegment>().AnyAsync(segment => segment.Revision.CourseLayout.FacilityId != facilityId &&
                (pointIds.Contains(segment.Start.Id) || pointIds.Contains(segment.End.Id)), cancellationToken))
            return "Coursepoints cannot be shared across different facilities.";
        return null;
    }

    // ---- Creating and deleting layouts -----------------------------------------------------------

    /// <summary>
    /// Creates a layout in use for all time with one unbounded revision, as one change. The caller validated the
    /// geometry with <see cref="ValidateGeometryAsync"/>.
    /// </summary>
    public async Task<CourseLayout> CreateLayoutAsync(Guid facilityId, IReadOnlyList<SaveCoursePointRequest> points,
        IReadOnlyList<SaveCourseSegmentRequest> segments, DateTimeOffset occurredAtUtc,
        Func<CourseConfigChangedV1, CancellationToken, Task> enqueueEvent, CancellationToken cancellationToken)
    {
        await using var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
        await AcquireWriteLockAsync(cancellationToken);
        var layout = new CourseLayout { Id = Guid.NewGuid(), FacilityId = facilityId };
        storage.Add(layout);
        layout.Assignments.Add(new FacilityLayoutAssignment { Id = Guid.NewGuid(), FacilityId = facilityId, CourseLayoutId = layout.Id, CourseLayout = layout });
        var revision = await NewRevisionAsync(layout, points, segments, new Interval(null, null), cancellationToken);
        layout.Revisions.Add(revision);
        await storage.SaveChangesAsync(cancellationToken);
        await enqueueEvent(Event(occurredAtUtc, facilityId, CourseConfigChangeKind.LayoutAssignment, new Interval(null, null), layout.Id),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return layout;
    }

    /// <summary>
    /// Deletes a layout with its whole history, and the course points no other layout uses. Returns false when the
    /// layout does not exist. Throws <see cref="CourseConfigurationValidationException"/> when an orphaned point still
    /// has association history.
    /// </summary>
    public async Task<bool> DeleteLayoutAsync(Guid facilityId, Guid layoutId, DateTimeOffset occurredAtUtc,
        Func<CourseConfigChangedV1, CancellationToken, Task> enqueueEvent, CancellationToken cancellationToken,
        PointDeletionCheck? canDeletePoint = null)
    {
        await using var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
        await AcquireWriteLockAsync(cancellationToken);
        var layout = await LayoutsWithHistory(storage)
            .SingleOrDefaultAsync(candidate => candidate.Id == layoutId && candidate.FacilityId == facilityId, cancellationToken);
        if (layout is null) return false;
        var points = Points([layout]).ToList();
        foreach (var revision in layout.Revisions) storage.RemoveRange(revision.Segments);
        storage.RemoveRange(layout.Revisions);
        storage.RemoveRange(layout.Assignments);
        storage.Remove(layout);
        await storage.SaveChangesAsync(cancellationToken);
        await RemoveOrphanPointsAsync(points, canDeletePoint, cancellationToken);
        await storage.SaveChangesAsync(cancellationToken);
        await enqueueEvent(Event(occurredAtUtc, facilityId, CourseConfigChangeKind.LayoutDeleted, new Interval(null, null), layoutId),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // ---- Revisions -------------------------------------------------------------------------------

    /// <summary>
    /// Replaces the layout's geometry over the requested interval with a new revision. When the interval overlaps
    /// existing revisions, the edit needs a confirmation token from a preview of exactly those overlaps; without one
    /// (or with a stale one) nothing changes and the result carries a fresh preview. The caller validated the
    /// geometry with <see cref="ValidateGeometryAsync"/>.
    /// </summary>
    public async Task<CourseLayoutRevisionEditResult> SetRevisionAsync(Guid facilityId, Guid layoutId,
        SetCourseLayoutRevisionRequest request, DateTimeOffset occurredAtUtc,
        Func<CourseConfigChangedV1, CancellationToken, Task> enqueueEvent, CancellationToken cancellationToken,
        PointDeletionCheck? canDeletePoint = null)
    {
        var window = NormalizeInterval(request.ValidFromUtc, request.ValidToUtc);
        await using var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
        await AcquireWriteLockAsync(cancellationToken);
        var layout = await LayoutsWithHistory(storage)
            .SingleOrDefaultAsync(candidate => candidate.Id == layoutId && candidate.FacilityId == facilityId, cancellationToken);
        if (layout is null) throw new KeyNotFoundException("The course layout does not exist.");

        var requested = request.Segments.Select((segment, index) => new CourseSegmentGeometry(index + 1, segment.StartId, segment.EndId, segment.Length)).ToList();
        var overlapping = layout.Revisions.Where(revision => IntervalMath.Overlaps(IntervalOf(revision), window))
            .OrderBy(revision => revision.ValidFromUtc.HasValue).ThenBy(revision => revision.ValidFromUtc).ToList();
        var covered = IntervalMath.Subtract(window, overlapping.Select(IntervalOf)).Count == 0;
        if (covered && overlapping.All(revision => SameGeometry(Geometry(layout.Id, revision).Segments, requested)))
        {
            // Same geometry throughout: only point names may change, and names do not affect any result.
            foreach (var point in Points([layout]))
                if (request.Points.FirstOrDefault(requestedPoint => requestedPoint.Id == point.Id) is { } renamed)
                    point.Name = renamed.Name.Trim();
            await storage.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(null, null, null);
        }

        var overlaps = overlapping.Select(revision =>
        {
            var overlap = IntervalMath.Intersect(IntervalOf(revision), window);
            return new CourseLayoutRevisionOverlap(revision.Id, revision.ValidFromUtc, revision.ValidToUtc, overlap.From, overlap.To,
                revision.Segments.Sum(segment => segment.Length), revision.Segments.Count);
        }).ToList();
        if (overlaps.Count > 0)
        {
            var state = new RevisionConfirmationState(layoutId, window.From, window.To, requested, overlaps);
            if (request.ConfirmationToken is null || !ConfirmationMatches(request.ConfirmationToken, state))
            {
                var preview = new CourseLayoutRevisionConflictPreview(request.ConfirmationToken is null ? "revision-overlap" : "confirmation-stale",
                    layoutId, window.From, window.To, overlaps, Protect(state));
                await transaction.RollbackAsync(cancellationToken);
                return new(null, null, preview);
            }
        }

        await DeferExclusionConstraintsAsync(cancellationToken);
        var previousPoints = Points([layout]).ToList();
        foreach (var revision in overlapping)
        {
            var (left, right) = IntervalMath.Remainder(IntervalOf(revision), window);
            if (left is { } keep)
            {
                revision.ValidToUtc = keep.To;
                if (right is { } split)
                    layout.Revisions.Add(CopyRevision(layout, revision, split));
            }
            else if (right is { } rest)
                revision.ValidFromUtc = rest.From;
            else
            {
                storage.RemoveRange(revision.Segments);
                storage.Remove(revision);
                layout.Revisions.Remove(revision);
            }
        }
        var created = await NewRevisionAsync(layout, request.Points, request.Segments, window, cancellationToken);
        layout.Revisions.Add(created);
        await storage.SaveChangesAsync(cancellationToken);
        await RemoveOrphanPointsAsync(previousPoints, canDeletePoint, cancellationToken);
        await storage.SaveChangesAsync(cancellationToken);

        var message = Event(occurredAtUtc, facilityId, CourseConfigChangeKind.LayoutRevision, window, layoutId);
        await enqueueEvent(message, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(message.ChangeId, created.Id, null);
    }

    // ---- Assignments -----------------------------------------------------------------------------

    /// <summary>Sets whether the facility runs the layout over the requested interval.</summary>
    public async Task<FacilityLayoutAssignmentEditResult> SetAssignmentAsync(Guid facilityId, Guid layoutId,
        SetFacilityLayoutAssignmentRequest request, DateTimeOffset occurredAtUtc,
        Func<CourseConfigChangedV1, CancellationToken, Task> enqueueEvent, CancellationToken cancellationToken)
    {
        var window = NormalizeInterval(request.ValidFromUtc, request.ValidToUtc);
        await using var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
        await AcquireWriteLockAsync(cancellationToken);
        var layout = await storage.Set<CourseLayout>().Include(candidate => candidate.Assignments)
            .SingleOrDefaultAsync(candidate => candidate.Id == layoutId && candidate.FacilityId == facilityId, cancellationToken);
        if (layout is null) throw new KeyNotFoundException("The course layout does not exist.");

        var overlapping = layout.Assignments.Where(assignment => IntervalMath.Overlaps(IntervalOf(assignment), window)).ToList();
        var changed = false;
        await DeferExclusionConstraintsAsync(cancellationToken);
        if (request.InUse)
        {
            foreach (var gap in IntervalMath.Subtract(window, overlapping.Select(IntervalOf)))
            {
                changed = true;
                layout.Assignments.Add(NewAssignment(layout, gap));
            }
        }
        else
        {
            foreach (var assignment in overlapping)
            {
                changed = true;
                var (left, right) = IntervalMath.Remainder(IntervalOf(assignment), window);
                if (left is { } keep)
                {
                    assignment.ValidToUtc = keep.To;
                    if (right is { } split) layout.Assignments.Add(NewAssignment(layout, split));
                }
                else if (right is { } rest)
                    assignment.ValidFromUtc = rest.From;
                else
                {
                    storage.Remove(assignment);
                    layout.Assignments.Remove(assignment);
                }
            }
        }
        if (!changed)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(null, Intervals(layout.Assignments));
        }
        await storage.SaveChangesAsync(cancellationToken);
        var message = Event(occurredAtUtc, facilityId, CourseConfigChangeKind.LayoutAssignment, window, layoutId);
        await enqueueEvent(message, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(message.ChangeId, Intervals(layout.Assignments));
    }

    public static IReadOnlyList<EffectiveIntervalDTO> Intervals(IEnumerable<IEffectiveInterval> items) =>
        items.OrderBy(item => item.ValidFromUtc.HasValue).ThenBy(item => item.ValidFromUtc)
            .Select(item => new EffectiveIntervalDTO { ValidFromUtc = item.ValidFromUtc, ValidToUtc = item.ValidToUtc })
            .ToList();

    // ---- Helpers ---------------------------------------------------------------------------------

    private async Task<CourseLayoutRevision> NewRevisionAsync(CourseLayout layout, IReadOnlyList<SaveCoursePointRequest> points,
        IReadOnlyList<SaveCourseSegmentRequest> segments, Interval window, CancellationToken cancellationToken)
    {
        var pointIds = points.Select(point => point.Id).ToArray();
        var known = await storage.Set<CoursePoint>().Where(point => pointIds.Contains(point.Id)).ToDictionaryAsync(point => point.Id, cancellationToken);
        foreach (var requested in points)
        {
            if (!known.TryGetValue(requested.Id, out var point))
            {
                point = new CoursePoint { Id = requested.Id };
                known.Add(point.Id, point);
                storage.Add(point);
            }
            point.Name = requested.Name.Trim();
        }
        var revision = new CourseLayoutRevision
        {
            Id = Guid.NewGuid(), CourseLayoutId = layout.Id, CourseLayout = layout, ValidFromUtc = window.From, ValidToUtc = window.To
        };
        for (var index = 0; index < segments.Count; index++)
        {
            var requested = segments[index];
            revision.Segments.Add(new CourseSegment
            {
                Id = Guid.NewGuid(), Revision = revision, CourseLayoutRevisionId = revision.Id, Order = index + 1,
                Start = known[requested.StartId], End = known[requested.EndId], Length = requested.Length
            });
        }
        storage.Add(revision);
        return revision;
    }

    private CourseLayoutRevision CopyRevision(CourseLayout layout, CourseLayoutRevision source, Interval window)
    {
        var copy = new CourseLayoutRevision
        {
            Id = Guid.NewGuid(), CourseLayoutId = layout.Id, CourseLayout = layout, ValidFromUtc = window.From, ValidToUtc = window.To
        };
        foreach (var segment in source.Segments.OrderBy(segment => segment.Order))
            copy.Segments.Add(new CourseSegment
            {
                Id = Guid.NewGuid(), Revision = copy, CourseLayoutRevisionId = copy.Id, Order = segment.Order,
                Start = segment.Start, End = segment.End, Length = segment.Length
            });
        storage.Add(copy);
        return copy;
    }

    private static FacilityLayoutAssignment NewAssignment(CourseLayout layout, Interval window) => new()
    {
        Id = Guid.NewGuid(), FacilityId = layout.FacilityId, CourseLayoutId = layout.Id, CourseLayout = layout,
        ValidFromUtc = window.From, ValidToUtc = window.To
    };

    /// <summary>Deletes the given points when no segment uses them any more. Points with association history stay.</summary>
    private async Task RemoveOrphanPointsAsync(IEnumerable<CoursePoint> candidates, PointDeletionCheck? canDeletePoint,
        CancellationToken cancellationToken)
    {
        foreach (var point in candidates)
        {
            var used = await storage.Set<CourseSegment>().AnyAsync(segment => segment.Start.Id == point.Id || segment.End.Id == point.Id, cancellationToken)
                       || storage.ChangeTracker.Entries<CourseSegment>().Any(entry => entry.State != EntityState.Deleted &&
                           (entry.Entity.Start.Id == point.Id || entry.Entity.End.Id == point.Id));
            if (used) continue;
            if (await storage.Set<CoursePointTimingAssociation>().AnyAsync(association => association.CoursePointId == point.Id, cancellationToken))
                throw new CourseConfigurationConflictException(
                    $"Coursepoint '{point.Name}' has effective association history and cannot be deleted.");
            if (canDeletePoint is not null && await canDeletePoint(point.Id, cancellationToken) is { } blocked)
                throw new CourseConfigurationConflictException(blocked);
            storage.Remove(point);
        }
    }

    private static bool SameGeometry(IReadOnlyList<CourseSegmentGeometry> left, IReadOnlyList<CourseSegmentGeometry> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => pair.First.StartPointId == pair.Second.StartPointId &&
            pair.First.EndPointId == pair.Second.EndPointId && pair.First.Length == pair.Second.Length);

    private static Interval IntervalOf(IEffectiveInterval item) => new(item.ValidFromUtc, item.ValidToUtc);

    private static Interval NormalizeInterval(DateTimeOffset? from, DateTimeOffset? to)
    {
        var window = new Interval(from.HasValue ? Normalize(from.Value) : null, to.HasValue ? Normalize(to.Value) : null);
        if (window.From == DateTimeOffset.MinValue || window.To == DateTimeOffset.MinValue)
            throw new CourseConfigurationValidationException("Bounds must not use the minimum UTC instant.");
        if (window.IsEmpty) throw new CourseConfigurationValidationException("ValidFromUtc must be earlier than ValidToUtc.");
        return window;
    }

    private static DateTimeOffset Normalize(DateTimeOffset value)
    {
        try
        {
            return TimingAssociationService.Normalize(value);
        }
        catch (TimingAssociationValidationException exception)
        {
            throw new CourseConfigurationValidationException(exception.Message, exception);
        }
    }

    private static CourseConfigChangedV1 Event(DateTimeOffset occurredAtUtc, Guid facilityId, CourseConfigChangeKind kind,
        Interval affected, Guid layoutId) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Normalize(occurredAtUtc), facilityId, kind, affected.From, affected.To, layoutId, []);

    private async Task AcquireWriteLockAsync(CancellationToken cancellationToken)
    {
        if (storage.Database.IsNpgsql())
            await storage.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({WriteLockKey})", cancellationToken);
    }

    private async Task DeferExclusionConstraintsAsync(CancellationToken cancellationToken)
    {
        if (storage.Database.IsNpgsql())
            await storage.Database.ExecuteSqlRawAsync(
                "SET CONSTRAINTS facility.ex_course_layout_revision_interval, facility.ex_facility_layout_assignment_interval DEFERRED",
                cancellationToken);
    }

    private string Protect(RevisionConfirmationState state) =>
        RequireProtector().Protect(JsonSerializer.Serialize(state));

    private bool ConfirmationMatches(string token, RevisionConfirmationState state)
    {
        try
        {
            var actual = RequireProtector().Unprotect(token);
            var expected = JsonSerializer.Serialize(state);
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private IDataProtector RequireProtector() => confirmationProtector ?? throw new InvalidOperationException(
        "A configured ASP.NET Core data-protection provider is required for revision confirmations.");

    private sealed record RevisionConfirmationState(Guid LayoutId, DateTimeOffset? From, DateTimeOffset? To,
        IReadOnlyList<CourseSegmentGeometry> Segments, IReadOnlyList<CourseLayoutRevisionOverlap> Overlaps);
}

public sealed record CourseLayoutRevisionEditResult(Guid? ChangeId, Guid? RevisionId, CourseLayoutRevisionConflictPreview? ConflictPreview);

public sealed record FacilityLayoutAssignmentEditResult(Guid? ChangeId, IReadOnlyList<EffectiveIntervalDTO> Assignments);

public sealed class CourseConfigurationValidationException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>The edit would delete something another part of the system still depends on.</summary>
public sealed class CourseConfigurationConflictException(string message) : Exception(message);

/// <summary>Returns why a course point that no layout uses any more must not be deleted, or null to delete it.</summary>
public delegate Task<string?> PointDeletionCheck(Guid coursePointId, CancellationToken cancellationToken);
