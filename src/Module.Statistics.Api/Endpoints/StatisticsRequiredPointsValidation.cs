using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.Module.Statistics.Api.Endpoints;

internal static class StatisticsRequiredPointsValidation
{
    // Never sort the requested snapshot: validate it against the layout's forward path.
    internal static string? ValidateShape(StatisticsRequiredCoursePoints requirements, Guid start, Guid end)
    {
        if (requirements.CoursePointIds is not { } ids) return "Required coursepoint IDs must be an array; use an empty array to clear requirements.";
        if (ids.Count == 0) return requirements.CourseLayoutId is null ? null : "Empty requirements must have a null layout ID.";
        if (requirements.CourseLayoutId.GetValueOrDefault() == Guid.Empty) return "Select a layout for required coursepoints.";
        if (ids.Any(id => id == Guid.Empty || id == start || id == end) || ids.Distinct().Count() != ids.Count)
            return "Required coursepoints must be unique, nonempty and distinct from both endpoints.";
        return null;
    }

    internal static string? ValidatePath(CourseLayoutDTO layout, Guid facilityId, Guid start, Guid end, IReadOnlyList<Guid> required)
    {
        if (layout.FacilityId != facilityId) return "The required layout must belong to the endpoints' facility.";
        var segments = layout.Segments.OrderBy(segment => segment.Order).ToArray();
        if (segments.Length == 0 || segments.Select(segment => segment.Order).Distinct().Count() != segments.Length ||
            segments.Any(segment => segment.Start is null || segment.End is null || segment.Start.Id == Guid.Empty || segment.End.Id == Guid.Empty))
            return "The selected layout has invalid or ambiguous ordered segments.";
        if (segments.Zip(segments.Skip(1)).Any(pair => pair.First.End.Id != pair.Second.Start.Id))
            return "The selected layout must form one continuous forward course.";
        var path = segments.Select(segment => segment.Start.Id).Append(segments[^1].End.Id).ToList();
        var closed = path[0] == path[^1];
        if (closed) path.RemoveAt(path.Count - 1);
        if (path.Distinct().Count() != path.Count) return "The selected layout repeats a coursepoint and has an ambiguous path.";
        var startIndex = path.IndexOf(start);
        var endIndex = path.IndexOf(end);
        if (startIndex < 0 || endIndex < 0 || (!closed && startIndex >= endIndex))
            return "Both endpoints must lie on a forward path in the selected layout.";
        var distance = closed ? (endIndex - startIndex + path.Count) % path.Count : endIndex - startIndex;
        if (closed && distance == 0) distance = path.Count;
        var intermediates = Enumerable.Range(1, distance - 1).Select(offset => path[(startIndex + offset) % path.Count]).ToList();
        var previous = -1;
        foreach (var id in required)
        {
            var index = intermediates.IndexOf(id);
            if (index <= previous) return "Required coursepoints must be intermediate points in strictly increasing forward layout order.";
            previous = index;
        }
        return null;
    }

    // Return every point in the validated forward itinerary. Optional observations can create
    // sample edges across non-adjacent points, so they all participate in resolver preflight.
    internal static IReadOnlyList<Guid> GetItineraryPoints(CourseLayoutDTO layout, Guid start, Guid end)
    {
        var segments = layout.Segments.OrderBy(segment => segment.Order).ToArray();
        var path = segments.Select(segment => segment.Start.Id).Append(segments[^1].End.Id).ToArray();
        var closed = path[0] == path[^1];
        if (closed) path = path[..^1];
        var startIndex = Array.IndexOf(path, start);
        var endIndex = Array.IndexOf(path, end);
        var count = closed
            ? (endIndex - startIndex + path.Length) % path.Length
            : endIndex - startIndex;
        if (closed && count == 0) count = path.Length;
        return Enumerable.Range(0, count + 1)
            .Select(offset => path[(startIndex + offset) % path.Length])
            .Distinct()
            .ToArray();
    }
}
