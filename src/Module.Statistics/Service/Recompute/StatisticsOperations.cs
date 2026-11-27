using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service.Recompute;

/// <summary>A point-in-time read of the projection's operational backlog (plan Phase 5).</summary>
public sealed record StatisticsBacklogSnapshot(
    int TransponderRequests,
    int RangeRequests,
    int FailingRequests,
    DateTime? OldestRequestCreatedAtUtc,
    string? LastError,
    IReadOnlyList<(RecomputeReason Reason, int Requests)> ByReason,
    int UnresolvedPassings,
    int ManualDecisionReviews,
    DateTime? OldestManualDecisionReviewAtUtc);

/// <summary>
/// Admin recompute jobs and backlog reads, shared by the Statistics API (progress view) and the processor
/// (backlog metrics). Read-only apart from <see cref="CreateJob"/>, which adds to the caller's context.
/// </summary>
public static class StatisticsOperations
{
    public const int NoteMaxLength = 500;

    /// <summary>
    /// Adds a job and its <see cref="RecomputeReason.Manual"/> request to the caller's context; the caller saves both
    /// in one transaction. A transponder job is one request; any other job is a range request the worker splits.
    /// </summary>
    public static RecomputeJob CreateJob(StatisticsDbContext storage, Guid? transponderId, Guid? installationId,
        DateTime? fromUtc, DateTime? toUtc, string? note, Guid? requestedBy, DateTime now)
    {
        var job = new RecomputeJob
        {
            Id = Guid.NewGuid(), TransponderId = transponderId, InstallationId = installationId, FromUtc = fromUtc,
            ToUtc = toUtc, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(), RequestedByUserId = requestedBy,
            CreatedAtUtc = now, TotalTransponders = transponderId is null ? null : 1
        };
        storage.Add(job);
        storage.Add(new RecomputeRequest
        {
            // A transponder job ignores the installation filter: the transponder's sessions are recomputed whole.
            TransponderId = transponderId, InstallationId = transponderId is null ? installationId : null,
            FromUtc = fromUtc, ToUtc = toUtc, Reason = RecomputeReason.Manual, CausationId = job.Id, CreatedAtUtc = now
        });
        return job;
    }

    public static async Task<StatisticsBacklogSnapshot> ReadBacklogAsync(StatisticsDbContext storage, CancellationToken cancellationToken)
    {
        var requests = storage.Set<RecomputeRequest>().AsNoTracking();
        var byReason = await requests
            .GroupBy(r => new { r.Reason, Range = r.TransponderId == null })
            .Select(g => new { g.Key.Reason, g.Key.Range, Count = g.Count(), Failing = g.Count(r => r.Attempts > 0), Oldest = g.Min(r => r.CreatedAtUtc) })
            .ToListAsync(cancellationToken);
        var lastError = await requests.Where(r => r.Attempts > 0 && r.LastError != null)
            .OrderByDescending(r => r.NotBeforeUtc).Select(r => r.LastError).FirstOrDefaultAsync(cancellationToken);
        var unresolved = await storage.Set<UnresolvedPassing>().CountAsync(cancellationToken);
        var reviews = await storage.Set<ManualDecisionReview>().AsNoTracking()
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Oldest = g.Min(r => r.CreatedAtUtc) })
            .FirstOrDefaultAsync(cancellationToken);
        return new StatisticsBacklogSnapshot(
            byReason.Where(r => !r.Range).Sum(r => r.Count),
            byReason.Where(r => r.Range).Sum(r => r.Count),
            byReason.Sum(r => r.Failing),
            byReason.Count == 0 ? null : byReason.Min(r => r.Oldest),
            lastError,
            byReason.GroupBy(r => r.Reason).OrderBy(g => g.Key).Select(g => (g.Key, g.Sum(r => r.Count))).ToList(),
            unresolved,
            reviews?.Count ?? 0,
            reviews?.Oldest);
    }

    /// <summary>The most recent jobs, newest first, with progress read from their pending requests.</summary>
    public static async Task<IReadOnlyList<RecomputeJobDTO>> ReadJobsAsync(StatisticsDbContext storage, int limit,
        CancellationToken cancellationToken, Guid? jobId = null)
    {
        var query = storage.Set<RecomputeJob>().AsNoTracking();
        if (jobId is { } id) query = query.Where(j => j.Id == id);
        var jobs = await query.OrderByDescending(j => j.CreatedAtUtc).ThenByDescending(j => j.Id).Take(limit)
            .ToListAsync(cancellationToken);
        if (jobs.Count == 0) return [];
        var ids = jobs.Select(j => (Guid?)j.Id).ToArray();
        var pending = await storage.Set<RecomputeRequest>().AsNoTracking()
            .Where(r => ids.Contains(r.CausationId))
            .GroupBy(r => r.CausationId)
            .Select(g => new
            {
                Job = g.Key,
                Range = g.Count(r => r.TransponderId == null),
                Remaining = g.Count(r => r.TransponderId != null),
                Failing = g.Count(r => r.Attempts > 0),
                LastError = g.Where(r => r.LastError != null).OrderByDescending(r => r.NotBeforeUtc).Select(r => r.LastError).FirstOrDefault()
            })
            .ToDictionaryAsync(p => p.Job!.Value, cancellationToken);
        return jobs.Select(job =>
        {
            pending.TryGetValue(job.Id, out var progress);
            var status = progress is { Range: > 0 } || job.TotalTransponders is null ? RecomputeJobStatus.Queued
                : progress is { Remaining: > 0 } ? RecomputeJobStatus.Running
                : RecomputeJobStatus.Completed;
            return new RecomputeJobDTO(job.Id, Utc(job.FromUtc), Utc(job.ToUtc), job.TransponderId, job.InstallationId, job.Note,
                job.RequestedByUserId, Utc(job.CreatedAtUtc)!.Value, status, job.TotalTransponders, progress?.Remaining ?? 0,
                progress?.Failing ?? 0, progress?.LastError, Utc(job.CompletedAtUtc));
        }).ToList();
    }

    public static RecomputeBacklogDTO ToDto(this StatisticsBacklogSnapshot snapshot) => new(
        snapshot.TransponderRequests, snapshot.RangeRequests, snapshot.FailingRequests, Utc(snapshot.OldestRequestCreatedAtUtc),
        snapshot.LastError, snapshot.ByReason.Select(r => new RecomputeBacklogReasonDTO(r.Reason.ToString(), r.Requests)).ToList(),
        snapshot.UnresolvedPassings, snapshot.ManualDecisionReviews, Utc(snapshot.OldestManualDecisionReviewAtUtc));

    /// <summary>
    /// Records the transponder count of jobs whose range request was just split (in the caller's transaction).
    /// </summary>
    public static Task MarkJobExpandedAsync(StatisticsDbContext storage, Guid jobId, int transponders, DateTime now,
        CancellationToken cancellationToken) =>
        storage.Set<RecomputeJob>().Where(j => j.Id == jobId && j.TotalTransponders == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(j => j.TotalTransponders, transponders)
                .SetProperty(j => j.CompletedAtUtc, j => transponders == 0 ? now : j.CompletedAtUtc), cancellationToken);

    /// <summary>
    /// Stamps jobs whose last request the caller's transaction just removed. Best effort: two replicas finishing a
    /// job's last two transponders at once may both see the other's request and skip the stamp, which only leaves
    /// <see cref="RecomputeJob.CompletedAtUtc"/> empty; the status is read from the requests.
    /// </summary>
    public static Task MarkJobsCompletedAsync(StatisticsDbContext storage, IReadOnlyCollection<Guid> jobIds, DateTime now,
        CancellationToken cancellationToken)
    {
        if (jobIds.Count == 0) return Task.CompletedTask;
        var ids = jobIds.ToArray();
        return storage.Set<RecomputeJob>()
            .Where(j => ids.Contains(j.Id) && j.CompletedAtUtc == null && j.TotalTransponders != null
                && !storage.Set<RecomputeRequest>().Any(r => r.CausationId == j.Id))
            .ExecuteUpdateAsync(set => set.SetProperty(j => j.CompletedAtUtc, now), cancellationToken);
    }

    private static DateTimeOffset? Utc(DateTime? value) =>
        value is { } v ? new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)) : null;
}
