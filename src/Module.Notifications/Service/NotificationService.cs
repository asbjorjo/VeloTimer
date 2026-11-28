using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Data;
using VeloTime.Module.Notifications.Interface.Identity;
using VeloTime.Module.Notifications.Mapping;
using VeloTime.Module.Notifications.Model;
using VeloTime.Module.Notifications.Storage;

namespace VeloTime.Module.Notifications.Service;

/// <summary>Manual-authoring entry point; no domain-event consumers or transport coupling.</summary>
public sealed class NotificationService(NotificationsDbContext db, TimeProvider clock)
{
    public async Task<NotificationAdministrationDTO> CreateAsync(CreateNotificationRequest request, CancellationToken ct = default)
    {
        var policy = request.DismissalPolicy ?? NotificationValidation.DefaultPolicy(request.Severity);
        NotificationValidation.Authoring(request.Content, request.Severity, request.Audience,
            request.StartsAtUtc, request.EndsAtUtc, policy);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = NotificationTimestamp.Persisted(clock.GetUtcNow());
        var n = new Notification
        {
            Id = Guid.NewGuid(), EnglishTitle = "", EnglishBody = "", NorwegianBokmalTitle = "",
            NorwegianBokmalBody = "", CreatedAtUtc = now, UpdatedAtUtc = now
        };
        NotificationMapper.Apply(n, request.Content, request.Severity, request.Audience,
            request.StartsAtUtc, request.EndsAtUtc, policy);
        db.Notifications.Add(n);
        await NoticeSlots(n, now, ct);
        Hint(now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return NotificationMapper.Administration(n);
    }

    public async Task<NotificationAdministrationDTO> GetAsync(Guid id, CancellationToken ct = default) =>
        NotificationMapper.Administration(await db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == id, ct)
            ?? throw new NotificationException(404, NotificationErrorCodes.NotFound));

    public async Task<NotificationAdministrationDTO> UpdateAsync(Guid id, UpdateNotificationRequest request, CancellationToken ct = default)
    {
        NotificationValidation.Authoring(request.Content, request.Severity, request.Audience,
            request.StartsAtUtc, request.EndsAtUtc, request.DismissalPolicy);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var n = await LockNotice(id, ct);
        Version(n, request.ExpectedVersion);
        var now = NotificationTimestamp.Persisted(clock.GetUtcNow());
        NotificationMapper.Apply(n, request.Content, request.Severity, request.Audience,
            request.StartsAtUtc, request.EndsAtUtc, request.DismissalPolicy);
        n.Version++;
        n.UpdatedAtUtc = now;
        await NoticeSlots(n, now, ct);
        Hint(now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return NotificationMapper.Administration(n);
    }

    public async Task<NotificationAdministrationDTO> EndAsync(Guid id, EndNotificationRequest request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var n = await LockNotice(id, ct);
        Version(n, request.ExpectedVersion);
        var now = NotificationTimestamp.Persisted(clock.GetUtcNow());
        if (n.EndsAtUtc is null || n.EndsAtUtc > now)
        {
            n.EndsAtUtc = now;
            n.UpdatedAtUtc = now;
            n.Version++;
            await NoticeSlots(n, now, ct);
            Hint(now);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return NotificationMapper.Administration(n);
    }

    public async Task<NotificationDismissalDTO> DismissAsync(Guid id, NotificationReaderIdentity identity,
        DismissNotificationRequest request, CancellationToken ct = default)
    {
        NotificationValidation.Identity(identity);
        NotificationValidation.Require(Enum.IsDefined(request.Scope), NotificationErrorCodes.InvalidPolicy);
        var sessionId = request.Scope == NotificationDismissalScope.Session
            ? NotificationValidation.SessionId(identity) : null;
        var currentSessionId = sessionId ?? (string.IsNullOrWhiteSpace(identity.LoginSessionId) ? null : identity.LoginSessionId);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var n = await LockNotice(id, ct);
        var now = NotificationTimestamp.Persisted(clock.GetUtcNow());
        Accessible(n, identity);
        NotificationValidation.Require(ScheduledActive(n, now), NotificationErrorCodes.NotFound, 404);
        NotificationValidation.Require(NotificationMapper.Policy(n).AllowedScopes.Contains(request.Scope),
            NotificationErrorCodes.ScopeNotAllowed, 403);
        var (account, session) = await LockDismissals(id, identity, currentSessionId, ct);
        var changed = false;
        if (request.Scope == NotificationDismissalScope.Session)
        {
            if (session is null)
            {
                session = new() { Id = Guid.NewGuid(), Issuer = identity.Issuer, Subject = identity.Subject,
                    LoginSessionId = sessionId!, NotificationId = id, DismissedAtUtc = now };
                db.SessionDismissals.Add(session);
                changed = true;
            }
        }
        else if (account is null || account.Scope != request.Scope ||
                 request.Scope == NotificationDismissalScope.Duration && account.DismissedUntilUtc <= now)
        {
            if (account is null)
            {
                account = new() { Id = Guid.NewGuid(), Issuer = identity.Issuer, Subject = identity.Subject, NotificationId = id };
                db.AccountDismissals.Add(account);
            }
            account.Scope = request.Scope;
            account.DismissedAtUtc = now;
            account.DismissedUntilUtc = request.Scope == NotificationDismissalScope.Duration
                ? now.AddSeconds(n.DismissalDurationSeconds!.Value) : null;
            await Slot(id, NotificationScheduledWorkKind.AccountDismissalExpiry, account.Id, account.DismissedUntilUtc, ct);
            changed = true;
        }
        if (changed)
        {
            Hint(now);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return State(id, clock.GetUtcNow(), account, session);
    }

    public async Task<RestoreNotificationDismissalDTO> RestoreAsync(Guid id, NotificationReaderIdentity identity,
        NotificationRestoreScope scope, CancellationToken ct = default)
    {
        NotificationValidation.Identity(identity);
        NotificationValidation.Require(Enum.IsDefined(scope), NotificationErrorCodes.InvalidRestoreScope);
        var sessionId = scope is NotificationRestoreScope.CurrentSession or NotificationRestoreScope.AccountAndCurrentSession
            ? NotificationValidation.SessionId(identity) : null;
        var currentSessionId = sessionId ?? (string.IsNullOrWhiteSpace(identity.LoginSessionId) ? null : identity.LoginSessionId);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var n = await LockNotice(id, ct);
        Accessible(n, identity);
        var (account, session) = await LockDismissals(id, identity, currentSessionId, ct);
        var changed = false;
        if (account is not null && scope is NotificationRestoreScope.Account or NotificationRestoreScope.AccountAndCurrentSession)
        {
            await Slot(id, NotificationScheduledWorkKind.AccountDismissalExpiry, account.Id, null, ct);
            db.AccountDismissals.Remove(account);
            account = null;
            changed = true;
        }
        if (session is not null && scope is NotificationRestoreScope.CurrentSession or NotificationRestoreScope.AccountAndCurrentSession)
        {
            await Slot(id, NotificationScheduledWorkKind.SessionDismissalExpiry, session.Id, null, ct);
            db.SessionDismissals.Remove(session);
            session = null;
            changed = true;
        }
        if (changed)
        {
            Hint(clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        var now = clock.GetUtcNow();
        var state = State(id, now, account, session);
        return new(state, ScheduledActive(n, now) && state.Account is null && state.CurrentSession is null);
    }

    private static NotificationDismissalDTO State(Guid id, DateTimeOffset now,
        NotificationAccountDismissal? account, NotificationSessionDismissal? session) =>
        new(id, now, NotificationMapper.Account(account, now), NotificationMapper.Session(session));

    private async Task<Notification> LockNotice(Guid id, CancellationToken ct) =>
        await db.Notifications.FromSqlInterpolated($"SELECT * FROM notifications.notification WHERE id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new NotificationException(404, NotificationErrorCodes.NotFound);

    private async Task<(NotificationAccountDismissal?, NotificationSessionDismissal?)> LockDismissals(
        Guid id, NotificationReaderIdentity identity, string? loginSessionId, CancellationToken ct)
    {
        // Notice lock fences absent-key inserts too. Source rows always precede slots.
        var a = await db.AccountDismissals.FromSqlInterpolated(
            $"SELECT * FROM notifications.account_dismissal WHERE notification_id = {id} AND issuer = {identity.Issuer} AND subject = {identity.Subject} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        var s = loginSessionId is null ? null : await db.SessionDismissals.FromSqlInterpolated(
                $"SELECT * FROM notifications.session_dismissal WHERE notification_id = {id} AND issuer = {identity.Issuer} AND subject = {identity.Subject} AND login_session_id = {loginSessionId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        return (a, s);
    }

    private async Task NoticeSlots(Notification n, DateTimeOffset now, CancellationToken ct)
    {
        var ended = n.EndsAtUtc <= now;
        await Slot(n.Id, NotificationScheduledWorkKind.NotificationStart, n.Id,
            !ended && n.StartsAtUtc > now ? n.StartsAtUtc : null, ct);
        await Slot(n.Id, NotificationScheduledWorkKind.NotificationEnd, n.Id,
            n.EndsAtUtc > now ? n.EndsAtUtc : null, ct);
    }

    private async Task Slot(Guid notificationId, NotificationScheduledWorkKind kind, Guid sourceId,
        DateTimeOffset? due, CancellationToken ct)
    {
        var row = await db.ScheduledWork.FromSqlInterpolated(
            $"SELECT * FROM notifications.scheduled_work WHERE notification_id = {notificationId} AND kind = {kind.ToString()} AND source_id = {sourceId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (due is null)
        {
            if (row is not null) db.ScheduledWork.Remove(row);
            return;
        }
        if (row is null)
        {
            row = new() { Id = Guid.NewGuid(), NotificationId = notificationId, Kind = kind, SourceId = sourceId };
            db.ScheduledWork.Add(row);
        }
        else row.Generation++;
        row.DueAtUtc = due.Value;
        row.CompletedAtUtc = null;
        row.ClaimId = null;
        row.ClaimedUntilUtc = null;
        row.AttemptCount = 0;
    }

    private void Hint(DateTimeOffset now) => db.RefreshOutbox.Add(new()
        { EventId = Guid.NewGuid(), CreatedAtUtc = now, AvailableAtUtc = now });

    private static void Version(Notification n, long version)
    {
        NotificationValidation.Require(version > 0, NotificationErrorCodes.VersionConflict);
        NotificationValidation.Require(n.Version == version, NotificationErrorCodes.VersionConflict, 409);
    }

    public static bool ScheduledActive(Notification n, DateTimeOffset now) =>
        n.StartsAtUtc <= now && (n.EndsAtUtc is null || now < n.EndsAtUtc);

    private static void Accessible(Notification n, NotificationReaderIdentity identity) =>
        NotificationValidation.Require(n.AudienceKind switch
        {
            NotificationAudienceKind.Everyone or NotificationAudienceKind.Authenticated => true,
            NotificationAudienceKind.Role => identity.Roles.Contains(n.AudienceRole, StringComparer.Ordinal),
            NotificationAudienceKind.User => n.RecipientIssuer == identity.Issuer && n.RecipientSubject == identity.Subject,
            _ => false
        }, NotificationErrorCodes.NotFound, 404);
}
