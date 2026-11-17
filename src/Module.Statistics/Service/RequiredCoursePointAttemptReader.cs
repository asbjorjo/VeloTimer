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
    internal sealed record Attempt(DateTime TimeStart, Guid FacilityId, bool Satisfied, StatisticsEntry? ExistingEntry = null);

    public async Task<Attempt?> ReadAsync(SimpleStatisticsItemConfig config, Guid transponderId,
        DateTime segmentStart, DateTime finish, Guid segmentStartPoint, CancellationToken cancellationToken)
    {
        var rider = storage.Set<Sample>().AsNoTracking().Where(s => s.TransponderId == transponderId);
        // SampleComplete has no facility/sample ID. Recover it only from its exact persisted edge.
        // TimingSampleHandler saves this edge BEFORE publishing. Missing or ambiguous evidence is
        // an integration error, not permission to fall back to another facility or acknowledge success.
        var facilities = await rider.Where(s => s.TimeStart == segmentStart && s.TimeEnd == finish
                && s.CoursePointStartId == segmentStartPoint && s.CoursePointEndId == config.CoursePointEnd)
            .Select(s => s.FacilityId).Distinct().ToListAsync(cancellationToken);
        if (facilities.Count != 1 || facilities[0] == Guid.Empty)
            throw new InvalidOperationException("Required-point completion must identify exactly one persisted facility.");
        var facility = facilities[0];

        // A replay must not reclassify the attempt using samples that arrived after its first result,
        // even if its first event publication failed after commit. Return the original entry so the
        // caller can retry that publication (at least once). Creation is serialized by transponder.
        var existing = await storage.Set<StatisticsEntry>().FirstOrDefaultAsync(e => e.TransponderId == transponderId
                && e.StatisticsItemConfigId == config.Id && e.FacilityId == facility && e.TimeEnd == finish,
                cancellationToken);
        if (existing is not null)
            return new(existing.TimeStart, facility, existing.RequiredCoursePointsSatisfied, existing);

        var samples = rider.Where(s => s.FacilityId == facility);
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
            .Select(s => new Edge(s.TimeStart, s.TimeEnd, s.CoursePointStartId, s.CoursePointEndId))
            .ToListAsync(cancellationToken);
        var edges = evidence.Distinct()
            .Where(s => !(s.End == timeStart && s.Start < timeStart && s.To == config.CoursePointStart)
                && !(s.Start == finish && s.End > finish && s.From == config.CoursePointEnd))
            .OrderBy(s => s.Start).ThenBy(s => s.End).ToList();
        var satisfied = timeStart < finish && edges.Count > 0;
        var instant = timeStart;
        var point = config.CoursePointStart;
        var requiredIndex = 0;
        foreach (var edge in edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (edge.Start != instant || edge.From != point || edge.End <= edge.Start || edge.End > finish)
            {
                satisfied = false;
                break;
            }
            instant = edge.End;
            point = edge.To;
            if (instant == finish) continue; // Only strict intermediate observations count.
            if (point == config.CoursePointStart || point == config.CoursePointEnd)
            {
                satisfied = false; // Another start/finish separates attempts.
                break;
            }
            if (config.RequiredCoursePointIds.Contains(point))
            {
                if (requiredIndex >= config.RequiredCoursePointIds.Length
                    || point != config.RequiredCoursePointIds[requiredIndex++])
                {
                    satisfied = false;
                    break;
                }
            }
        }
        satisfied &= instant == finish && point == config.CoursePointEnd
            && requiredIndex == config.RequiredCoursePointIds.Length;
        return new(timeStart, facility, satisfied);
    }

    private sealed record Edge(DateTime Start, DateTime End, Guid From, Guid To);
}
