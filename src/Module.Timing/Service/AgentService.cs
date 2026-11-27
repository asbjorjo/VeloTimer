using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlimMessageBus;
using System.Diagnostics;
using VeloTime.Module.Timing.Interface.Messages;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Service;

/// <summary>
/// Records agent passing observations as immutable facts. The observation identity is
/// (installation, SourcePassingId); a redelivered observation is a no-op, and one that contradicts a recorded
/// passing is kept as a <see cref="PassingConflict"/> instead of changing the fact. A new fact and its
/// <see cref="PassingRecordedV1"/> outbox row are written in one transaction.
/// </summary>
public class AgentService(InstallationService installationService, TimingDbContext storage, IMessageBus messageBus, Metrics metrics,
    ILogger<AgentService>? logger = null, TimeProvider? timeProvider = null) : IAgentService
{
    private readonly ILogger _logger = logger ?? NullLogger<AgentService>.Instance;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<PassingRegistration> RegisterPassingAsync(PassingObservation observation, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("RegisterPassing");
        if (string.IsNullOrWhiteSpace(observation.SourcePassingId))
            throw new ArgumentException("A passing observation must carry the agent's SourcePassingId.", nameof(observation));
        activity?.SetTag("SourcePassingId", observation.SourcePassingId);

        // Identity resolution creates configuration rows idempotently and outside the fact transaction,
        // so the cache never holds entities from a rolled-back transaction.
        var installation =
            await installationService.GetInstallationForAgent(observation.AgentId, cancellationToken: cancellationToken) ??
            await installationService.CreateInstallationForAgent(observation.AgentId, cancellationToken: cancellationToken);

        var timingPoint =
            await installationService.GetTimingPoint(installation, observation.LoopId, cancellationToken: cancellationToken) ??
            await installationService.CreateTimingPoint(installation, observation.LoopId, cancellationToken: cancellationToken);

        var transponder =
            await installationService.GetTransponder(observation.TransponderId, installation.TimingSystem, cancellationToken: cancellationToken) ??
            await installationService.RegisterTransponder(new MylapsX2Transponder() { SystemId = observation.TransponderId }, cancellationToken: cancellationToken);

        var received = new Passing
        {
            InstallationId = installation.Id,
            SourcePassingId = observation.SourcePassingId,
            Time = PassingTime.Normalize(observation.Time),
            TimingPointId = timingPoint.Id,
            TransponderId = transponder.Id,
            LowBattery = observation.LowBattery,
            LowStrength = observation.LowStrength,
            LowHits = observation.LowHits
        };

        var ownsTransaction = storage.Database.CurrentTransaction is null;
        IDbContextTransaction? transaction = ownsTransaction ? await storage.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var result = await RegisterAsync(received, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);

            var outcome = result.Outcome.ToString().ToLowerInvariant();
            activity?.SetTag("Outcome", outcome);
            activity?.SetTag("Duplicate", result.Outcome != PassingRegistrationOutcome.Recorded);
            if (result.Conflict is { } conflict)
                _logger.LogWarning(
                    "Rejected passing {SourcePassingId} from agent {AgentId} ({Reason}); it conflicts with recorded passing {PassingId}.",
                    conflict.SourcePassingId, observation.AgentId, conflict.Reason, conflict.ExistingPassingId);
            metrics.PassingProcessed(observation.AgentId, activity, outcome);
            return result;
        }
        catch
        {
            storage.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    private async Task<PassingRegistration> RegisterAsync(Passing received, CancellationToken cancellationToken)
    {
        var sameSource = await storage.Set<Passing>().AsNoTracking()
            .SingleOrDefaultAsync(p => p.InstallationId == received.InstallationId && p.SourcePassingId == received.SourcePassingId, cancellationToken);
        if (sameSource is not null)
        {
            return SamePayload(sameSource, received)
                ? new PassingRegistration(PassingRegistrationOutcome.Duplicate, sameSource)
                : await RecordConflictAsync(sameSource, received, PassingConflictReason.SourcePassingIdPayloadMismatch, cancellationToken);
        }

        var sameObservation = await storage.Set<Passing>().AsNoTracking()
            .SingleOrDefaultAsync(p => p.TransponderId == received.TransponderId && p.Time == received.Time
                && p.TimingPointId == received.TimingPointId, cancellationToken);
        if (sameObservation is not null)
            return await RecordConflictAsync(sameObservation, received, PassingConflictReason.DuplicateObservation, cancellationToken);

        storage.Add(received);
        await storage.SaveChangesAsync(cancellationToken);
        storage.Entry(received).State = EntityState.Detached;

        // The Timing outbox writes on this context's connection inside the active transaction.
        await messageBus.Publish(new PassingRecordedV1(
            PassingId: received.Id,
            InstallationId: received.InstallationId,
            TimingPointId: received.TimingPointId,
            TransponderId: received.TransponderId,
            Time: received.Time,
            LowBattery: received.LowBattery,
            LowStrength: received.LowStrength,
            LowHits: received.LowHits), cancellationToken: cancellationToken);

        return new PassingRegistration(PassingRegistrationOutcome.Recorded, received);
    }

    private async Task<PassingRegistration> RecordConflictAsync(Passing existing, Passing received, PassingConflictReason reason,
        CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var conflict = await storage.Set<PassingConflict>().SingleOrDefaultAsync(c =>
            c.InstallationId == received.InstallationId && c.SourcePassingId == received.SourcePassingId && c.Reason == reason
            && c.TransponderId == received.TransponderId && c.TimingPointId == received.TimingPointId && c.Time == received.Time
            && c.LowBattery == received.LowBattery && c.LowStrength == received.LowStrength && c.LowHits == received.LowHits,
            cancellationToken);
        if (conflict is null)
        {
            conflict = new PassingConflict
            {
                InstallationId = received.InstallationId,
                SourcePassingId = received.SourcePassingId,
                Reason = reason,
                ExistingPassingId = existing.Id,
                TransponderId = received.TransponderId,
                TimingPointId = received.TimingPointId,
                Time = received.Time,
                LowBattery = received.LowBattery,
                LowStrength = received.LowStrength,
                LowHits = received.LowHits,
                FirstSeenAt = now,
                LastSeenAt = now
            };
            storage.Add(conflict);
        }
        else
        {
            conflict.Occurrences++;
            conflict.LastSeenAt = now;
        }
        await storage.SaveChangesAsync(cancellationToken);
        storage.Entry(conflict).State = EntityState.Detached;
        return new PassingRegistration(PassingRegistrationOutcome.Conflict, existing, conflict);
    }

    private static bool SamePayload(Passing stored, Passing received) =>
        stored.TimingPointId == received.TimingPointId
        && stored.TransponderId == received.TransponderId
        && stored.Time == received.Time
        && stored.LowBattery == received.LowBattery
        && stored.LowStrength == received.LowStrength
        && stored.LowHits == received.LowHits;
}

public static class PassingTime
{
    /// <summary>
    /// Passing times are UTC instants at PostgreSQL <c>timestamptz</c> precision (1 µs), so a stored fact,
    /// its event and a redelivered observation compare equal.
    /// </summary>
    public static DateTime Normalize(DateTime time)
    {
        var utc = time.Kind switch
        {
            DateTimeKind.Local => time.ToUniversalTime(),
            // Backend messages use UTC, including values without Kind metadata.
            DateTimeKind.Unspecified => DateTime.SpecifyKind(time, DateTimeKind.Utc),
            _ => time
        };
        return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }
}
