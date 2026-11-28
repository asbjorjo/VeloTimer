using VeloTime.Module.Statistics.Model;

namespace VeloTime.Module.Statistics.Service.Recompute;

/// <summary>A passing fact with its facility and course point resolved at its own time.</summary>
public sealed record ResolvedPassing(Guid PassingId, Guid TransponderId, Guid TimingPointId, DateTime Time,
    Guid FacilityId, Guid CoursePointId);

/// <summary>One transponder's maximal run of passings at one facility with no gap of SessionGap or more.</summary>
public sealed record RidingSession(Guid TransponderId, Guid FacilityId, IReadOnlyList<ResolvedPassing> Passings)
{
    public DateTime Start => Passings[0].Time;
    public DateTime End => Passings[^1].Time;
}

/// <summary>Why a fact or a derived candidate produced no row; counted, never fatal (plan §5.3).</summary>
public sealed record DerivationRejection(string Reason, DateTime Time, Guid? ConfigId = null);

/// <summary>Pure session and sample construction over facts (plan §3.2, §5.2).</summary>
public static class SessionBuilder
{
    public static readonly TimeSpan DefaultSessionGap = TimeSpan.FromMinutes(30);

    /// <summary>The deterministic processing order: time, then passing ID.</summary>
    public static IOrderedEnumerable<T> InEventOrder<T>(IEnumerable<T> items, Func<T, DateTime> time, Func<T, Guid> id) =>
        items.OrderBy(time).ThenBy(id);

    /// <summary>
    /// Splits resolved passings into sessions per (transponder, facility). Passings at the same instant
    /// on the same timing point count as one (the lowest ID wins). Consecutive passings at least
    /// <paramref name="gap"/> apart start a new session.
    /// </summary>
    public static IReadOnlyList<RidingSession> Sessions(IEnumerable<ResolvedPassing> passings, TimeSpan gap)
    {
        if (gap <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(gap));
        var sessions = new List<RidingSession>();
        foreach (var stream in passings.GroupBy(p => (p.TransponderId, p.FacilityId)).OrderBy(g => g.Key.TransponderId).ThenBy(g => g.Key.FacilityId))
        {
            var current = new List<ResolvedPassing>();
            foreach (var passing in InEventOrder(stream, p => p.Time, p => p.PassingId))
            {
                if (current.Count > 0)
                {
                    var previous = current[^1];
                    if (previous.Time == passing.Time && previous.TimingPointId == passing.TimingPointId) continue;
                    if (passing.Time - previous.Time >= gap)
                    {
                        sessions.Add(new(stream.Key.TransponderId, stream.Key.FacilityId, current));
                        current = [];
                    }
                }
                current.Add(passing);
            }
            if (current.Count > 0) sessions.Add(new(stream.Key.TransponderId, stream.Key.FacilityId, current));
        }
        return sessions;
    }

    /// <summary>
    /// One sample per pair of consecutive passings. A zero-duration pair (equal instants at different
    /// loops) and a pair whose distance the layout cannot resolve produce no sample.
    /// </summary>
    public static IReadOnlyList<Sample> Samples(RidingSession session, FacilityCourseHistory course,
        ICollection<DerivationRejection>? rejections = null)
    {
        var samples = new List<Sample>();
        for (var i = 1; i < session.Passings.Count; i++)
        {
            var start = session.Passings[i - 1];
            var end = session.Passings[i];
            var duration = end.Time - start.Time;
            if (duration <= TimeSpan.Zero)
            {
                rejections?.Add(new("zero-duration sample", end.Time));
                continue;
            }
            // Each passing's course point was resolved at its own time; the layout geometry is the one in
            // effect when the rider entered the segment.
            var distance = course.ResolveDistance(start.CoursePointId, end.CoursePointId, start.Time);
            if (distance is null)
            {
                rejections?.Add(new("no compatible course layout", end.Time));
                continue;
            }
            samples.Add(new Sample
            {
                Id = DeterministicId.Sample(start.PassingId, end.PassingId),
                StartPassingId = start.PassingId,
                EndPassingId = end.PassingId,
                TransponderId = session.TransponderId,
                FacilityId = session.FacilityId,
                CoursePointStartId = start.CoursePointId,
                CoursePointEndId = end.CoursePointId,
                TimeStart = start.Time,
                TimeEnd = end.Time,
                Duration = duration,
                Distance = distance.Distance,
                Speed = distance.Distance / duration.TotalSeconds,
                CourseLayoutId = distance.CourseLayoutId,
                ClosedLoopDistanceMetres = distance.ClosedLoopDistanceMetres
            });
        }
        return samples;
    }
}
