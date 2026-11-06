using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Messages;
using VeloTime.Module.Notifications.Model;
using VeloTime.Module.Notifications.Storage;

namespace VeloTime.Module.Notifications.Processor.Workers;

/// <summary>
/// Claims durable outbox and deadline rows using short PostgreSQL transactions. Publication is
/// at-least-once; both acknowledgements and deadline completion are fenced by claim ID/generation.
/// </summary>
public sealed class NotificationWorkDispatcher(
    NotificationsDbContext db,
    INotificationRefreshPublisher publisher,
    TimeProvider clock,
    IOptions<NotificationWorkerOptions> options,
    ILogger<NotificationWorkDispatcher> logger)
{
    private readonly NotificationWorkerOptions settings = options.Value;

    public async Task DispatchOnceAsync(CancellationToken cancellationToken)
    {
        await DispatchOutboxBatchAsync(cancellationToken);
        await ProcessScheduledBatchAsync(cancellationToken);
    }

    private async Task DispatchOutboxBatchAsync(CancellationToken cancellationToken)
    {
        var claims = await ClaimOutboxAsync(cancellationToken);
        db.ChangeTracker.Clear();

        foreach (var claim in claims)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await publisher.PublishAsync(claim.EventId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "Notifications refresh publication failed for event {EventId} on attempt {AttemptCount}.",
                    claim.EventId, claim.AttemptCount);
                try
                {
                    await RescheduleOutboxAsync(claim, cancellationToken);
                }
                catch (Exception retryException) when (retryException is not OperationCanceledException)
                {
                    logger.LogError(retryException,
                        "Could not persist retry time for Notifications event {EventId}; its lease will expire.",
                        claim.EventId);
                }

                continue;
            }

            try
            {
                await AcknowledgeOutboxAsync(claim, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // The broker may already have accepted the event. Leave the lease for recovery:
                // retrying after lease expiry is the intentional at-least-once behavior.
                logger.LogError(exception,
                    "Could not acknowledge published Notifications event {EventId}; it may be delivered again.",
                    claim.EventId);
            }
        }
    }

    private async Task<IReadOnlyList<OutboxClaim>> ClaimOutboxAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var rows = await db.RefreshOutbox.FromSqlInterpolated($"""
            SELECT * FROM notifications.refresh_outbox
            WHERE published_at_utc IS NULL
              AND available_at_utc <= {now}
              AND (claimed_until_utc IS NULL OR claimed_until_utc <= {now})
            ORDER BY available_at_utc, event_id
            LIMIT {settings.BatchSize}
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);

        var leaseUntil = now.AddSeconds(settings.ClaimLeaseSeconds);
        var claims = new List<OutboxClaim>(rows.Count);
        foreach (var row in rows)
        {
            row.ClaimId = Guid.NewGuid();
            row.ClaimedUntilUtc = leaseUntil;
            row.AttemptCount = row.AttemptCount == int.MaxValue ? int.MaxValue : row.AttemptCount + 1;
            claims.Add(new(row.EventId, row.ClaimId.Value, row.AttemptCount));
        }

        if (rows.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return claims;
    }

    private async Task RescheduleOutboxAsync(OutboxClaim claim, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var availableAt = now.Add(settings.RetryDelay(claim.AttemptCount));
        var changed = await db.RefreshOutbox
            .Where(row => row.EventId == claim.EventId && row.ClaimId == claim.ClaimId
                && row.PublishedAtUtc == null && row.ClaimedUntilUtc > now)
            .ExecuteUpdateAsync(update => update
                .SetProperty(row => row.AvailableAtUtc, availableAt)
                .SetProperty(row => row.ClaimId, (Guid?)null)
                .SetProperty(row => row.ClaimedUntilUtc, (DateTimeOffset?)null),
                cancellationToken);

        if (changed != 1)
        {
            logger.LogWarning(
                "Notifications event {EventId} retry was not rescheduled because claim {ClaimId} is no longer current.",
                claim.EventId, claim.ClaimId);
        }
    }

    private async Task AcknowledgeOutboxAsync(OutboxClaim claim, CancellationToken cancellationToken)
    {
        var publishedAt = clock.GetUtcNow();
        var changed = await db.RefreshOutbox
            .Where(row => row.EventId == claim.EventId && row.ClaimId == claim.ClaimId
                && row.PublishedAtUtc == null && row.ClaimedUntilUtc > publishedAt)
            .ExecuteUpdateAsync(update => update
                .SetProperty(row => row.PublishedAtUtc, publishedAt)
                .SetProperty(row => row.ClaimId, (Guid?)null)
                .SetProperty(row => row.ClaimedUntilUtc, (DateTimeOffset?)null),
                cancellationToken);

        if (changed != 1)
        {
            logger.LogWarning(
                "Published Notifications event {EventId} was not acknowledged because claim {ClaimId} is no longer current.",
                claim.EventId, claim.ClaimId);
        }
    }

    private async Task ProcessScheduledBatchAsync(CancellationToken cancellationToken)
    {
        var claims = await ClaimScheduledWorkAsync(cancellationToken);
        db.ChangeTracker.Clear();

        foreach (var claim in claims)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ProcessScheduledClaimAsync(claim, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Keep the claim until its lease expires. A later pass can safely retry, while
                // the source row and generation remain the authority for whether it is current.
                logger.LogError(exception,
                    "Notifications deadline processing failed for work {WorkId}, generation {Generation}, attempt {AttemptCount}.",
                    claim.Id, claim.Generation, claim.AttemptCount);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<IReadOnlyList<ScheduledWorkClaim>> ClaimScheduledWorkAsync(
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var rows = await db.ScheduledWork.FromSqlInterpolated($"""
            SELECT * FROM notifications.scheduled_work
            WHERE completed_at_utc IS NULL
              AND due_at_utc <= {now}
              AND (claimed_until_utc IS NULL OR claimed_until_utc <= {now})
            ORDER BY due_at_utc, id
            LIMIT {settings.BatchSize}
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);

        var leaseUntil = now.AddSeconds(settings.ClaimLeaseSeconds);
        var claims = new List<ScheduledWorkClaim>(rows.Count);
        foreach (var row in rows)
        {
            row.ClaimId = Guid.NewGuid();
            row.ClaimedUntilUtc = leaseUntil;
            row.AttemptCount = row.AttemptCount == int.MaxValue ? int.MaxValue : row.AttemptCount + 1;
            claims.Add(new(row.Id, row.NotificationId, row.Kind, row.SourceId,
                row.Generation, row.DueAtUtc, row.ClaimId.Value, row.AttemptCount));
        }

        if (rows.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return claims;
    }

    private async Task ProcessScheduledClaimAsync(
        ScheduledWorkClaim claim,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Match API mutation lock ordering: notification, dismissal source if applicable, then slot.
        var notification = await db.Notifications.FromSqlInterpolated($"""
            SELECT * FROM notifications.notification
            WHERE id = {claim.NotificationId}
            FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);

        NotificationAccountDismissal? account = null;
        NotificationSessionDismissal? session = null;
        if (claim.Kind == NotificationScheduledWorkKind.AccountDismissalExpiry)
        {
            account = await db.AccountDismissals.FromSqlInterpolated($"""
                SELECT * FROM notifications.account_dismissal
                WHERE id = {claim.SourceId} AND notification_id = {claim.NotificationId}
                FOR UPDATE
                """).SingleOrDefaultAsync(cancellationToken);
        }
        else if (claim.Kind == NotificationScheduledWorkKind.SessionDismissalExpiry)
        {
            session = await db.SessionDismissals.FromSqlInterpolated($"""
                SELECT * FROM notifications.session_dismissal
                WHERE id = {claim.SourceId} AND notification_id = {claim.NotificationId}
                FOR UPDATE
                """).SingleOrDefaultAsync(cancellationToken);
        }

        var slot = await db.ScheduledWork.FromSqlInterpolated($"""
            SELECT * FROM notifications.scheduled_work
            WHERE id = {claim.Id}
            FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);

        if (slot is null || slot.ClaimId != claim.ClaimId || slot.Generation != claim.Generation
            || slot.NotificationId != claim.NotificationId || slot.Kind != claim.Kind
            || slot.SourceId != claim.SourceId || slot.DueAtUtc != claim.DueAtUtc
            || slot.CompletedAtUtc is not null || slot.ClaimedUntilUtc is not { } leaseUntil
            || leaseUntil <= clock.GetUtcNow())
        {
            return;
        }

        var now = clock.GetUtcNow();
        var isCurrentDeadline = IsCurrentDeadline(notification, account, session, slot, now);
        slot.CompletedAtUtc = now;
        slot.ClaimId = null;
        slot.ClaimedUntilUtc = null;

        if (isCurrentDeadline)
        {
            // Time crossing changes active snapshots without mutating a notification or dismissal
            // entity. Bump the global cursor revision in this same transaction so continuations
            // cannot silently cross a start/end/expiry boundary.
            var revisionRows = await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE notifications.snapshot_revision SET revision = revision + 1 WHERE id = 1",
                cancellationToken);
            if (revisionRows != 1)
            {
                throw new InvalidOperationException("The Notifications snapshot revision singleton is missing.");
            }

            db.RefreshOutbox.Add(new NotificationRefreshOutbox
            {
                EventId = Guid.NewGuid(),
                CreatedAtUtc = now,
                AvailableAtUtc = now
            });
        }
        else if (slot.Kind == NotificationScheduledWorkKind.SessionDismissalExpiry)
        {
            // The schema reserves this kind, but session expiry has no trusted lifecycle feed.
            // Do not infer a login expiry or emit a misleading wake-up.
            logger.LogWarning(
                "Completed unsupported Notifications session-expiry slot {WorkId} without emitting a refresh.",
                slot.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static bool IsCurrentDeadline(
        Notification? notification,
        NotificationAccountDismissal? account,
        NotificationSessionDismissal? session,
        NotificationScheduledWork slot,
        DateTimeOffset now)
    {
        if (now < slot.DueAtUtc)
        {
            return false;
        }

        return slot.Kind switch
        {
            NotificationScheduledWorkKind.NotificationStart =>
                notification is not null
                && notification.StartsAtUtc == slot.DueAtUtc
                && (notification.EndsAtUtc is null
                    || notification.EndsAtUtc.Value > notification.StartsAtUtc
                        && now < notification.EndsAtUtc.Value),
            NotificationScheduledWorkKind.NotificationEnd =>
                notification is not null
                && notification.EndsAtUtc is { } endAt
                && endAt == slot.DueAtUtc
                && endAt > notification.StartsAtUtc,
            NotificationScheduledWorkKind.AccountDismissalExpiry =>
                account is not null
                && account.Id == slot.SourceId
                && account.NotificationId == slot.NotificationId
                && account.Scope == NotificationDismissalScope.Duration
                && account.DismissedUntilUtc == slot.DueAtUtc,
            // No session expiry jobs are authored without an agreed trusted ticket lifecycle.
            NotificationScheduledWorkKind.SessionDismissalExpiry => false,
            _ => false
        };
    }

    private sealed record OutboxClaim(Guid EventId, Guid ClaimId, int AttemptCount);

    private sealed record ScheduledWorkClaim(
        Guid Id,
        Guid NotificationId,
        NotificationScheduledWorkKind Kind,
        Guid SourceId,
        long Generation,
        DateTimeOffset DueAtUtc,
        Guid ClaimId,
        int AttemptCount);
}
