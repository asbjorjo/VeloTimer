using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service;

/// <summary>
/// Uses resolved (course point, UTC instant) observations as nodes, not unordered point presence.
/// Installation identity is not available here. Exact duplicate edges are equivalent evidence;
/// conflicting/overlapping edges, gaps and non-increasing instants fail closed.
/// </summary>
internal sealed class RequiredCoursePointAttemptReader(StatisticsDbContext storage)
{
    internal sealed record Attempt(DateTime TimeStart, Guid FacilityId, bool Satisfied);

    public async Task<Attempt?> ReadAsync(SimpleStatisticsItemConfig config, Sample completion,
        CancellationToken cancellationToken)
    {
        // The service has resolved and validated one exact source, including legacy uniqueness.
        // Never rediscover its facility by a tuple that can describe independent samples.
        var segmentStart = completion.TimeStart;
        var finish = completion.TimeEnd;
        var facility = completion.FacilityId;
        var samples = storage.Set<Sample>().AsNoTracking()
            .Where(s => s.TransponderId == completion.TransponderId && s.FacilityId == facility);
        // Use the newest observed start (arrival OR departure), never an older start with a more
        // convenient set of checkpoints. Equal endpoints denote a lap; the final arrival is excluded.
        var departure = await samples.Where(s => s.CoursePointStartId == config.CoursePointStart
                && s.TimeStart <= segmentStart)
            .OrderByDescending(s => s.TimeStart).Select(s => (DateTime?)s.TimeStart).FirstOrDefaultAsync(cancellationToken);
        var arrival = await samples.Where(s => s.CoursePointEndId == config.CoursePointStart
                && s.TimeEnd <= segmentStart)
            .OrderByDescending(s => s.TimeEnd).Select(s => (DateTime?)s.TimeEnd).FirstOrDefaultAsync(cancellationToken);
        var start = departure is null ? arrival
            : arrival is null || departure >= arrival ? departure : arrival;
        if (start is null) return null; // No identifiable attempt, as with legacy endpoint-only processing.

        var timeStart = start.Value;
        // Load only edges touching the selected interval, including spanning/conflicting intervals.
        // Adjacent edges outside its endpoints do not belong to this attempt.
        var evidence = await samples.Where(s => (s.TimeStart <= finish && s.TimeEnd >= timeStart)
                || (s.TimeEnd <= finish && s.TimeStart >= timeStart && s.TimeStart <= finish))
            .Select(s => new OrderedCourseAttempt.Edge(s.TimeStart, s.TimeEnd, s.CoursePointStartId, s.CoursePointEndId))
            .ToListAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var result = OrderedCourseAttempt.Evaluate(evidence, timeStart, finish, config.CoursePointStart,
            config.CoursePointEnd, config.RequiredCoursePointIds, cancellationToken);
        return new(timeStart, facility, result.RequiredCoursePointsSatisfied);
    }

}
