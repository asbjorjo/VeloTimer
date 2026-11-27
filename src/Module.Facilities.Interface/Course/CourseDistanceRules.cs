namespace VeloTime.Module.Facilities.Interface.Course;

/// <summary>One directed course segment of a layout's geometry. Length is in metres.</summary>
public readonly record struct CourseSegmentGeometry(int Order, Guid StartPointId, Guid EndPointId, double Length);

/// <summary>The geometry a layout has at one instant: the segments of the revision that is in effect then.</summary>
public sealed record CourseLayoutGeometry(Guid LayoutId, IReadOnlyList<CourseSegmentGeometry> Segments);

/// <summary>
/// Endpoint distance in metres, plus full closed-loop evidence. <see cref="CourseLayoutId"/> and
/// <see cref="ClosedLoopDistanceMetres"/> are set together, and only when the layout proves one finite,
/// positive closed loop.
/// </summary>
public sealed record CourseDistanceResult(double Distance, Guid? CourseLayoutId, double? ClosedLoopDistanceMetres);

public enum CourseDistanceFailure
{
    None,
    /// <summary>No layout in effect contains both points, or more than one does.</summary>
    NoUniqueLayout,
    /// <summary>The layout's segments do not form one continuous path in order.</summary>
    Discontinuous
}

/// <summary>
/// The distance rules shared by Facilities and Statistics (docs/replay-safe-statistics-plan.md §4.3), so the
/// rules live in one place. Callers pass only the layouts in use at the instant being resolved, each with the
/// geometry of its revision at that instant.
///
/// <list type="bullet">
/// <item>Exactly one layout must contain both points, and its segments must form one continuous path.</item>
/// <item>Closed courses are directed: travel continues forward through the last segment and wraps.</item>
/// <item>Open courses are symmetric: the linear distance between the points either way.</item>
/// <item>Equal points mean the whole course (one lap on a closed course).</item>
/// </list>
/// </summary>
public static class CourseDistanceRules
{
    public static CourseDistanceResult? Resolve(IEnumerable<CourseLayoutGeometry> layouts, Guid start, Guid end) =>
        Resolve(layouts, start, end, out _);

    public static CourseDistanceResult? Resolve(IEnumerable<CourseLayoutGeometry> layouts, Guid start, Guid end,
        out CourseDistanceFailure failure)
    {
        CourseLayoutGeometry? layout = null;
        foreach (var candidate in layouts)
        {
            if (!Touches(candidate, start) || !Touches(candidate, end)) continue;
            if (layout is not null)
            {
                failure = CourseDistanceFailure.NoUniqueLayout;
                return null;
            }
            layout = candidate;
        }
        if (layout is null)
        {
            failure = CourseDistanceFailure.NoUniqueLayout;
            return null;
        }

        var segments = layout.Segments.OrderBy(segment => segment.Order).ToList();
        if (segments.Count == 0 || segments.Zip(segments.Skip(1)).Any(pair => pair.First.EndPointId != pair.Second.StartPointId))
        {
            failure = CourseDistanceFailure.Discontinuous;
            return null;
        }
        var points = segments.Take(1).Select(segment => segment.StartPointId)
            .Concat(segments.Select(segment => segment.EndPointId)).ToList();
        var startIndex = points.IndexOf(start);
        var endIndex = points.IndexOf(end);
        if (startIndex < 0 || endIndex < 0)
        {
            failure = CourseDistanceFailure.Discontinuous;
            return null;
        }
        var closed = segments[^1].EndPointId == segments[0].StartPointId;
        double distance;
        if (startIndex == endIndex)
            distance = segments.Sum(segment => segment.Length);
        else if (closed)
        {
            var count = (endIndex - startIndex + segments.Count) % segments.Count;
            distance = segments.Skip(startIndex).Concat(segments.Take(startIndex)).Take(count).Sum(segment => segment.Length);
        }
        else
        {
            distance = segments.Skip(Math.Min(startIndex, endIndex)).Take(Math.Abs(endIndex - startIndex))
                .Sum(segment => segment.Length);
        }

        var loop = ClosedLoopDistance(segments, points, closed);
        if (layout.LayoutId == Guid.Empty) loop = null;
        failure = CourseDistanceFailure.None;
        return new CourseDistanceResult(distance, loop.HasValue ? layout.LayoutId : null, loop);
    }

    private static bool Touches(CourseLayoutGeometry layout, Guid point) =>
        layout.Segments.Any(segment => segment.StartPointId == point || segment.EndPointId == point);

    private static double? ClosedLoopDistance(IReadOnlyList<CourseSegmentGeometry> segments, IReadOnlyList<Guid> points, bool closed)
    {
        if (!closed || segments.Any(segment => !double.IsFinite(segment.Length) || segment.Length <= 0) ||
            segments.Select(segment => segment.Order).Distinct().Count() != segments.Count ||
            points.Take(points.Count - 1).Distinct().Count() != segments.Count)
            return null;
        var distance = segments.Sum(segment => segment.Length);
        return double.IsFinite(distance) && distance > 0 ? distance : null;
    }
}

/// <summary>Half-open UTC intervals [from, to) where null is unbounded, the convention for all dated config.</summary>
public static class EffectiveInterval
{
    public static bool Contains(DateTimeOffset? from, DateTimeOffset? to, DateTimeOffset instant) =>
        (from is not { } lower || lower <= instant) && (to is not { } upper || instant < upper);

    public static bool Contains(DateTime? from, DateTime? to, DateTime instant) =>
        (from is not { } lower || lower <= instant) && (to is not { } upper || instant < upper);

    public static bool Overlaps(DateTimeOffset? leftFrom, DateTimeOffset? leftTo, DateTimeOffset? rightFrom, DateTimeOffset? rightTo) =>
        (leftTo is not { } lt || rightFrom is not { } rf || rf < lt) && (rightTo is not { } rt || leftFrom is not { } lf || lf < rt);
}
