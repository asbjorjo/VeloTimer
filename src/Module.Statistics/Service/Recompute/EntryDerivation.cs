using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;

namespace VeloTime.Module.Statistics.Service.Recompute;

/// <summary>
/// Pure Simple, required-point, course-layout and Multi derivation over the samples of one session
/// (plan §3.3 step 3). These are the rules of the former incremental readers
/// (<c>StatisticsService.ProcessCompletedAsync</c>/<c>ProcessEntryAsync</c>,
/// <c>RequiredCoursePointAttemptReader</c>, <c>CourseLayoutLapAttemptReader</c>), applied to everything
/// the session contains instead of to whatever had arrived. Every completion is re-evaluated, so late
/// evidence now repairs an attempt (plan §5.1). Rejections that used to fail a message are reported
/// instead, so one bad attempt never blocks the rest of the session.
/// Samples are grouped by (transponder, facility); nothing is derived across groups.
/// </summary>
public static class EntryDerivation
{
    /// <summary>Simple entries for one configuration. <paramref name="config"/> must include its item.</summary>
    public static IReadOnlyList<StatisticsEntry> Simple(SimpleStatisticsItemConfig config, StatisticsDurationBounds bounds,
        IEnumerable<Sample> samples, bool hasMultiDependents = false, ICollection<DerivationRejection>? rejections = null)
    {
        var item = config.StatisticsItem ?? throw new ArgumentException("The configuration must include its item.", nameof(config));
        var entries = new Dictionary<Guid, StatisticsEntry>();
        foreach (var group in samples.GroupBy(s => (s.TransponderId, s.FacilityId)))
        {
            var session = group.ToList();
            var completions = session.Where(s => s.CoursePointEndId == config.CoursePointEnd)
                .DistinctBy(s => (s.StartPassingId, s.EndPassingId))
                .OrderBy(s => s.TimeEnd).ThenBy(s => s.EndPassingId);
            foreach (var completion in completions)
            {
                StatisticsEntry? entry = item.DistanceMode switch
                {
                    StatisticsDistanceMode.CourseLayout => CourseLayoutLap(config, bounds, session, completion, hasMultiDependents, rejections),
                    StatisticsDistanceMode.Fixed when config.RequiredCoursePointIds.Length > 0 => RequiredPoints(config, bounds, session, completion),
                    StatisticsDistanceMode.Fixed => Fixed(config, bounds, session, completion),
                    _ => Reject(rejections, "unsupported distance mode", completion, config)
                };
                if (entry is not null) entries.TryAdd(entry.Id, entry);
            }
        }
        return [.. entries.Values.OrderBy(e => e.TimeEnd).ThenBy(e => e.Id)];
    }

    /// <summary>
    /// Fixed-distance point to point: the newest sample leaving the start point that ends at or before the
    /// completion begins (or the completion itself) starts the entry.
    /// </summary>
    private static StatisticsEntry? Fixed(SimpleStatisticsItemConfig config, StatisticsDurationBounds bounds,
        IReadOnlyList<Sample> session, Sample completion)
    {
        var start = session
            .Where(s => s.CoursePointStartId == config.CoursePointStart
                && (s.TimeEnd <= completion.TimeStart || SameEdge(s, completion)))
            .OrderByDescending(s => s.TimeEnd).ThenByDescending(s => s.StartPassingId)
            .FirstOrDefault();
        if (start is null) return null;
        return Create(config, bounds, completion.TransponderId, start.StartPassingId, start.TimeStart,
            completion.EndPassingId, completion.TimeEnd, completion.FacilityId, checkpointsSatisfied: true);
    }

    /// <summary>
    /// Required course points: the newest observed start (departure or arrival) before the completion, then
    /// one continuous ordered attempt through every required point. An unsatisfied attempt is kept, invalid.
    /// </summary>
    private static StatisticsEntry? RequiredPoints(SimpleStatisticsItemConfig config, StatisticsDurationBounds bounds,
        IReadOnlyList<Sample> session, Sample completion)
    {
        if (AttemptStart(config, session, completion) is not { } start) return null;
        var result = OrderedCourseAttempt.Evaluate(Evidence(session, start.Time, completion.TimeEnd).Select(Edge),
            start.Time, completion.TimeEnd, config.CoursePointStart, config.CoursePointEnd, config.RequiredCoursePointIds);
        return Create(config, bounds, completion.TransponderId, start.PassingId, start.Time,
            completion.EndPassingId, completion.TimeEnd, completion.FacilityId, result.RequiredCoursePointsSatisfied);
    }

    /// <summary>
    /// Finish-to-finish lap measured by its layout's closed-loop length. The path must be continuous, on one
    /// layout with one loop length, and its sample distances must add up to exactly one loop.
    /// </summary>
    private static StatisticsEntry? CourseLayoutLap(SimpleStatisticsItemConfig config, StatisticsDurationBounds bounds,
        IReadOnlyList<Sample> session, Sample completion, bool hasMultiDependents, ICollection<DerivationRejection>? rejections)
    {
        if (hasMultiDependents)
            return Reject(rejections, "Multi statistics are not supported for course-layout distance items", completion, config);
        if (config.CoursePointStart != config.CoursePointEnd)
            return Reject(rejections, "course-layout item must use a finish-to-finish configuration", completion, config);
        var geometry = session.Where(s => SameEdge(s, completion))
            .Select(s => (s.CourseLayoutId, s.ClosedLoopDistanceMetres)).Distinct().ToList();
        if (geometry.Count != 1 || !ValidGeometry(geometry[0].CourseLayoutId, geometry[0].ClosedLoopDistanceMetres))
            return Reject(rejections, "completion has missing or conflicting closed-loop geometry evidence", completion, config);
        var layoutId = geometry[0].CourseLayoutId!.Value;
        var loop = geometry[0].ClosedLoopDistanceMetres!.Value;
        if (config.RequiredCourseLayoutId is { } required && required != layoutId)
            return Reject(rejections, "lap evidence conflicts with its configured checkpoint layout", completion, config);
        if (AttemptStart(config, session, completion) is not { } start)
            return Reject(rejections, "lap has no start/finish observation", completion, config);
        if (completion.TimeEnd <= start.Time)
            return Reject(rejections, "course-layout lap duration must be positive", completion, config);

        var evidence = Evidence(session, start.Time, completion.TimeEnd);
        var path = evidence.Where(s => !(s.TimeEnd == start.Time && s.TimeStart < start.Time && s.CoursePointEndId == config.CoursePointStart)
            && !(s.TimeStart == completion.TimeEnd && s.TimeEnd > completion.TimeEnd && s.CoursePointStartId == config.CoursePointEnd)).ToList();
        if (path.Count == 0 || path.Any(s => !ValidGeometry(s.CourseLayoutId, s.ClosedLoopDistanceMetres))
            || path.Any(s => s.CourseLayoutId != layoutId || s.ClosedLoopDistanceMetres != loop))
            return Reject(rejections, "lap path has missing or conflicting layout/length evidence", completion, config);

        var ordered = OrderedCourseAttempt.Evaluate(evidence.Select(Edge), start.Time, completion.TimeEnd,
            config.CoursePointStart, config.CoursePointEnd, config.RequiredCoursePointIds);
        if (!ordered.Continuous)
            return Reject(rejections, "lap path is not continuously ordered", completion, config);

        var distancesByEdge = path.GroupBy(Edge).ToDictionary(g => g.Key, g => g.Select(s => s.Distance).ToArray());
        var total = 0d;
        var compensation = 0d;
        foreach (var edge in ordered.Edges)
        {
            if (!distancesByEdge.TryGetValue(edge, out var distances) || distances.Length == 0
                || distances.Any(d => !ValidDistance(d) || d != distances[0]))
                return Reject(rejections, "lap path has missing or conflicting sample-distance evidence", completion, config);
            // Kahan summation keeps the comparison about source arithmetic, not this accumulator.
            var adjusted = distances[0] - compensation;
            var next = total + adjusted;
            compensation = (next - total) - adjusted;
            total = next;
            if (!double.IsFinite(total))
                return Reject(rejections, "lap sample distances overflowed", completion, config);
        }
        if (!MatchesLoopDistance(total, loop))
            return Reject(rejections, "lap sample distances do not equal exactly one closed-loop distance", completion, config);
        return Create(config, bounds, completion.TransponderId, start.PassingId, start.Time, completion.EndPassingId,
            completion.TimeEnd, completion.FacilityId, ordered.RequiredCoursePointsSatisfied, loop);
    }

    /// <summary>
    /// Multi entries for one Multi configuration over its parent's entries. Each parent entry closes a window
    /// of the newest <see cref="MultiStatisticsItemConfig.Repetitions"/> parent entries ending at or before
    /// it (ordered by end, then ID; invalid parents are never skipped). The window is valid only when exactly
    /// that many parents of the same transponder and facility lie within it and all are valid.
    /// Callers pass one session's entries, so windows never span sessions (decision §6.3).
    /// </summary>
    public static IReadOnlyList<StatisticsEntry> Multi(MultiStatisticsItemConfig config, StatisticsDurationBounds bounds,
        IEnumerable<StatisticsEntry> parentEntries)
    {
        var item = config.StatisticsItem ?? throw new ArgumentException("The configuration must include its item.", nameof(config));
        if (config.Repetitions <= 0) return [];
        var entries = new Dictionary<Guid, StatisticsEntry>();
        foreach (var group in parentEntries.Where(e => e.StatisticsItemConfigId == config.ParentConfigId)
                     .GroupBy(e => (e.TransponderId, e.FacilityId)))
        {
            var newestFirst = group.OrderByDescending(e => e.TimeEnd).ThenByDescending(e => e.Id).ToList();
            for (var i = 0; i + config.Repetitions <= newestFirst.Count; i++)
            {
                var window = newestFirst.GetRange(i, config.Repetitions);
                var last = window[0];
                var first = window[^1];
                var id = DeterministicId.MultiEntry(config.Id, first.Id, last.Id);
                if (entries.ContainsKey(id)) continue;
                var contributors = group.Where(e => e.TimeStart >= first.TimeStart && e.TimeEnd <= last.TimeEnd).ToList();
                var contributorsValid = contributors.Count == config.Repetitions && contributors.All(e => e.IsValid);
                var duration = last.TimeEnd - first.TimeStart;
                var entry = new StatisticsEntry
                {
                    Id = id,
                    TransponderId = group.Key.TransponderId,
                    FacilityId = group.Key.FacilityId,
                    StatisticsItemId = item.Id,
                    StatisticsItemConfigId = config.Id,
                    TimeStart = first.TimeStart,
                    TimeEnd = last.TimeEnd,
                    Duration = duration,
                    Speed = item.Distance / duration.TotalSeconds,
                };
                StatisticsValidityEvaluator.Apply(entry, StatisticsValidityEvaluator.Evaluate(duration, contributorsValid, bounds));
                entries.Add(id, entry);
            }
        }
        return [.. entries.Values.OrderBy(e => e.TimeEnd).ThenBy(e => e.Id)];
    }

    private readonly record struct AttemptStartPoint(DateTime Time, Guid PassingId);

    /// <summary>
    /// The newest observed start before the completion begins: a departure from the start point or an arrival
    /// at it, whichever is later (a departure wins a tie). Never an older start with more convenient checkpoints.
    /// </summary>
    private static AttemptStartPoint? AttemptStart(SimpleStatisticsItemConfig config, IReadOnlyList<Sample> session, Sample completion)
    {
        var departure = session.Where(s => s.CoursePointStartId == config.CoursePointStart && s.TimeStart <= completion.TimeStart)
            .OrderByDescending(s => s.TimeStart).ThenByDescending(s => s.StartPassingId)
            .Select(s => (AttemptStartPoint?)new AttemptStartPoint(s.TimeStart, s.StartPassingId)).FirstOrDefault();
        var arrival = session.Where(s => s.CoursePointEndId == config.CoursePointStart && s.TimeEnd <= completion.TimeStart)
            .OrderByDescending(s => s.TimeEnd).ThenByDescending(s => s.EndPassingId)
            .Select(s => (AttemptStartPoint?)new AttemptStartPoint(s.TimeEnd, s.EndPassingId)).FirstOrDefault();
        if (departure is null) return arrival;
        return arrival is null || departure.Value.Time >= arrival.Value.Time ? departure : arrival;
    }

    /// <summary>Edges touching [start, finish], including spanning or conflicting ones.</summary>
    private static List<Sample> Evidence(IReadOnlyList<Sample> session, DateTime start, DateTime finish) =>
        session.Where(s => (s.TimeStart <= finish && s.TimeEnd >= start)
            || (s.TimeEnd <= finish && s.TimeStart >= start && s.TimeStart <= finish)).ToList();

    private static OrderedCourseAttempt.Edge Edge(Sample s) => new(s.TimeStart, s.TimeEnd, s.CoursePointStartId, s.CoursePointEndId);

    private static bool SameEdge(Sample a, Sample b) => a.TimeStart == b.TimeStart && a.TimeEnd == b.TimeEnd
        && a.CoursePointStartId == b.CoursePointStartId && a.CoursePointEndId == b.CoursePointEndId;

    private static StatisticsEntry Create(SimpleStatisticsItemConfig config, StatisticsDurationBounds bounds, Guid transponderId,
        Guid startPassingId, DateTime start, Guid endPassingId, DateTime end, Guid facilityId, bool checkpointsSatisfied,
        double? distanceMetres = null)
    {
        var duration = end - start;
        var entry = new StatisticsEntry
        {
            Id = DeterministicId.SimpleEntry(config.Id, startPassingId, endPassingId),
            TransponderId = transponderId,
            TimeStart = start,
            TimeEnd = end,
            StatisticsItemId = config.StatisticsItem.Id,
            StatisticsItemConfigId = config.Id,
            Duration = duration,
            Speed = (distanceMetres ?? config.StatisticsItem.Distance) / duration.TotalSeconds,
            DistanceMetres = distanceMetres,
            FacilityId = facilityId,
            RequiredCoursePointsSatisfied = checkpointsSatisfied
        };
        StatisticsValidityEvaluator.Apply(entry, StatisticsValidityEvaluator.Evaluate(duration,
            contributorsValid: true, bounds, checkpointsSatisfied));
        return entry;
    }

    private static StatisticsEntry? Reject(ICollection<DerivationRejection>? rejections, string reason, Sample completion,
        SimpleStatisticsItemConfig config)
    {
        rejections?.Add(new(reason, completion.TimeEnd, config.Id));
        return null;
    }

    private static bool ValidGeometry(Guid? layoutId, double? distance) =>
        layoutId is { } id && id != Guid.Empty && distance is { } d && double.IsFinite(d) && d > 0;

    private static bool ValidDistance(double distance) => double.IsFinite(distance) && distance > 0;

    // A 1e-12 relative tolerance only absorbs floating-point summation differences between endpoint
    // distances and loop geometry.
    private static bool MatchesLoopDistance(double actual, double expected) =>
        ValidDistance(actual) && ValidDistance(expected) && Math.Abs(actual - expected) <= Math.Max(actual, expected) * 1e-12;
}
