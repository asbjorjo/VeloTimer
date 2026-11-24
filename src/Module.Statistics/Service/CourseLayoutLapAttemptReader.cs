using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service;

/// <summary>Resolves one finish-to-finish lap from persisted, same-layout Facilities evidence.</summary>
internal sealed class CourseLayoutLapAttemptReader(StatisticsDbContext storage)
{
    internal sealed record Attempt(DateTime TimeStart, Guid FacilityId, double DistanceMetres,
        bool RequiredCoursePointsSatisfied);

    public async Task<Attempt> ReadAsync(SimpleStatisticsItemConfig config, Sample completion,
        CancellationToken cancellationToken)
    {
        // Facility and geometry belong to the validated persisted source, not all rows sharing
        // its completion tuple. Same-facility conflicting path evidence still fails closed below.
        var segmentStart = completion.TimeStart;
        var finish = completion.TimeEnd;
        var facility = completion.FacilityId;
        if (!ValidGeometry(completion.CourseLayoutId, completion.ClosedLoopDistanceMetres))
            throw new InvalidOperationException("Course-layout lap completion has missing or conflicting closed-loop geometry evidence.");
        var layoutId = completion.CourseLayoutId!.Value;
        var distanceMetres = completion.ClosedLoopDistanceMetres!.Value;
        if (config.RequiredCourseLayoutId.HasValue && config.RequiredCourseLayoutId.Value != layoutId)
            throw new InvalidOperationException("Course-layout lap evidence conflicts with its configured checkpoint layout.");

        var samples = storage.Set<Sample>().AsNoTracking()
            .Where(s => s.TransponderId == completion.TransponderId && s.FacilityId == facility);
        // Match the checkpoint reader: newest departure OR arrival is the attempt start.
        var departure = await samples.Where(s => s.CoursePointStartId == config.CoursePointStart
                && s.TimeStart <= segmentStart)
            .OrderByDescending(s => s.TimeStart).Select(s => (DateTime?)s.TimeStart).FirstOrDefaultAsync(cancellationToken);
        var arrival = await samples.Where(s => s.CoursePointEndId == config.CoursePointStart
                && s.TimeEnd <= segmentStart)
            .OrderByDescending(s => s.TimeEnd).Select(s => (DateTime?)s.TimeEnd).FirstOrDefaultAsync(cancellationToken);
        var start = departure is null ? arrival : arrival is null || departure >= arrival ? departure : arrival;
        if (start is null)
            throw new InvalidOperationException("Course-layout lap has no persisted start/finish observation.");
        var timeStart = start.Value;

        var evidence = await samples.Where(s => (s.TimeStart <= finish && s.TimeEnd >= timeStart)
                || (s.TimeEnd <= finish && s.TimeStart >= timeStart && s.TimeStart <= finish))
            .Select(s => new
            {
                s.TimeStart, s.TimeEnd, s.CoursePointStartId, s.CoursePointEndId,
                s.CourseLayoutId, s.ClosedLoopDistanceMetres, s.Distance
            }).ToListAsync(cancellationToken);
        var pathSamples = evidence.Where(s => !(s.TimeEnd == timeStart && s.TimeStart < timeStart
                    && s.CoursePointEndId == config.CoursePointStart)
                && !(s.TimeStart == finish && s.TimeEnd > finish
                    && s.CoursePointStartId == config.CoursePointEnd)).ToList();
        if (pathSamples.Count == 0 || pathSamples.Any(s => !ValidGeometry(s.CourseLayoutId, s.ClosedLoopDistanceMetres))
            || pathSamples.Any(s => s.CourseLayoutId != layoutId || s.ClosedLoopDistanceMetres != distanceMetres))
            throw new InvalidOperationException("Course-layout lap path has missing or conflicting layout/length evidence.");

        var path = OrderedCourseAttempt.Evaluate(evidence.Select(s => new OrderedCourseAttempt.Edge(
                s.TimeStart, s.TimeEnd, s.CoursePointStartId, s.CoursePointEndId)), timeStart, finish,
            config.CoursePointStart, config.CoursePointEnd, config.RequiredCoursePointIds, cancellationToken);
        if (!path.Continuous)
            throw new InvalidOperationException("Course-layout lap path is stale, conflicting, or not continuously ordered.");

        // The shared evaluator has already removed equivalent duplicate edges and established their
        // exact order. Require duplicate rows to agree on their measured distance, then sum each
        // accepted edge once; otherwise a path can traverse the loop multiple times undetected.
        var distancesByEdge = pathSamples.GroupBy(sample => new OrderedCourseAttempt.Edge(sample.TimeStart,
                sample.TimeEnd, sample.CoursePointStartId, sample.CoursePointEndId))
            .ToDictionary(group => group.Key, group => group.Select(sample => sample.Distance).ToArray());
        var totalDistance = 0d;
        var compensation = 0d;
        foreach (var edge in path.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!distancesByEdge.TryGetValue(edge, out var duplicateDistances) || duplicateDistances.Length == 0
                || duplicateDistances.Any(distance => !ValidDistance(distance) || distance != duplicateDistances[0]))
                throw new InvalidOperationException("Course-layout lap path has missing or conflicting sample-distance evidence.");

            // Kahan summation keeps the comparison focused on source arithmetic, not this accumulator.
            var adjusted = duplicateDistances[0] - compensation;
            var nextTotal = totalDistance + adjusted;
            compensation = (nextTotal - totalDistance) - adjusted;
            totalDistance = nextTotal;
            if (!double.IsFinite(totalDistance))
                throw new InvalidOperationException("Course-layout lap sample distances overflowed their finite range.");
        }
        if (!MatchesLoopDistance(totalDistance, distanceMetres))
            throw new InvalidOperationException("Course-layout lap sample distances do not equal exactly one closed-loop distance.");
        return new(timeStart, facility, distanceMetres, path.RequiredCoursePointsSatisfied);
    }

    private static bool ValidGeometry(Guid? layoutId, double? distance) =>
        layoutId.HasValue && layoutId.Value != Guid.Empty && distance.HasValue
        && double.IsFinite(distance.Value) && distance.Value > 0;

    private static bool ValidDistance(double distance) => double.IsFinite(distance) && distance > 0;

    // A 1e-12 relative tolerance (~4,500 double-precision unit roundoffs) only accommodates
    // floating-point summation differences between Facilities endpoint distances and loop geometry.
    private static bool MatchesLoopDistance(double actual, double expected) =>
        ValidDistance(actual) && ValidDistance(expected)
        && Math.Abs(actual - expected) <= Math.Max(actual, expected) * 1e-12;
}
