namespace VeloTime.Module.Statistics.Service;

/// <summary>Shared ordered, continuous edge semantics for configured course attempts.</summary>
internal static class OrderedCourseAttempt
{
    internal sealed record Edge(DateTime Start, DateTime End, Guid From, Guid To);
    internal readonly record struct Result(bool Continuous, bool RequiredCoursePointsSatisfied, IReadOnlyList<Edge> Edges);

    internal static Result Evaluate(IEnumerable<Edge> evidence, DateTime timeStart, DateTime finish,
        Guid startPoint, Guid finishPoint, IReadOnlyList<Guid> requiredCoursePointIds,
        CancellationToken cancellationToken = default)
    {
        var edges = evidence.Distinct()
            .Where(s => !(s.End == timeStart && s.Start < timeStart && s.To == startPoint)
                && !(s.Start == finish && s.End > finish && s.From == finishPoint))
            .OrderBy(s => s.Start).ThenBy(s => s.End).ToList();
        var continuous = timeStart < finish && edges.Count > 0;
        var instant = timeStart;
        var point = startPoint;
        var requiredIndex = 0;
        foreach (var edge in edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (edge.Start != instant || edge.From != point || edge.End <= edge.Start || edge.End > finish)
            {
                continuous = false;
                break;
            }
            instant = edge.End;
            point = edge.To;
            if (instant == finish) continue;
            if (point == startPoint || point == finishPoint)
            {
                continuous = false;
                break;
            }
            if (requiredCoursePointIds.Contains(point))
            {
                if (requiredIndex >= requiredCoursePointIds.Count
                    || point != requiredCoursePointIds[requiredIndex++])
                {
                    continuous = false;
                    break;
                }
            }
        }
        continuous &= instant == finish && point == finishPoint;
        return new(continuous, continuous && requiredIndex == requiredCoursePointIds.Count, edges);
    }
}
