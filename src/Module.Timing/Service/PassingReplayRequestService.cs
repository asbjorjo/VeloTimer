using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlimMessageBus;
using VeloTime.Agent.Interface;
using VeloTime.Agent.Interface.Messages.Control;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Messaging;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Service;

/// <summary>A replay request the platform refuses before sending anything; <see cref="Exception.Message"/> is the reason code.</summary>
public sealed class PassingReplayValidationException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}

/// <summary>Platform-side reason codes for refusing a replay request (agent reason codes are reused where they fit).</summary>
public static class PassingReplayValidationReasons
{
    public const string NoCapabilityReport = "NoCapabilityReport";
    public const string UnsupportedMode = PassingReplayRejectionReasons.UnsupportedMode;
    public const string InvalidRange = PassingReplayRejectionReasons.InvalidRange;
    public const string RangeTooLong = PassingReplayRejectionReasons.RangeTooLong;
    public const string OutOfRetention = PassingReplayRejectionReasons.OutOfRetention;
    public const string LoopFilterUnsupported = PassingReplayRejectionReasons.LoopFilterUnsupported;
    public const string Busy = PassingReplayRejectionReasons.Busy;
    public const string NoteTooLong = "NoteTooLong";
}

/// <summary>
/// Creates passing replay requests against the agent's stored capabilities, applies the agent's status reports and
/// counts Timing's outcome for each replayed passing. See docs/passing-replay-plan.md.
/// </summary>
public sealed class PassingReplayRequestService(TimingDbContext storage, IMessageBus? messageBus = null,
    ILogger<PassingReplayRequestService>? logger = null, TimeProvider? timeProvider = null)
{
    /// <summary>How long a replay command stays deliverable.</summary>
    public static readonly TimeSpan CommandExpiry = TimingOutboxExtensions.AgentCommandTimeToLive;

    private readonly ILogger _logger = logger ?? NullLogger<PassingReplayRequestService>.Instance;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    private static readonly PassingReplayRequestState[] Active =
        [PassingReplayRequestState.Pending, PassingReplayRequestState.Accepted, PassingReplayRequestState.Running];

    /// <summary>
    /// Validates the request against the agent's latest capability report and stores it together with the command in
    /// one outbox transaction. Throws <see cref="KeyNotFoundException"/> for an unknown installation and
    /// <see cref="PassingReplayValidationException"/> when the request cannot be offered.
    /// </summary>
    public async Task<PassingReplayRequest> CreateAsync(Guid installationId, CreatePassingReplayRequest request, Guid? userId,
        CancellationToken cancellationToken)
    {
        if (messageBus is null) throw new InvalidOperationException("Messaging is required to send agent commands.");
        var installation = await storage.Set<Installation>().AsNoTracking()
            .SingleOrDefaultAsync(i => i.Id == installationId, cancellationToken) ?? throw new KeyNotFoundException();
        var from = AsUtc(request.FromUtc);
        var to = AsUtc(request.ToUtc);
        var loops = (request.LoopIds ?? []).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).Distinct().ToList();
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > 500 }) throw new PassingReplayValidationException(PassingReplayValidationReasons.NoteTooLong);
        Validate(installation, request.Mode, from, to, loops);

        var now = _time.GetUtcNow().UtcDateTime;
        await using var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
        // One replay per agent at a time; the agent enforces it too, this just answers sooner.
        if (await storage.Set<PassingReplayRequest>().AnyAsync(r => r.InstallationId == installationId
                && Active.Contains(r.State) && r.ExpiresAt > now, cancellationToken))
            throw new PassingReplayValidationException(PassingReplayValidationReasons.Busy);
        var replay = new PassingReplayRequest
        {
            Id = Guid.NewGuid(),
            InstallationId = installationId,
            AgentId = installation.AgentId,
            Mode = request.Mode,
            FromUtc = from,
            ToUtc = to,
            LoopIds = loops,
            RequestedByUserId = userId,
            Note = note,
            CreatedAt = now,
            ExpiresAt = now + CommandExpiry,
            UpdatedAt = now
        };
        storage.Add(replay);
        await messageBus.Publish(new ReplayPassingsCommandV1(replay.Id, replay.AgentId, replay.Mode, from, to,
            loops.Count == 0 ? null : loops, replay.ExpiresAt), cancellationToken: cancellationToken);
        await storage.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        storage.Entry(replay).State = EntityState.Detached;
        _logger.LogInformation("Requested {Mode} replay {RequestId} of {From:o} to {To:o} from agent {AgentId}.",
            replay.Mode, replay.Id, from, to, replay.AgentId);
        return replay;
    }

    private static void Validate(Installation installation, string mode, DateTime from, DateTime to, List<string> loops)
    {
        if (installation.CapabilitiesReportedAt is null)
            throw new PassingReplayValidationException(PassingReplayValidationReasons.NoCapabilityReport);
        var capability = installation.AgentCapabilities
            .Where(c => c.Name == AgentCapabilities.PassingReplay && c.PassingReplay is not null)
            .Select(c => c.PassingReplay!)
            .FirstOrDefault(c => c.Mode == mode)
            ?? throw new PassingReplayValidationException(PassingReplayValidationReasons.UnsupportedMode);
        if (from >= to) throw new PassingReplayValidationException(PassingReplayValidationReasons.InvalidRange);
        if (capability.MaxRangeSeconds is { } max && (to - from).TotalSeconds > max)
            throw new PassingReplayValidationException(PassingReplayValidationReasons.RangeTooLong);
        if (capability.OldestAvailableUtc is { } oldest && to <= oldest)
            throw new PassingReplayValidationException(PassingReplayValidationReasons.OutOfRetention);
        if (loops.Count > 0 && !capability.SupportsLoopFilter)
            throw new PassingReplayValidationException(PassingReplayValidationReasons.LoopFilterUnsupported);
    }

    public async Task<IReadOnlyList<PassingReplayRequest>> ListAsync(Guid installationId, int limit, CancellationToken cancellationToken)
    {
        if (!await storage.Set<Installation>().AnyAsync(i => i.Id == installationId, cancellationToken)) throw new KeyNotFoundException();
        return await storage.Set<PassingReplayRequest>().AsNoTracking()
            .Where(r => r.InstallationId == installationId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
    }

    public async Task<PassingReplayRequest> GetAsync(Guid installationId, Guid requestId, CancellationToken cancellationToken) =>
        await storage.Set<PassingReplayRequest>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.InstallationId == installationId && r.Id == requestId, cancellationToken)
        ?? throw new KeyNotFoundException();

    /// <summary>
    /// Applies an agent's status report. Reports older than the last one applied are ignored (the subscription is not
    /// ordered), and a finished request never goes back to running. Returns whether the report changed anything.
    /// </summary>
    public async Task<bool> ApplyStatusAsync(string agentId, PassingReplayStatusV1 status, CancellationToken cancellationToken)
    {
        var replay = await storage.Set<PassingReplayRequest>()
            .SingleOrDefaultAsync(r => r.Id == status.RequestId, cancellationToken);
        if (replay is null || replay.AgentId != agentId)
        {
            _logger.LogWarning("Ignoring replay status {State} for unknown request {RequestId} from agent {AgentId}.",
                status.State, status.RequestId, agentId);
            return false;
        }
        if (!Enum.TryParse<PassingReplayRequestState>(status.State, out var state) || state is PassingReplayRequestState.Pending
            or PassingReplayRequestState.Expired)
            throw new InvalidOperationException($"Unknown passing replay state '{status.State}'.");
        var reportedAt = AsUtc(status.ReportedAtUtc);
        if (replay.StatusReportedAt is { } last && reportedAt < last) return false;
        if (IsFinished(replay.State) && !IsFinished(state)) return false;

        replay.State = state;
        replay.StatusReportedAt = reportedAt;
        replay.Read = status.Read;
        replay.Published = status.Published;
        replay.NewInAgentStore = status.NewInAgentStore;
        replay.Reason = status.Reason;
        replay.UpdatedAt = _time.GetUtcNow().UtcDateTime;
        await storage.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static bool IsFinished(PassingReplayRequestState state) =>
        state is PassingReplayRequestState.Completed or PassingReplayRequestState.Failed or PassingReplayRequestState.Rejected;

    /// <summary>Marks requests the agent never answered before the command expired. Returns how many.</summary>
    public async Task<int> ExpireUnansweredAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        return await storage.Set<PassingReplayRequest>()
            .Where(r => r.State == PassingReplayRequestState.Pending && r.ExpiresAt <= now)
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.State, PassingReplayRequestState.Expired)
                .SetProperty(r => r.UpdatedAt, now), cancellationToken);
    }

    /// <summary>
    /// Counts what happened to one replayed passing. Runs on the caller's connection, so inside the registration's
    /// transaction it commits or rolls back with it. Unknown request IDs are ignored.
    /// </summary>
    public static Task CountOutcomeAsync(TimingDbContext storage, Guid requestId, string agentId, PassingRegistrationOutcome outcome,
        CancellationToken cancellationToken)
    {
        var query = storage.Set<PassingReplayRequest>().Where(r => r.Id == requestId && r.AgentId == agentId);
        return outcome switch
        {
            PassingRegistrationOutcome.Recorded => query.ExecuteUpdateAsync(s => s.SetProperty(r => r.Recorded, r => r.Recorded + 1), cancellationToken),
            PassingRegistrationOutcome.Duplicate => query.ExecuteUpdateAsync(s => s.SetProperty(r => r.Duplicate, r => r.Duplicate + 1), cancellationToken),
            PassingRegistrationOutcome.Conflict => query.ExecuteUpdateAsync(s => s.SetProperty(r => r.Conflict, r => r.Conflict + 1), cancellationToken),
            _ => query.ExecuteUpdateAsync(s => s.SetProperty(r => r.Rejected, r => r.Rejected + 1), cancellationToken)
        };
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
