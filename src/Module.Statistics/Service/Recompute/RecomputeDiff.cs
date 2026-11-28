using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;

namespace VeloTime.Module.Statistics.Service.Recompute;

/// <summary>
/// Manual decisions and diff-apply for one recompute (plan §3.3 step 4, §5.4). Pure: it only changes the
/// given objects and returns what to insert and delete.
/// </summary>
public static class RecomputeDiff
{
    /// <summary>
    /// Keeps manual invalidations on derived entries whose identity and measured values are unchanged.
    /// When the values changed the decision is lifted and a <see cref="ManualDecisionReviewOutcome.Changed"/>
    /// review is returned. Call before deriving Multi entries, so their contributor validity sees the decisions.
    /// </summary>
    public static IReadOnlyList<ManualDecisionReview> CarryManualDecisions(IReadOnlyDictionary<Guid, StatisticsEntry> existing,
        IEnumerable<StatisticsEntry> derived, DateTime now)
    {
        var reviews = new List<ManualDecisionReview>();
        foreach (var entry in derived)
        {
            if (!existing.TryGetValue(entry.Id, out var stored) || !IsManual(stored)) continue;
            if (SameMeasurement(stored, entry))
            {
                entry.IsValid = false;
                entry.InvalidReason = StatisticsEntryInvalidReason.ManuallyInvalidated;
                entry.ManualInvalidationChangedByUserId = stored.ManualInvalidationChangedByUserId;
                entry.ManualInvalidationChangedAtUtc = stored.ManualInvalidationChangedAtUtc;
                entry.ManualInvalidationNote = stored.ManualInvalidationNote;
            }
            else reviews.Add(Review(stored, ManualDecisionReviewOutcome.Changed, now));
        }
        return reviews;
    }

    /// <summary>Reviews for manual decisions on stored entries the recompute no longer derives.</summary>
    public static IReadOnlyList<ManualDecisionReview> RemovedManualDecisions(IEnumerable<StatisticsEntry> existing,
        IReadOnlySet<Guid> derivedIds, DateTime now) =>
        existing.Where(e => IsManual(e) && !derivedIds.Contains(e.Id))
            .Select(e => Review(e, ManualDecisionReviewOutcome.Removed, now)).ToList();

    public sealed record Plan<T>(IReadOnlyList<T> Inserts, IReadOnlyList<T> Deletes, int Updated)
    {
        public int Changed => Inserts.Count + Deletes.Count + Updated;
    }

    /// <summary>Copies changed values onto tracked stored samples; new samples are inserted, missing ones deleted.</summary>
    public static Plan<Sample> Samples(IEnumerable<Sample> stored, IEnumerable<Sample> derived)
    {
        var byId = stored.ToDictionary(s => s.Id);
        var inserts = new List<Sample>();
        var updated = 0;
        var seen = new HashSet<Guid>();
        foreach (var sample in derived)
        {
            if (!seen.Add(sample.Id)) continue;
            if (!byId.TryGetValue(sample.Id, out var current)) { inserts.Add(sample); continue; }
            if (Copy(sample, current)) updated++;
        }
        return new(inserts, byId.Values.Where(s => !seen.Contains(s.Id)).ToList(), updated);
    }

    /// <summary>Copies changed values onto tracked stored entries; new entries are inserted, missing ones deleted.</summary>
    public static Plan<StatisticsEntry> Entries(IEnumerable<StatisticsEntry> stored, IEnumerable<StatisticsEntry> derived)
    {
        var byId = stored.ToDictionary(e => e.Id);
        var inserts = new List<StatisticsEntry>();
        var updated = 0;
        var seen = new HashSet<Guid>();
        foreach (var entry in derived)
        {
            if (!seen.Add(entry.Id)) continue;
            if (!byId.TryGetValue(entry.Id, out var current)) { inserts.Add(entry); continue; }
            if (Copy(entry, current)) updated++;
        }
        return new(inserts, byId.Values.Where(e => !seen.Contains(e.Id)).ToList(), updated);
    }

    private static bool IsManual(StatisticsEntry entry) => entry.InvalidReason == StatisticsEntryInvalidReason.ManuallyInvalidated;

    private static bool SameMeasurement(StatisticsEntry a, StatisticsEntry b) =>
        a.StatisticsItemConfigId == b.StatisticsItemConfigId && a.TransponderId == b.TransponderId
        && a.FacilityId == b.FacilityId && a.TimeStart == b.TimeStart && a.TimeEnd == b.TimeEnd
        && a.Duration == b.Duration && a.DistanceMetres == b.DistanceMetres && a.Speed.Equals(b.Speed);

    private static ManualDecisionReview Review(StatisticsEntry entry, ManualDecisionReviewOutcome outcome, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        EntryId = entry.Id,
        Outcome = outcome,
        TransponderId = entry.TransponderId,
        StatisticsItemId = entry.StatisticsItemId,
        StatisticsItemConfigId = entry.StatisticsItemConfigId,
        FacilityId = entry.FacilityId,
        TimeStart = entry.TimeStart,
        TimeEnd = entry.TimeEnd,
        Duration = entry.Duration,
        DistanceMetres = entry.DistanceMetres,
        ChangedByUserId = entry.ManualInvalidationChangedByUserId,
        ChangedAtUtc = entry.ManualInvalidationChangedAtUtc,
        Note = entry.ManualInvalidationNote,
        CreatedAtUtc = now
    };

    private static bool Copy(Sample from, Sample to)
    {
        if (from.TransponderId != to.TransponderId || from.StartPassingId != to.StartPassingId || from.EndPassingId != to.EndPassingId)
            throw new InvalidOperationException($"Sample {to.Id} changed its identity.");
        var changed = false;
        Set(to.TimeStart, from.TimeStart, v => to.TimeStart = v);
        Set(to.TimeEnd, from.TimeEnd, v => to.TimeEnd = v);
        Set(to.CoursePointStartId, from.CoursePointStartId, v => to.CoursePointStartId = v);
        Set(to.CoursePointEndId, from.CoursePointEndId, v => to.CoursePointEndId = v);
        Set(to.Duration, from.Duration, v => to.Duration = v);
        Set(to.Distance, from.Distance, v => to.Distance = v);
        Set(to.CourseLayoutId, from.CourseLayoutId, v => to.CourseLayoutId = v);
        Set(to.ClosedLoopDistanceMetres, from.ClosedLoopDistanceMetres, v => to.ClosedLoopDistanceMetres = v);
        Set(to.Speed, from.Speed, v => to.Speed = v);
        Set(to.FacilityId, from.FacilityId, v => to.FacilityId = v);
        return changed;

        void Set<T>(T current, T value, Action<T> assign)
        {
            if (EqualityComparer<T>.Default.Equals(current, value)) return;
            assign(value);
            changed = true;
        }
    }

    private static bool Copy(StatisticsEntry from, StatisticsEntry to)
    {
        if (from.TransponderId != to.TransponderId || from.StatisticsItemConfigId != to.StatisticsItemConfigId
            || from.StatisticsItemId != to.StatisticsItemId)
            throw new InvalidOperationException($"Statistics entry {to.Id} changed its identity.");
        var changed = false;
        Set(to.TimeStart, from.TimeStart, v => to.TimeStart = v);
        Set(to.TimeEnd, from.TimeEnd, v => to.TimeEnd = v);
        Set(to.Duration, from.Duration, v => to.Duration = v);
        Set(to.Speed, from.Speed, v => to.Speed = v);
        Set(to.DistanceMetres, from.DistanceMetres, v => to.DistanceMetres = v);
        Set(to.FacilityId, from.FacilityId, v => to.FacilityId = v);
        Set(to.RequiredCoursePointsSatisfied, from.RequiredCoursePointsSatisfied, v => to.RequiredCoursePointsSatisfied = v);
        Set(to.IsValid, from.IsValid, v => to.IsValid = v);
        Set(to.InvalidReason, from.InvalidReason, v => to.InvalidReason = v);
        Set(to.ManualInvalidationChangedByUserId, from.ManualInvalidationChangedByUserId, v => to.ManualInvalidationChangedByUserId = v);
        Set(to.ManualInvalidationChangedAtUtc, from.ManualInvalidationChangedAtUtc, v => to.ManualInvalidationChangedAtUtc = v);
        Set(to.ManualInvalidationNote, from.ManualInvalidationNote, v => to.ManualInvalidationNote = v);
        Set(to.StatsProfileUserId, from.StatsProfileUserId, v => to.StatsProfileUserId = v);
        Set(to.StatisticsConfigVersionId, from.StatisticsConfigVersionId, v => to.StatisticsConfigVersionId = v);
        return changed;

        void Set<T>(T current, T value, Action<T> assign)
        {
            if (EqualityComparer<T>.Default.Equals(current, value)) return;
            assign(value);
            changed = true;
        }
    }
}
