using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SlimMessageBus;
using VeloTime.Agent.Interface;
using VeloTime.Agent.Interface.Messages.Control;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Agent.Model;
using VeloTime.Agent.Service;
using VeloTime.Agent.Storage;

namespace VeloTime.Agent.Replay;

/// <summary>
/// Accepts, validates and runs passing replays. One replay runs at a time per agent: a request while one is accepted
/// or running is rejected as <c>Busy</c>. Every state change is stored in <c>agent.replay_request</c> and reported
/// with <see cref="PassingReplayStatusV1"/> in the same outbox transaction.
/// </summary>
public sealed class PassingReplayService(
    IServiceScopeFactory scopes,
    AgentStoreReplayCapabilityProvider agentStore,
    IOutboxBacklog backlog,
    IOptions<ReplayOptions> options,
    ILogger<PassingReplayService> log,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private int outstanding;

    /// <summary>Whether a replay is accepted or running.</summary>
    public bool IsBusy => Volatile.Read(ref outstanding) > 0;

    /// <summary>
    /// Handles a command: reports the stored result of a command seen before, otherwise validates it and either
    /// rejects it or stores it as accepted. Returns the request to run, or null when there is nothing to run.
    /// </summary>
    public async Task<ReplayRequest?> AcceptAsync(ReplayPassingsCommandV1 command, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var existing = await storage.Set<ReplayRequest>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.RequestId == command.RequestId, cancellationToken);
        if (existing is not null)
        {
            // A redelivered command: report what happened instead of running again.
            log.LogInformation("Replay {RequestId} was already handled ({State}); reporting its result again.",
                existing.RequestId, existing.State);
            if (existing.IsFinished) await ReportAsync(existing, existing.State, cancellationToken);
            return null;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var reason = await ValidateAsync(command, now, cancellationToken);
        var reserved = reason is null && Interlocked.CompareExchange(ref outstanding, 1, 0) == 0;
        if (reason is null && !reserved) reason = PassingReplayRejectionReasons.Busy;

        var request = new ReplayRequest
        {
            RequestId = command.RequestId,
            Mode = command.Mode,
            FromUtc = AsUtc(command.FromUtc),
            ToUtc = AsUtc(command.ToUtc),
            LoopIds = [.. command.LoopIds ?? []],
            ReceivedAtUtc = now,
            State = reason is null ? PassingReplayStates.Accepted : PassingReplayStates.Rejected,
            Reason = reason,
            UpdatedAtUtc = now
        };
        try
        {
            await SaveAndReportAsync(request, isNew: true, cancellationToken);
        }
        catch
        {
            if (reserved) Interlocked.Decrement(ref outstanding);
            throw;
        }
        if (reason is not null)
        {
            log.LogWarning("Rejected replay {RequestId} ({Mode} {From:o} to {To:o}): {Reason}.",
                command.RequestId, command.Mode, command.FromUtc, command.ToUtc, reason);
            return null;
        }
        log.LogInformation("Accepted replay {RequestId} ({Mode} {From:o} to {To:o}).", command.RequestId, command.Mode,
            command.FromUtc, command.ToUtc);
        return request;
    }

    /// <summary>
    /// Returns replays a restart interrupted, reserved so new requests are rejected as <c>Busy</c> until they finish.
    /// They run again from the start, which is safe: Timing counts passings it already has as duplicates.
    /// </summary>
    public async Task<IReadOnlyList<ReplayRequest>> ReserveInterruptedAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var interrupted = await storage.Set<ReplayRequest>().AsNoTracking()
            .Where(r => r.State == PassingReplayStates.Accepted || r.State == PassingReplayStates.Running)
            .OrderBy(r => r.ReceivedAtUtc)
            .ToListAsync(cancellationToken);
        if (interrupted.Count > 0) Interlocked.Add(ref outstanding, interrupted.Count);
        return interrupted;
    }

    private async Task<string?> ValidateAsync(ReplayPassingsCommandV1 command, DateTime now, CancellationToken cancellationToken)
    {
        // The platform checked against an earlier capability report; this check against current capabilities wins.
        if (now > AsUtc(command.ExpiresAtUtc)) return PassingReplayRejectionReasons.Expired;
        if (command.Mode != PassingReplayModes.AgentStore || !options.Value.AgentStoreEnabled)
            return PassingReplayRejectionReasons.UnsupportedMode;
        if (command.FromUtc >= command.ToUtc) return PassingReplayRejectionReasons.InvalidRange;
        var capability = await agentStore.DescribeAsync(cancellationToken);
        if (capability.MaxRange is { } max && command.ToUtc - command.FromUtc > max) return PassingReplayRejectionReasons.RangeTooLong;
        if (capability.OldestAvailableUtc is { } oldest && AsUtc(command.ToUtc) <= oldest) return PassingReplayRejectionReasons.OutOfRetention;
        if (command.LoopIds is { Count: > 0 } && !capability.SupportsLoopFilter) return PassingReplayRejectionReasons.LoopFilterUnsupported;
        return null;
    }

    /// <summary>
    /// Re-publishes every stored passing in the request's range in chunks, waiting for the outbox to drain between
    /// chunks so live passings are never queued behind more than about one chunk. Cancellation (shutdown) leaves the
    /// request running so it resumes on the next start.
    /// </summary>
    public async Task RunAsync(ReplayRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var chunkSize = Math.Max(1, settings.ChunkSize);
        var maxPending = Math.Max(1, settings.MaxPendingOutbox ?? chunkSize);
        try
        {
            request.Read = request.Published = 0;
            request.Reason = null;
            request.State = PassingReplayStates.Running;
            await SaveAndReportAsync(request, isNew: false, cancellationToken);
            var lastProgress = time.GetUtcNow();

            DateTime? afterTime = null;
            Guid afterId = Guid.Empty;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (await backlog.PendingAsync(cancellationToken) > maxPending)
                    await Task.Delay(settings.ChunkPause, time, cancellationToken);

                await using var scope = scopes.CreateAsyncScope();
                var storage = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
                var chunk = await ReadChunkAsync(storage, request, afterTime, afterId, chunkSize, cancellationToken);
                if (chunk.Count == 0) break;
                await scope.ServiceProvider.GetRequiredService<MessagingService>()
                    .RepublishAsync(chunk, request.RequestId, cancellationToken);
                request.Read += chunk.Count;
                request.Published += chunk.Count;
                afterTime = chunk[^1].Time;
                afterId = chunk[^1].Id;

                if (time.GetUtcNow() - lastProgress >= settings.ProgressInterval)
                {
                    await SaveAndReportAsync(request, isNew: false, cancellationToken);
                    lastProgress = time.GetUtcNow();
                }
                if (chunk.Count < chunkSize) break;
                await Task.Delay(settings.ChunkPause, time, cancellationToken);
            }

            request.State = PassingReplayStates.Completed;
            await SaveAndReportAsync(request, isNew: false, cancellationToken);
            log.LogInformation("Replay {RequestId} completed: {Published} passings published.", request.RequestId, request.Published);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            log.LogInformation("Replay {RequestId} stopped by shutdown after {Published} passings; it resumes on the next start.",
                request.RequestId, request.Published);
            throw;
        }
        catch (Exception exception)
        {
            log.LogError(exception, "Replay {RequestId} failed after {Published} passings.", request.RequestId, request.Published);
            request.State = PassingReplayStates.Failed;
            request.Reason = Truncate(exception.Message, 500);
            await SaveAndReportAsync(request, isNew: false, CancellationToken.None);
        }
        finally
        {
            Interlocked.Decrement(ref outstanding);
        }
    }

    private static async Task<List<Passing>> ReadChunkAsync(AgentDbContext storage, ReplayRequest request, DateTime? afterTime,
        Guid afterId, int chunkSize, CancellationToken cancellationToken)
    {
        var query = storage.Set<Passing>().AsNoTracking()
            .Where(p => p.Time >= request.FromUtc && p.Time < request.ToUtc);
        if (request.LoopIds.Count > 0)
        {
            var loops = request.LoopIds;
            query = query.Where(p => loops.Contains(p.LoopId));
        }
        if (afterTime is { } after)
            query = query.Where(p => p.Time > after || (p.Time == after && p.Id.CompareTo(afterId) > 0));
        return await query.OrderBy(p => p.Time).ThenBy(p => p.Id).Take(chunkSize).ToListAsync(cancellationToken);
    }

    private async Task SaveAndReportAsync(ReplayRequest request, bool isNew, CancellationToken cancellationToken)
    {
        request.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        await using var scope = scopes.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await using var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
        if (isNew) storage.Add(request);
        else storage.Update(request);
        await bus.Publish(Status(request, request.State, request.UpdatedAtUtc), cancellationToken: cancellationToken);
        await storage.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        storage.Entry(request).State = EntityState.Detached;
    }

    private async Task ReportAsync(ReplayRequest request, string state, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMessageBus>()
            .Publish(Status(request, state, time.GetUtcNow().UtcDateTime), cancellationToken: cancellationToken);
    }

    private static PassingReplayStatusV1 Status(ReplayRequest request, string state, DateTime reportedAt) =>
        new(request.RequestId, reportedAt, state, request.Read, request.Published, NewInAgentStore: 0, request.Reason);

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
