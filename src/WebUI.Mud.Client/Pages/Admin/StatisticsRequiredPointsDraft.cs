using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.WebUI.Mud.Client.Pages.Admin;

/// <summary>Presentation-only selection. Never reinterpret or mutate the saved ordered snapshot.</summary>
public sealed class StatisticsRequiredPointsDraft(StatisticsRequiredCoursePoints? saved = null)
{
    private readonly Guid? _savedLayout = saved?.CourseLayoutId;
    private readonly Guid[] _savedPoints = saved?.CoursePointIds?.ToArray() ?? [];
    private Guid[] _selected = saved?.CoursePointIds?.ToArray() ?? [];
    public IReadOnlyList<Guid> Selected => _selected;

    public void Clear() => _selected = [];

    public void Toggle(Guid id, IReadOnlyList<CoursePointView> path)
    {
        if (!path.Any(point => point.Id == id)) return;
        var selected = _selected.ToHashSet();
        if (!selected.Remove(id)) selected.Add(id);
        // Retain unavailable selections until explicitly cleared; never silently drop saved requirements.
        _selected = path.Where(point => selected.Contains(point.Id)).Select(point => point.Id)
            .Concat(_selected.Where(point => !path.Any(candidate => candidate.Id == point))).ToArray();
    }

    // Null means preserve, including threshold-only edits while reference loading is unavailable.
    public StatisticsRequiredCoursePoints? Replacement(Guid? layoutId)
    {
        var effectiveLayout = _selected.Length == 0 ? null : layoutId;
        return effectiveLayout == _savedLayout && _selected.SequenceEqual(_savedPoints)
            ? null : new(effectiveLayout, _selected.ToArray());
    }

    public bool MatchesPath(Guid? layoutId, IReadOnlyList<CoursePointView>? path) =>
        _selected.Length == 0 || (layoutId.HasValue && path is not null &&
            path.Where(point => _selected.Contains(point.Id)).Select(point => point.Id).SequenceEqual(_selected));

    /// <summary>Mirror the shared contract at the presentation boundary; API remains authoritative.</summary>
    public static IReadOnlyList<CoursePointView>? IntermediatePath(CourseLayoutDetailView? layout, Guid? start, Guid? end)
    {
        if (layout is null || !start.HasValue || !end.HasValue) return null;
        var segments = layout.Segments.OrderBy(segment => segment.Order).ToArray();
        if (segments.Length == 0 || segments.Select(segment => segment.Order).Distinct().Count() != segments.Length ||
            segments.Any(segment => segment.StartPoint.Id == Guid.Empty || segment.EndPoint.Id == Guid.Empty) ||
            segments.Zip(segments.Skip(1)).Any(pair => pair.First.EndPoint.Id != pair.Second.StartPoint.Id)) return null;
        var points = segments.Select(segment => segment.StartPoint).Append(segments[^1].EndPoint).ToList();
        var closed = points[0].Id == points[^1].Id;
        if (closed) points.RemoveAt(points.Count - 1);
        if (points.Select(point => point.Id).Distinct().Count() != points.Count) return null;
        var first = points.FindIndex(point => point.Id == start);
        var last = points.FindIndex(point => point.Id == end);
        if (first < 0 || last < 0 || (!closed && first >= last)) return null;
        var distance = closed ? (last - first + points.Count) % points.Count : last - first;
        if (closed && distance == 0) distance = points.Count;
        return Enumerable.Range(1, distance - 1).Select(offset => points[(first + offset) % points.Count]).ToArray();
    }
}
