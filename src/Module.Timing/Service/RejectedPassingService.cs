using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Service;

/// <summary>Lists, reprocesses and dismisses <see cref="RejectedPassing"/> rows.</summary>
public sealed class RejectedPassingService(TimingDbContext storage, IAgentService agentService)
{
    private const int BatchSize = 200;

    public async Task<RejectedPassingPage> ListAsync(string? agentId, PassingRejectionReason? reason, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 0) throw new ArgumentException("Page must not be negative.");
        if (pageSize is < 1 or > 100) throw new ArgumentException("Page size must be between 1 and 100.");
        var query = Filter(agentId, reason);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(r => r.LastSeenAt).ThenBy(r => r.Id)
            .Skip(page * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new(rows.Select(ToDto).ToList(), total);
    }

    /// <summary>
    /// Re-submits matching rows, oldest passing first, through the normal passing path. A row is deleted once it
    /// records, is a duplicate, or becomes a <see cref="PassingConflict"/>; a row that is still rejected stays with its reason updated.
    /// </summary>
    public async Task<ReprocessRejectedPassingsResult> ReprocessAsync(string? agentId, PassingRejectionReason? reason,
        CancellationToken cancellationToken)
    {
        int recorded = 0, duplicate = 0, conflict = 0, stillRejected = 0;
        // Snapshot the selection first: still-rejected rows stay in the table and must not be picked up again.
        var ids = await Filter(agentId, reason).OrderBy(r => r.Time).ThenBy(r => r.Id).Select(r => r.Id).ToListAsync(cancellationToken);
        foreach (var chunk in ids.Chunk(BatchSize))
        {
            var batch = await storage.Set<RejectedPassing>().AsNoTracking().Where(r => chunk.Contains(r.Id))
                .OrderBy(r => r.Time).ThenBy(r => r.Id).ToListAsync(cancellationToken);
            foreach (var row in batch)
            {
                var result = await agentService.RegisterPassingAsync(new PassingObservation(row.AgentId, row.SourcePassingId,
                    row.TransponderId, row.TransponderType, row.LoopId, row.Time, row.LowBattery, row.LowStrength, row.LowHits,
                    row.Strength, row.Hits), cancellationToken);
                switch (result.Outcome)
                {
                    case PassingRegistrationOutcome.Recorded: recorded++; break;
                    case PassingRegistrationOutcome.Duplicate: duplicate++; break;
                    case PassingRegistrationOutcome.Conflict: conflict++; break;
                    default: stillRejected++; break;
                }
            }
        }
        return new(recorded, duplicate, conflict, stillRejected);
    }

    public async Task<bool> DismissAsync(Guid id, CancellationToken cancellationToken) =>
        await storage.Set<RejectedPassing>().Where(r => r.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;

    private IQueryable<RejectedPassing> Filter(string? agentId, PassingRejectionReason? reason)
    {
        var query = storage.Set<RejectedPassing>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(agentId)) query = query.Where(r => r.AgentId == agentId.Trim());
        if (reason is { } value) query = query.Where(r => r.Reason == value);
        return query;
    }

    public static RejectedPassingDTO ToDto(RejectedPassing row) => new(row.Id, row.AgentId, row.SourcePassingId,
        new DateTimeOffset(DateTime.SpecifyKind(row.Time, DateTimeKind.Utc)), row.TransponderType, row.TransponderId, row.LoopId,
        row.LowBattery, row.LowStrength, row.LowHits, row.Reason.ToString(),
        new DateTimeOffset(DateTime.SpecifyKind(row.FirstSeenAt, DateTimeKind.Utc)),
        new DateTimeOffset(DateTime.SpecifyKind(row.LastSeenAt, DateTimeKind.Utc)), row.Occurrences);
}
