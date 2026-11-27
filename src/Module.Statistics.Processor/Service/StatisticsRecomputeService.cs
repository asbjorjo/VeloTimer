using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service.Recompute;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service;

public sealed class StatisticsRecomputeOptions
{
    public const string Section = "Statistics:Recompute";

    /// <summary>Passings at least this far apart start a new riding session (decision §6.2).</summary>
    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan SessionGap { get; set; } = SessionBuilder.DefaultSessionGap;

    /// <summary>Fallback poll interval when no wake-up arrives; request rows are the source of work.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan IdlePollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>First retry delay for a failed recompute; doubles per attempt up to <see cref="MaxFailureDelay"/>.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:10:00")]
    public TimeSpan FailureDelay { get; set; } = TimeSpan.FromSeconds(5);

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan MaxFailureDelay { get; set; } = TimeSpan.FromMinutes(5);
}

public enum RecomputeStepOutcome
{
    /// <summary>No request is due.</summary>
    Idle,
    /// <summary>A range-wide request was split into per-transponder requests.</summary>
    Expanded,
    /// <summary>One transponder was recomputed and its requests were removed.</summary>
    Recomputed,
    /// <summary>The recompute failed and rolled back; its requests are retried later.</summary>
    Failed
}

public sealed record RecomputeStepResult(RecomputeStepOutcome Outcome, Guid? TransponderId = null, Exception? Error = null);

/// <summary>
/// The recompute worker's unit of work (plan §3.3). Each step claims the due requests of one transponder,
/// expands their range to whole riding sessions, rebuilds samples, Simple and Multi entries from the
/// passing facts and the configuration history, and diff-applies the result by deterministic identity,
/// all in one transaction under the transponder lock. Readers see a session switch atomically.
/// PostgreSQL only (row locks with SKIP LOCKED, advisory locks).
/// </summary>
public sealed class StatisticsRecomputeService(
    StatisticsDbContext storage,
    StatisticsConfigurationHistory configuration,
    StatisticsOwnershipService ownership,
    StatisticsValidityScheduler scheduler,
    IOptions<StatisticsRecomputeOptions> options,
    Metrics metrics,
    ILogger<StatisticsRecomputeService> logger,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private TimeSpan Gap => options.Value.SessionGap;

    /// <summary>Runs one step. Never throws for a failed recompute; the failure is recorded on its requests.</summary>
    public async Task<RecomputeStepResult> ProcessNextAsync(CancellationToken cancellationToken)
    {
        if (!storage.Database.IsNpgsql()) throw new NotSupportedException("Statistics recompute requires PostgreSQL.");
        if (await ExpandNextRangeRequestAsync(cancellationToken)) return new(RecomputeStepOutcome.Expanded);

        var now = time.GetUtcNow().UtcDateTime;
        await using var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
        var transponderId = (await storage.Database.SqlQuery<Guid>($"""
            SELECT transponder_id AS "Value" FROM statistics.recompute_request
            WHERE transponder_id IS NOT NULL AND (not_before_utc IS NULL OR not_before_utc <= {now})
            ORDER BY id LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken)).Cast<Guid?>().FirstOrDefault();
        if (transponderId is not { } transponder)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(RecomputeStepOutcome.Idle);
        }

        await ownership.LockTransponderAsync(transponder, cancellationToken);
        // Requests committed while we waited for the lock are included; later ones stay for the next step.
        var requests = await storage.Set<RecomputeRequest>()
            .FromSql($"""
                SELECT * FROM statistics.recompute_request
                WHERE transponder_id = {transponder} AND (not_before_utc IS NULL OR not_before_utc <= {now})
                ORDER BY id FOR UPDATE SKIP LOCKED
                """)
            .AsNoTracking().ToListAsync(cancellationToken);
        if (requests.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(RecomputeStepOutcome.Idle);
        }

        try
        {
            await transaction.CreateSavepointAsync("statistics_recompute", cancellationToken);
            var from = requests.Any(r => r.FromUtc is null) ? null : requests.Min(r => r.FromUtc);
            var to = requests.Any(r => r.ToUtc is null) ? null : requests.Max(r => r.ToUtc);
            await RecomputeAsync(transponder, from, to, cancellationToken);
            var ids = requests.Select(r => r.Id).ToArray();
            await storage.Set<RecomputeRequest>().Where(r => ids.Contains(r.Id)).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(RecomputeStepOutcome.Recomputed, transponder);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            metrics.RecomputeFailed();
            storage.ChangeTracker.Clear();
            await transaction.RollbackToSavepointAsync("statistics_recompute", CancellationToken.None);
            var attempts = requests.Max(r => r.Attempts) + 1;
            var delay = TimeSpan.FromTicks(Math.Min(options.Value.FailureDelay.Ticks * (1L << Math.Min(attempts - 1, 20)),
                options.Value.MaxFailureDelay.Ticks));
            var error = $"{exception.GetType().Name}: {exception.Message}";
            if (error.Length > 2000) error = error[..2000];
            var ids = requests.Select(r => r.Id).ToArray();
            await storage.Set<RecomputeRequest>().Where(r => ids.Contains(r.Id)).ExecuteUpdateAsync(set => set
                .SetProperty(r => r.Attempts, attempts)
                .SetProperty(r => r.NotBeforeUtc, now + delay)
                .SetProperty(r => r.LastError, error), CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
            logger.LogError(exception, "Recompute of transponder {TransponderId} failed (attempt {Attempts}); retrying in {Delay}",
                transponder, attempts, delay);
            return new(RecomputeStepOutcome.Failed, transponder, exception);
        }
    }

    /// <summary>Replaces one range-wide request by one request per transponder with facts in its range.</summary>
    private async Task<bool> ExpandNextRangeRequestAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
        var request = (await storage.Set<RecomputeRequest>()
            .FromSql($"""
                SELECT * FROM statistics.recompute_request WHERE transponder_id IS NULL
                ORDER BY id LIMIT 1 FOR UPDATE SKIP LOCKED
                """)
            .AsNoTracking().ToListAsync(cancellationToken)).FirstOrDefault();
        if (request is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        var facts = storage.Set<PassingFact>().AsQueryable();
        if (request.FromUtc is { } from) facts = facts.Where(f => f.Time >= from);
        if (request.ToUtc is { } to) facts = facts.Where(f => f.Time <= to);
        var transponders = await facts.Select(f => f.TransponderId).Distinct().ToListAsync(cancellationToken);
        var created = time.GetUtcNow().UtcDateTime;
        foreach (var transponder in transponders.Order())
            storage.Add(new RecomputeRequest
            {
                TransponderId = transponder, FromUtc = request.FromUtc, ToUtc = request.ToUtc, Reason = request.Reason,
                CausationId = request.CausationId, CreatedAtUtc = created
            });
        await storage.SaveChangesAsync(cancellationToken);
        await storage.Set<RecomputeRequest>().Where(r => r.Id == request.Id).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        storage.ChangeTracker.Clear();
        logger.LogInformation("Split {Reason} recompute request {RequestId} into {Count} transponder requests",
            request.Reason, request.Id, transponders.Count);
        return true;
    }

    /// <summary>
    /// Recomputes every riding session of the transponder that overlaps [from, to] (null bounds are
    /// unbounded). Must run in the caller's transaction, holding the transponder lock.
    /// </summary>
    public async Task RecomputeAsync(Guid transponderId, DateTime? from, DateTime? to, CancellationToken cancellationToken)
    {
        using var activity = Instrumentation.Source.StartActivity("Recompute");
        activity?.SetTag("TransponderId", transponderId);
        var stopwatch = Stopwatch.StartNew();
        var facts = storage.Set<PassingFact>().AsNoTracking().Where(f => f.TransponderId == transponderId);
        var start = from ?? await facts.MinAsync(f => (DateTime?)f.Time, cancellationToken);
        var end = to ?? await facts.MaxAsync(f => (DateTime?)f.Time, cancellationToken);
        if (start is null || end is null) return; // No facts, so nothing can be derived.

        // Expand to whole clusters of the transponder's passings (all facilities). Every facility session
        // lies inside one cluster, and clusters only grow, so every stored row lies inside one too.
        var clusterStart = start.Value;
        while (await facts.Where(f => f.Time < clusterStart && f.Time > clusterStart - Gap)
                   .MinAsync(f => (DateTime?)f.Time, cancellationToken) is { } earlier)
            clusterStart = earlier;
        var clusterEnd = end.Value < clusterStart ? clusterStart : end.Value;
        while (await facts.Where(f => f.Time > clusterEnd && f.Time < clusterEnd + Gap)
                   .MaxAsync(f => (DateTime?)f.Time, cancellationToken) is { } later)
            clusterEnd = later;

        var loaded = await facts.Where(f => f.Time >= clusterStart && f.Time <= clusterEnd).ToListAsync(cancellationToken);
        var resolved = new List<ResolvedPassing>(loaded.Count);
        var installations = new Dictionary<Guid, InstallationFacilityHistory>();
        var courses = new Dictionary<Guid, FacilityCourseHistory>();
        var unresolved = 0;
        foreach (var fact in loaded)
        {
            if (!installations.TryGetValue(fact.InstallationId, out var installation))
                installations[fact.InstallationId] = installation = await configuration.GetInstallationAsync(fact.InstallationId, cancellationToken);
            if (installation.ResolveFacility(fact.Time) is not { } facilityId) { unresolved++; continue; }
            if (!courses.TryGetValue(facilityId, out var course))
                courses[facilityId] = course = await configuration.GetFacilityCourseAsync(facilityId, cancellationToken);
            if (course.ResolveCoursePoint(fact.TimingPointId, fact.Time) is not { } coursePointId) { unresolved++; continue; }
            resolved.Add(new(fact.PassingId, fact.TransponderId, fact.TimingPointId, fact.Time, facilityId, coursePointId));
        }

        // Definitions are read after share-locking their items, as every entry writer does, so a concurrent
        // threshold revalidation or definition edit cannot interleave with this classification.
        var itemIds = await storage.Set<StatisticsItemConfig>().Select(c => c.StatisticsItemId).Distinct().ToListAsync(cancellationToken);
        await scheduler.LockItemsAsync(itemIds, StatisticsItemLockMode.Shared, cancellationToken);
        var simpleConfigs = await storage.Set<SimpleStatisticsItemConfig>().AsNoTracking().Include(c => c.StatisticsItem)
            .OrderBy(c => c.Id).ToListAsync(cancellationToken);
        var multiConfigs = await storage.Set<MultiStatisticsItemConfig>().AsNoTracking().Include(c => c.StatisticsItem)
            .OrderBy(c => c.Id).ToListAsync(cancellationToken);
        var parentsWithMulti = multiConfigs.Select(m => m.ParentConfigId).ToHashSet();

        var storedSamples = await storage.Set<Sample>()
            .Where(s => s.TransponderId == transponderId && s.TimeStart >= clusterStart && s.TimeEnd <= clusterEnd)
            .ToListAsync(cancellationToken);
        var storedEntries = await storage.Set<StatisticsEntry>()
            .Where(e => e.TransponderId == transponderId && e.TimeStart >= clusterStart && e.TimeEnd <= clusterEnd)
            .ToListAsync(cancellationToken);
        var storedById = storedEntries.ToDictionary(e => e.Id);

        var now = time.GetUtcNow().UtcDateTime;
        var rejections = new List<DerivationRejection>();
        var samples = new List<Sample>();
        var entries = new List<StatisticsEntry>();
        var reviews = new List<ManualDecisionReview>();
        foreach (var session in SessionBuilder.Sessions(resolved, Gap))
        {
            var sessionSamples = SessionBuilder.Samples(session, courses[session.FacilityId], rejections);
            samples.AddRange(sessionSamples);
            var simple = new List<StatisticsEntry>();
            foreach (var config in simpleConfigs)
                simple.AddRange(EntryDerivation.Simple(config, StatisticsDurationBounds.Effective(config.StatisticsItem, config),
                    sessionSamples, parentsWithMulti.Contains(config.Id), rejections));
            reviews.AddRange(RecomputeDiff.CarryManualDecisions(storedById, simple, now));
            var multi = new List<StatisticsEntry>();
            foreach (var config in multiConfigs)
            {
                if (config.StatisticsItem.DistanceMode == Interface.Data.StatisticsDistanceMode.CourseLayout
                    || simpleConfigs.FirstOrDefault(c => c.Id == config.ParentConfigId)?.StatisticsItem.DistanceMode
                        == Interface.Data.StatisticsDistanceMode.CourseLayout)
                    continue;
                multi.AddRange(EntryDerivation.Multi(config, StatisticsDurationBounds.Effective(config.StatisticsItem, config), simple));
            }
            reviews.AddRange(RecomputeDiff.CarryManualDecisions(storedById, multi, now));
            entries.AddRange(simple);
            entries.AddRange(multi);
        }
        reviews.AddRange(RecomputeDiff.RemovedManualDecisions(storedEntries, entries.Select(e => e.Id).ToHashSet(), now));

        await ownership.ApplyOwnersAsync(transponderId, entries, cancellationToken);

        var samplePlan = RecomputeDiff.Samples(storedSamples, samples);
        var entryPlan = RecomputeDiff.Entries(storedEntries, entries);
        storage.RemoveRange(entryPlan.Deletes);
        storage.RemoveRange(samplePlan.Deletes);
        // Deletes first: a row whose content moved to another deterministic ID never collides on a unique key.
        await storage.SaveChangesAsync(cancellationToken);
        storage.AddRange(samplePlan.Inserts);
        storage.AddRange(entryPlan.Inserts);
        storage.AddRange(reviews);
        await storage.SaveChangesAsync(cancellationToken);
        storage.ChangeTracker.Clear();

        var changed = samplePlan.Changed + entryPlan.Changed;
        metrics.RecomputeCommitted(stopwatch.Elapsed.TotalMilliseconds, unresolved, rejections.Count, changed, reviews.Count);
        activity?.SetTag("Passings", loaded.Count);
        activity?.SetTag("Unresolved", unresolved);
        activity?.SetTag("RowsChanged", changed);
        if (reviews.Count > 0)
            logger.LogWarning("Recompute of transponder {TransponderId} surfaced {Count} manual decision(s) for review",
                transponderId, reviews.Count);
        logger.LogDebug("Recomputed transponder {TransponderId} over {Start:o}..{End:o}: {Passings} passings ({Unresolved} unresolved), " +
            "{Samples} samples, {Entries} entries, {Changed} rows changed, {Rejected} rejected candidates",
            transponderId, clusterStart, clusterEnd, loaded.Count, unresolved, samples.Count, entries.Count, changed, rejections.Count);
    }
}
