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
            .Select(s => new OrderedCourseAttempt.Edge(s.TimeStart, s.TimeEnd, s.CoursePointStartId, s.CoursePointEndId))
            .ToListAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var result = OrderedCourseAttempt.Evaluate(evidence, timeStart, finish, config.CoursePointStart,
            config.CoursePointEnd, config.RequiredCoursePointIds, cancellationToken);
        return new(timeStart, facility, result.RequiredCoursePointsSatisfied);
    }

}
