namespace VeloTime.Module.Statistics.Service.Recompute;

/// <summary>
/// Distance between course points over the given layouts. The rules match
/// <c>FacilitiesService.CalculateCoursePointDistance</c>: exactly one layout must contain both points;
/// it must be one continuous path; closed courses are directed and wrap forward; open courses are
/// symmetric; equal points mean one whole lap. Closed-loop evidence is returned only for a valid loop.
/// Phase 3 moves this into a library shared with Facilities.
/// </summary>
public static class CourseGeometry
{
    public static CourseDistance? Resolve(IReadOnlyList<CourseLayoutVersion> layouts, Guid start, Guid end)
    {
        CourseLayoutVersion? layout = null;
        foreach (var candidate in layouts)
        {
            if (!Touches(candidate, start) || !Touches(candidate, end)) continue;
            if (layout is not null) return null; // Ambiguous: more than one compatible layout.
            layout = candidate;
        }
        if (layout is null) return null;

        var segments = layout.Segments.OrderBy(segment => segment.Order).ToList();
        if (segments.Count == 0 || segments.Zip(segments.Skip(1)).Any(pair => pair.First.EndPointId != pair.Second.StartPointId))
            return null;
        var points = segments.Take(1).Select(segment => segment.StartPointId)
            .Concat(segments.Select(segment => segment.EndPointId)).ToList();
        var startIndex = points.IndexOf(start);
        var endIndex = points.IndexOf(end);
        if (startIndex < 0 || endIndex < 0) return null;
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
        return new CourseDistance(distance, loop.HasValue ? layout.LayoutId : null, loop);
    }

    private static bool Touches(CourseLayoutVersion layout, Guid point) =>
        layout.Segments.Any(segment => segment.StartPointId == point || segment.EndPointId == point);

    private static double? ClosedLoopDistance(IReadOnlyList<CourseSegmentVersion> segments, IReadOnlyList<Guid> points, bool closed)
    {
        if (!closed || segments.Any(segment => !double.IsFinite(segment.Length) || segment.Length <= 0) ||
            segments.Select(segment => segment.Order).Distinct().Count() != segments.Count ||
            points.Take(points.Count - 1).Distinct().Count() != segments.Count)
            return null;
        var distance = segments.Sum(segment => segment.Length);
        return double.IsFinite(distance) && distance > 0 ? distance : null;
    }
}
