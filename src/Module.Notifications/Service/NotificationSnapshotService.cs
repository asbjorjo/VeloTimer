using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Data;
using VeloTime.Module.Notifications.Interface.Identity;
using VeloTime.Module.Notifications.Mapping;
using VeloTime.Module.Notifications.Model;
using VeloTime.Module.Notifications.Storage;

namespace VeloTime.Module.Notifications.Service;

public sealed class NotificationSnapshotService(NotificationsDbContext db, IDataProtectionProvider protection, TimeProvider clock)
{
    private readonly IDataProtector protector = protection.CreateProtector("Notifications.Snapshot.v1");

    public async Task<ActiveNotificationsDTO> ActiveAsync(NotificationReaderIdentity? identity,
        NotificationPageRequest request, CancellationToken ct = default)
    {
        var continuation = Prepare("active", identity, request);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var (snapshot, notices) = await Begin("active", identity, request, continuation, ct);
        var eligible = notices.Where(n => n.StartsAtUtc <= snapshot.AsOf && (n.EndsAtUtc == null || n.EndsAtUtc > snapshot.AsOf));
        if (identity is not null)
            eligible = eligible.Where(n => !db.AccountDismissals.Any(a => a.NotificationId == n.Id &&
                    a.Issuer == identity.Issuer && a.Subject == identity.Subject &&
                    (a.Scope == NotificationDismissalScope.Permanent || a.DismissedUntilUtc > snapshot.AsOf)) &&
                !db.SessionDismissals.Any(s => s.NotificationId == n.Id && s.Issuer == identity.Issuer &&
                    s.Subject == identity.Subject && s.LoginSessionId == identity.LoginSessionId));
        var rows = await Page(eligible, snapshot, ct);
        var now = Finish(snapshot);
        await tx.CommitAsync(ct);
        return new(now, snapshot.AsOf, snapshot.Expires, snapshot.NextChange,
            rows.Take(snapshot.Size).Select(NotificationMapper.Presentation).ToArray(), Next(rows, snapshot));
    }

    public async Task<NotificationAdministrationPageDTO> AdministrationAsync(NotificationReaderIdentity identity,
        NotificationPageRequest request, CancellationToken ct = default)
    {
        var continuation = Prepare("admin", identity, request);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var (snapshot, notices) = await Begin("admin", identity, request, continuation, ct);
        var rows = await Page(notices, snapshot, ct);
        var now = Finish(snapshot);
        await tx.CommitAsync(ct);
        return new(now, snapshot.AsOf, snapshot.Expires,
            rows.Take(snapshot.Size).Select(NotificationMapper.Administration).ToArray(), Next(rows, snapshot));
    }

    public async Task<NotificationDismissalsPageDTO> DismissalsAsync(NotificationReaderIdentity identity,
        NotificationPageRequest request, CancellationToken ct = default)
    {
        var continuation = Prepare("dismissals", identity, request);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var (snapshot, notices) = await Begin("dismissals", identity, request, continuation, ct);
        var accounts = db.AccountDismissals.AsNoTracking().Where(a => a.Issuer == identity.Issuer &&
            a.Subject == identity.Subject && (a.Scope == NotificationDismissalScope.Permanent || a.DismissedUntilUtc > snapshot.AsOf));
        var sessions = db.SessionDismissals.AsNoTracking().Where(s => s.Issuer == identity.Issuer &&
            s.Subject == identity.Subject && s.LoginSessionId == identity.LoginSessionId);
        var rows = await Page(notices.Where(n => accounts.Any(a => a.NotificationId == n.Id) ||
            sessions.Any(s => s.NotificationId == n.Id)), snapshot, ct);
        var ids = rows.Take(snapshot.Size).Select(n => n.Id).ToArray();
        var aRows = await accounts.Where(a => ids.Contains(a.NotificationId)).ToDictionaryAsync(a => a.NotificationId, ct);
        var sRows = await sessions.Where(s => ids.Contains(s.NotificationId)).ToDictionaryAsync(s => s.NotificationId, ct);
        var items = rows.Take(snapshot.Size).Select(n => new NotificationDismissalListItemDTO(
            NotificationMapper.Presentation(n), NotificationMapper.Account(aRows.GetValueOrDefault(n.Id), snapshot.AsOf),
            NotificationMapper.Session(sRows.GetValueOrDefault(n.Id)))).ToArray();
        var now = Finish(snapshot);
        await tx.CommitAsync(ct);
        return new(now, snapshot.AsOf, snapshot.Expires, items, Next(rows, snapshot));
    }

    private Snapshot? Prepare(string operation, NotificationReaderIdentity? identity, NotificationPageRequest request)
    {
        if (identity is not null) NotificationValidation.Identity(identity);
        NotificationValidation.Require(request.PageSize is >= 1 and <= NotificationLimits.MaximumPageSize,
            NotificationErrorCodes.InvalidPage);
        if (request.ContinuationToken is null) return null;
        Snapshot snapshot;
        try
        {
            if (request.ContinuationToken.Length > 16384) throw new FormatException();
            snapshot = JsonSerializer.Deserialize<Snapshot>(protector.Unprotect(request.ContinuationToken))
                ?? throw new FormatException();
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException or ArgumentException)
        {
            throw new NotificationException(400, NotificationErrorCodes.InvalidContinuation);
        }
        NotificationValidation.Require(snapshot.Operation == operation && snapshot.Size == request.PageSize &&
            snapshot.Principal == Principal(identity) && snapshot.Cursor is not null, NotificationErrorCodes.InvalidContinuation);
        NotificationValidation.Require(snapshot.Authorization == Authorization(identity) && clock.GetUtcNow() < snapshot.Expires,
            NotificationErrorCodes.SnapshotExpired, 409);
        return snapshot;
    }

    private async Task<(Snapshot, IQueryable<Notification>)> Begin(string operation, NotificationReaderIdentity? identity,
        NotificationPageRequest request, Snapshot? snapshot, CancellationToken ct)
    {
        // First SELECT establishes a repeatable-read MVCC snapshot; revision and rows are consistent.
        var revision = await db.SnapshotRevisions.Select(r => r.Revision).SingleAsync(ct);
        var now = clock.GetUtcNow();
        var notices = operation == "admin" ? db.Notifications.AsNoTracking() : Authorized(identity);
        if (snapshot is not null)
            NotificationValidation.Require(snapshot.Revision == revision && now < snapshot.Expires,
                NotificationErrorCodes.SnapshotExpired, 409);
        else
        {
            var deadline = operation == "active" ? await Deadline(notices, identity, now, ct)
                : operation == "dismissals" ? await SnoozeDeadline(notices, identity!, now, ct) : null;
            var expires = now.AddSeconds(NotificationLimits.MaximumSnapshotLifetimeSeconds);
            if (deadline < expires) expires = deadline.Value;
            snapshot = new(operation, request.PageSize, Principal(identity), Authorization(identity), revision, now, expires, deadline, null);
        }
        return (snapshot, notices);
    }

    private IQueryable<Notification> Authorized(NotificationReaderIdentity? identity)
    {
        var query = db.Notifications.AsNoTracking();
        if (identity is null) return query.Where(n => n.AudienceKind == NotificationAudienceKind.Everyone);
        var roles = identity.Roles.ToArray();
        return query.Where(n => n.AudienceKind == NotificationAudienceKind.Everyone ||
            n.AudienceKind == NotificationAudienceKind.Authenticated ||
            n.AudienceKind == NotificationAudienceKind.Role && roles.Contains(n.AudienceRole!) ||
            n.AudienceKind == NotificationAudienceKind.User && n.RecipientIssuer == identity.Issuer && n.RecipientSubject == identity.Subject);
    }

    private async Task<DateTimeOffset?> Deadline(IQueryable<Notification> notices, NotificationReaderIdentity? identity,
        DateTimeOffset now, CancellationToken ct)
    {
        var start = await notices.Where(n => n.StartsAtUtc > now && (n.EndsAtUtc == null || n.EndsAtUtc > n.StartsAtUtc))
            .Select(n => (DateTimeOffset?)n.StartsAtUtc).MinAsync(ct);
        var end = await notices.Where(n => n.EndsAtUtc > now).Select(n => n.EndsAtUtc).MinAsync(ct);
        var snooze = identity is null ? null : await SnoozeDeadline(notices, identity, now, ct);
        return new[] { start, end, snooze }.Where(t => t.HasValue).Min();
    }

    private Task<DateTimeOffset?> SnoozeDeadline(IQueryable<Notification> notices, NotificationReaderIdentity identity,
        DateTimeOffset now, CancellationToken ct) =>
        db.AccountDismissals.Where(a => a.Issuer == identity.Issuer && a.Subject == identity.Subject &&
            a.DismissedUntilUtc > now && notices.Any(n => n.Id == a.NotificationId))
            .Select(a => a.DismissedUntilUtc).MinAsync(ct);

    private static Task<List<Notification>> Page(IQueryable<Notification> notices, Snapshot snapshot, CancellationToken ct)
    {
        if (snapshot.Cursor is { } cursor) notices = notices.Where(n => n.Id.CompareTo(cursor) > 0);
        return notices.OrderBy(n => n.Id).Take(snapshot.Size + 1).ToListAsync(ct);
    }

    private DateTimeOffset Finish(Snapshot snapshot)
    {
        var now = clock.GetUtcNow();
        NotificationValidation.Require(now < snapshot.Expires, NotificationErrorCodes.SnapshotExpired, 409);
        return now;
    }

    private string? Next(List<Notification> rows, Snapshot snapshot) => rows.Count > snapshot.Size
        ? protector.Protect(JsonSerializer.Serialize(snapshot with { Cursor = rows[snapshot.Size - 1].Id })) : null;

    private static string Hash(string[] values) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(values)));
    private static string Principal(NotificationReaderIdentity? identity) => Hash(identity is null
        ? ["anonymous"] : [identity.Issuer, identity.Subject, identity.LoginSessionId]);
    private static string Authorization(NotificationReaderIdentity? identity) =>
        Hash(identity?.Roles.Order(StringComparer.Ordinal).Distinct(StringComparer.Ordinal).ToArray() ?? []);

    private sealed record Snapshot(string Operation, int Size, string Principal, string Authorization, long Revision,
        DateTimeOffset AsOf, DateTimeOffset Expires, DateTimeOffset? NextChange, Guid? Cursor);
}
