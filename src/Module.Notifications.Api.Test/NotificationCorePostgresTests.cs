using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Data;
using VeloTime.Module.Notifications.Interface.Identity;
using VeloTime.Module.Notifications.Model;
using VeloTime.Module.Notifications.Service;
using VeloTime.Module.Notifications.Storage;
using Xunit.Sdk;

namespace VeloTime.Module.Notifications.Api.Test;

[Trait("Category", "PostgreSQL")]
public sealed class NotificationCorePostgresTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private static readonly NotificationContentDTO Content = new(
        new("A test notice", "English body"),
        new("En testmelding", "Norsk tekst"));

    [Fact]
    public void DatabaseTestsUseTheCiPostgresOptInAndCannotSilentlySkipInGitHubActions()
    {
        Assert.Equal("NOTIFICATIONS_API_TEST_ADMIN_CONNECTION", PostgresFixture.ConfigurationKey);
        var databaseTests = new[] { typeof(NotificationCorePostgresTests), typeof(NotificationsApiAuthorizationTests) }
            .SelectMany(type => type.GetMethods(System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            .Where(method => method.ReturnType == typeof(Task)).ToArray();
        Assert.NotEmpty(databaseTests);
        Assert.All(databaseTests, method =>
            Assert.IsType<PostgresFactAttribute>(Assert.Single(method.GetCustomAttributes(typeof(FactAttribute), false))));

        if (string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresFixture.ConfigurationKey)),
                $"CI must set {PostgresFixture.ConfigurationKey}; otherwise core and API PostgreSQL tests skip.");
            Assert.Null(new PostgresFactAttribute().Skip);
        }
    }

    [Fact]
    public void EffectiveDefaultsAndValidationCodes_AreStable()
    {
        Assert.Equal((NotificationDismissalScope.Permanent, null),
            (NotificationValidation.DefaultPolicy(NotificationSeverity.Info).DefaultScope,
                NotificationValidation.DefaultPolicy(NotificationSeverity.Info).DurationSeconds));
        Assert.Equal((NotificationDismissalScope.Duration, NotificationLimits.WarningDurationSeconds),
            (NotificationValidation.DefaultPolicy(NotificationSeverity.Warning).DefaultScope,
                NotificationValidation.DefaultPolicy(NotificationSeverity.Warning).DurationSeconds));
        Assert.Equal((NotificationDismissalScope.Duration, NotificationLimits.ErrorDurationSeconds),
            (NotificationValidation.DefaultPolicy(NotificationSeverity.Error).DefaultScope,
                NotificationValidation.DefaultPolicy(NotificationSeverity.Error).DurationSeconds));

        var start = DateTimeOffset.UtcNow;
        start = start.AddTicks(-(start.Ticks % 10));
        var invalid = new[]
        {
            (Request(audience: new(NotificationAudienceKind.Role, Role: " ")), NotificationErrorCodes.InvalidAudience),
            (Request(audience: new(NotificationAudienceKind.User, RecipientIssuer: "issuer")), NotificationErrorCodes.InvalidAudience),
            (Request(policy: new([NotificationDismissalScope.Permanent, NotificationDismissalScope.Permanent],
                NotificationDismissalScope.Permanent, null)), NotificationErrorCodes.InvalidPolicy),
            (Request(policy: new([NotificationDismissalScope.Duration], NotificationDismissalScope.Duration, 0)),
                NotificationErrorCodes.InvalidDuration),
            (Request(policy: new([NotificationDismissalScope.Session], NotificationDismissalScope.Permanent, null)),
                NotificationErrorCodes.InvalidPolicy),
            (Request(start: start, end: start.AddTicks(1)), NotificationErrorCodes.InvalidSchedule),
            (Request(start: new DateTimeOffset(start.DateTime, TimeSpan.FromHours(2))), NotificationErrorCodes.InvalidSchedule),
            (Request(content: Content with { English = new("", "Body") }), NotificationErrorCodes.InvalidContent)
        };
        foreach (var (request, code) in invalid)
        {
            NotificationException? observed = null;
            try
            {
                NotificationValidation.Authoring(
                request.Content, request.Severity, request.Audience, request.StartsAtUtc, request.EndsAtUtc,
                request.DismissalPolicy ?? NotificationValidation.DefaultPolicy(request.Severity));
            }
            catch (NotificationException exception)
            {
                observed = exception;
            }
            Assert.True(observed is not null, $"Expected validation error {code} for {request}.");
            Assert.Equal(400, observed!.StatusCode);
            Assert.Equal(code, observed.Code);
        }
    }

    [PostgresFact]
    public async Task AudienceMatrix_IsExactAndAnonymousProjectionContainsNoTargetMetadata()
    {
        await database.ResetAsync();
        var now = PersistedNow();
        var publicNotice = await CreateAsync(database, now, audience: new(NotificationAudienceKind.Everyone));
        var authenticatedNotice = await CreateAsync(database, now, audience: new(NotificationAudienceKind.Authenticated));
        var roleNotice = await CreateAsync(database, now, audience: new(NotificationAudienceKind.Role, Role: "RaceOfficial"));
        var exactUserNotice = await CreateAsync(database, now,
            audience: new(NotificationAudienceKind.User, RecipientIssuer: "https://issuer.example/Realm", RecipientSubject: "Rider"));

        await using var db = database.CreateContext();
        var snapshots = Snapshot(db, now);
        var anonymous = await snapshots.ActiveAsync(null, new(100));
        Assert.Equal([publicNotice.Id], anonymous.Items.Select(item => item.Id));
        var serialized = JsonSerializer.Serialize(anonymous);
        Assert.DoesNotContain("recipientIssuer", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recipientSubject", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("audience", serialized, StringComparison.OrdinalIgnoreCase);

        var reader = Identity("https://issuer.example/Realm", "Rider", "login-1", "RaceOfficial");
        var authenticated = await snapshots.ActiveAsync(reader, new(100));
        Assert.Equal(new[] { publicNotice.Id, authenticatedNotice.Id, roleNotice.Id, exactUserNotice.Id }.Order(),
            authenticated.Items.Select(item => item.Id).Order());

        var wrongRole = await snapshots.ActiveAsync(Identity(reader.Issuer, "Other", "login-2", "raceofficial"), new(100));
        Assert.Contains(publicNotice.Id, wrongRole.Items.Select(item => item.Id));
        Assert.DoesNotContain(roleNotice.Id, wrongRole.Items.Select(item => item.Id));
        Assert.DoesNotContain(exactUserNotice.Id, wrongRole.Items.Select(item => item.Id));

        var wrongSubject = await snapshots.ActiveAsync(
            Identity(reader.Issuer, "rider", "login-4", "RaceOfficial"), new(100));
        Assert.Contains(roleNotice.Id, wrongSubject.Items.Select(item => item.Id));
        Assert.DoesNotContain(exactUserNotice.Id, wrongSubject.Items.Select(item => item.Id));

        var wrongIssuer = await snapshots.ActiveAsync(Identity("https://issuer.example/realm", reader.Subject, "login-3"), new(100));
        Assert.DoesNotContain(exactUserNotice.Id, wrongIssuer.Items.Select(item => item.Id));
        Assert.Contains(authenticatedNotice.Id, wrongIssuer.Items.Select(item => item.Id));
    }

    [PostgresFact]
    public async Task StartEndAndDurationBoundaries_AreInclusiveStartAndExclusiveEndAndSnoozeExpiry()
    {
        await database.ResetAsync();
        var now = PersistedNow();
        var startsNow = await CreateAsync(database, now);
        var endsNow = await CreateAsync(database, now.AddMinutes(-1), now);
        var snoozed = await CreateAsync(database, now.AddMinutes(-1),
            policy: new([NotificationDismissalScope.Duration], NotificationDismissalScope.Duration, 60));
        var identity = Identity("issuer", "subject", "login");
        await using (var db = database.CreateContext())
            await new NotificationService(db, new TestClock(now)).DismissAsync(snoozed.Id, identity,
                new(NotificationDismissalScope.Duration));

        await using (var db = database.CreateContext())
        {
            var page = await Snapshot(db, now).ActiveAsync(identity, new(100));
            Assert.Equal(TimeSpan.Zero, page.ServerNowUtc.Offset);
            Assert.Contains(startsNow.Id, page.Items.Select(item => item.Id));
            Assert.DoesNotContain(endsNow.Id, page.Items.Select(item => item.Id));
            Assert.DoesNotContain(snoozed.Id, page.Items.Select(item => item.Id));
            Assert.Equal(now.AddSeconds(60), page.NextChangeAtUtc);
        }

        var boundary = now.AddSeconds(60);
        await using (var db = database.CreateContext())
        {
            var page = await Snapshot(db, boundary).ActiveAsync(identity, new(100));
            Assert.Contains(snoozed.Id, page.Items.Select(item => item.Id));
        }
    }

    [PostgresFact]
    public async Task PersistedScheduleAndDurationExpiryAreUtcAtPostgreSqlPrecisionAndUseSiSeconds()
    {
        await database.ResetAsync();
        var now = PersistedNow();
        var requestedStart = now.AddTicks(9);
        var requestedEnd = requestedStart.AddSeconds(10).AddTicks(7);
        var created = await CreateAsync(database, requestedStart, requestedEnd,
            policy: new([NotificationDismissalScope.Duration], NotificationDismissalScope.Duration, 37));

        Assert.Equal(NotificationTimestamp.Persisted(requestedStart), created.StartsAtUtc);
        Assert.Equal(NotificationTimestamp.Persisted(requestedEnd), created.EndsAtUtc);
        Assert.Equal(TimeSpan.Zero, created.StartsAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, created.EndsAtUtc!.Value.Offset);
        Assert.Equal(37, created.DismissalPolicy.DurationSeconds);

        var identity = Identity("issuer", "subject", "login");
        await using var db = database.CreateContext();
        var dismissed = await new NotificationService(db, new TestClock(now))
            .DismissAsync(created.Id, identity, new(NotificationDismissalScope.Duration));
        Assert.Equal(now.AddSeconds(37), dismissed.Account!.DismissedUntilUtc);
        Assert.Equal(TimeSpan.Zero, dismissed.Account.DismissedUntilUtc!.Value.Offset);
    }

    [PostgresFact]
    public async Task DefaultInfoForbidsDurationButCompleteOverrideMayAllowIt_AndPolicyTighteningDoesNotReviveDismissedUuid()
    {
        await database.ResetAsync();
        var now = PersistedNow();
        var info = await CreateAsync(database, now, severity: NotificationSeverity.Info);
        Assert.Equal([NotificationDismissalScope.Session, NotificationDismissalScope.Permanent],
            info.DismissalPolicy.AllowedScopes);
        Assert.Equal(NotificationDismissalScope.Permanent, info.DismissalPolicy.DefaultScope);

        var identity = Identity("issuer", "subject", "login");
        await using (var db = database.CreateContext())
        {
            var service = new NotificationService(db, new TestClock(now));
            var denied = await Assert.ThrowsAsync<NotificationException>(() => service.DismissAsync(
                info.Id, identity, new(NotificationDismissalScope.Duration)));
            Assert.Equal(403, denied.StatusCode);
            Assert.Equal(NotificationErrorCodes.ScopeNotAllowed, denied.Code);
            await service.DismissAsync(info.Id, identity, new(NotificationDismissalScope.Permanent));
        }

        var overridden = await CreateAsync(database, now, severity: NotificationSeverity.Info,
            policy: new([NotificationDismissalScope.Duration, NotificationDismissalScope.Session],
                NotificationDismissalScope.Session, 30));
        Assert.Equal(NotificationDismissalScope.Session, overridden.DismissalPolicy.DefaultScope);
        Assert.Equal(30, overridden.DismissalPolicy.DurationSeconds);

        await using (var db = database.CreateContext())
        {
            var service = new NotificationService(db, new TestClock(now.AddSeconds(1)));
            var updated = await service.UpdateAsync(info.Id, new(
                info.Version, info.Content, info.Severity, info.Audience, info.StartsAtUtc, info.EndsAtUtc,
                new([NotificationDismissalScope.Session], NotificationDismissalScope.Session, null)));
            Assert.Equal(2, updated.Version);
            var active = await Snapshot(db, now.AddSeconds(1)).ActiveAsync(identity, new(100));
            Assert.DoesNotContain(info.Id, active.Items.Select(item => item.Id));
            var list = await Snapshot(db, now.AddSeconds(1)).DismissalsAsync(identity, new(100));
            Assert.Contains(list.Items, item => item.Notification.Id == info.Id &&
                item.Account?.Scope == NotificationDismissalScope.Permanent);
        }
    }

    [PostgresFact]
    public async Task EndingFutureNoticePreservesStartAndCreatesAnEmptyCancelledInterval()
    {
        await database.ResetAsync();
        var now = PersistedNow();
        var futureStart = now.AddHours(1);
        var notice = await CreateAsync(database, futureStart);
        await using var db = database.CreateContext();
        var ended = await new NotificationService(db, new TestClock(now))
            .EndAsync(notice.Id, new(notice.Version));
        Assert.Equal(futureStart, ended.StartsAtUtc);
        Assert.Equal(now, ended.EndsAtUtc);
        Assert.Equal(2, ended.Version);
        Assert.Empty(await db.ScheduledWork.Where(slot => slot.NotificationId == notice.Id).ToListAsync());
        var active = await Snapshot(db, now.AddHours(2)).ActiveAsync(null, new(100));
        Assert.DoesNotContain(notice.Id, active.Items.Select(item => item.Id));
    }

    [PostgresFact]
    public async Task DismissalLayers_AreIdempotentSessionIsolatedAndRestoredIndependently()
    {
        await database.ResetAsync();
        var now = PersistedNow();
        var notice = await CreateAsync(database, now, policy: new(
            [NotificationDismissalScope.Duration, NotificationDismissalScope.Session, NotificationDismissalScope.Permanent],
            NotificationDismissalScope.Duration, 3600));
        var first = Identity("https://issuer.example", "subject", "login-one");
        var second = Identity(first.Issuer, first.Subject, "login-two");
        NotificationDismissalDTO firstState;
        await using (var db = database.CreateContext())
        {
            var service = new NotificationService(db, new TestClock(now));
            firstState = await service.DismissAsync(notice.Id, first, new(NotificationDismissalScope.Duration));
            var duplicate = await new NotificationService(db, new TestClock(now.AddSeconds(2)))
                .DismissAsync(notice.Id, first, new(NotificationDismissalScope.Duration));
            Assert.Equal(firstState.Account, duplicate.Account);
        }

        await using (var db = database.CreateContext())
        {
            var service = new NotificationService(db, new TestClock(now.AddSeconds(2)));
            var session = await service.DismissAsync(notice.Id, first, new(NotificationDismissalScope.Session));
            Assert.NotNull(session.Account);
            Assert.NotNull(session.CurrentSession);
            var secondSession = await service.DismissAsync(notice.Id, second, new(NotificationDismissalScope.Session));
            Assert.Equal(session.Account, secondSession.Account); // Account choice is shared; only session state is login-scoped.
            Assert.NotNull(secondSession.CurrentSession);
            var restored = await service.RestoreAsync(notice.Id, first, NotificationRestoreScope.Account);
            Assert.Null(restored.Dismissal.Account);
            Assert.NotNull(restored.Dismissal.CurrentSession);
            Assert.False(restored.IsActive);
        }

        await using (var db = database.CreateContext())
        {
            Assert.Equal(2, await db.SessionDismissals.CountAsync());
            Assert.Empty(await db.AccountDismissals.ToListAsync());
            var restored = await new NotificationService(db, new TestClock(now.AddSeconds(3)))
                .RestoreAsync(notice.Id, first, NotificationRestoreScope.CurrentSession);
            Assert.True(restored.IsActive);
            Assert.Null(restored.Dismissal.CurrentSession);
            Assert.Single(await db.SessionDismissals.ToListAsync());
        }
    }

    [PostgresFact]
    public async Task ConcurrentLoginDismissals_DoNotOverwriteEachOther_AndWritesCreateAtomicHints()
    {
        await database.ResetAsync();
        var now = PersistedNow();
        var notice = await CreateAsync(database, now);
        var logins = Enumerable.Range(0, 8).Select(index => Identity("issuer", "parallel", $"login-{index}")).ToArray();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = 0;
        var writes = logins.Select(async identity =>
        {
            await using var db = database.CreateContext();
            await db.Database.OpenConnectionAsync();
            if (Interlocked.Increment(ref ready) == logins.Length) barrier.TrySetResult();
            await barrier.Task.WaitAsync(TimeSpan.FromSeconds(15));
            return await new NotificationService(db, new TestClock(now)).DismissAsync(
                notice.Id, identity, new(NotificationDismissalScope.Session));
        });
        var results = await Task.WhenAll(writes).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.All(results, result => Assert.NotNull(result.CurrentSession));

        await using var verify = database.CreateContext();
        Assert.Equal(logins.Length, await verify.SessionDismissals.CountAsync(row =>
            row.Issuer == "issuer" && row.Subject == "parallel" && row.NotificationId == notice.Id));
        Assert.Equal(logins.Length + 1, await verify.RefreshOutbox.CountAsync());
        Assert.Equal(logins.Length + 1, await verify.SnapshotRevisions.Select(row => row.Revision).SingleAsync());
    }

    [PostgresFact]
    public async Task KeysetPagesDrainEveryNotice_AndRevisionChangeRequiresSnapshotRestart()
    {
        await database.ResetAsync();
        var now = PersistedNow();
        var seed = Enumerable.Range(0, 103).Select(_ => Notice(now)).ToArray();
        await using (var db = database.CreateContext())
        {
            db.Notifications.AddRange(seed);
            await db.SaveChangesAsync();
        }

        var found = new List<Guid>();
        string? token = null;
        DateTimeOffset asOf = default;
        do
        {
            await using var db = database.CreateContext();
            var page = await Snapshot(db, now).ActiveAsync(null, new(17, token));
            if (token is null) asOf = page.AsOfUtc;
            else Assert.Equal(asOf, page.AsOfUtc);
            found.AddRange(page.Items.Select(item => item.Id));
            token = page.ContinuationToken;
        } while (token is not null);
        Assert.Equal(seed.Select(row => row.Id).Order(), found);

        await using var firstDb = database.CreateContext();
        var first = await Snapshot(firstDb, now).ActiveAsync(null, new(10));
        Assert.NotNull(first.ContinuationToken);
        _ = await CreateAsync(database, now); // Inserts behind the cursor and increments the shared revision.
        await using var continuedDb = database.CreateContext();
        var error = await Assert.ThrowsAsync<NotificationException>(() =>
            Snapshot(continuedDb, now).ActiveAsync(null, new(10, first.ContinuationToken)));
        Assert.Equal(409, error.StatusCode);
        Assert.Equal(NotificationErrorCodes.SnapshotExpired, error.Code);
    }

    [PostgresFact]
    public async Task FailedOutboxInsertRollsBackNoticeAndRevision()
    {
        await database.ResetAsync();
        await using (var connection = new Npgsql.NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE FUNCTION notifications.reject_test_refresh() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'injected test failure'; END $$;
                CREATE TRIGGER reject_test_refresh BEFORE INSERT ON notifications.refresh_outbox
                FOR EACH ROW EXECUTE FUNCTION notifications.reject_test_refresh();
                """;
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            await using var db = database.CreateContext();
            await Assert.ThrowsAsync<DbUpdateException>(() => new NotificationService(db, new TestClock(PersistedNow()))
                .CreateAsync(Request()));
            Assert.Empty(await db.Notifications.ToListAsync());
            Assert.Equal(0, await db.SnapshotRevisions.Select(row => row.Revision).SingleAsync());
            Assert.Empty(await db.RefreshOutbox.ToListAsync());
        }
        finally
        {
            await using var connection = new Npgsql.NpgsqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DROP TRIGGER IF EXISTS reject_test_refresh ON notifications.refresh_outbox;
                DROP FUNCTION IF EXISTS notifications.reject_test_refresh();
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public void SessionAssertion_IsBoundToLoginTokenIdentityAndExpiry()
    {
        using var key = RSA.Create(2048);
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var token = "validated test token";
        var assertion = NotificationSessionAssertion.Sign(key, "key-1", "webui", "notifications-api",
            "https://issuer", "subject", "login-1", token, now, now.AddMinutes(2), 120);
        var publicKeys = new Dictionary<string, string> { ["key-1"] = key.ExportSubjectPublicKeyInfoPem() };
        Assert.Equal("login-1", NotificationSessionAssertion.Verify(assertion, publicKeys, "webui",
            "notifications-api", "https://issuer", "subject", token, now, 120, 0));
        Assert.Throws<CryptographicException>(() => NotificationSessionAssertion.Verify(assertion, publicKeys, "webui",
            "notifications-api", "https://issuer", "subject", "different-token", now, 120, 0));
        Assert.Throws<CryptographicException>(() => NotificationSessionAssertion.Verify(assertion, publicKeys, "webui",
            "notifications-api", "https://issuer", "different-subject", token, now, 120, 0));
        Assert.Throws<CryptographicException>(() => NotificationSessionAssertion.Verify(assertion, publicKeys, "webui",
            "notifications-api", "https://issuer", "subject", token, now.AddMinutes(2), 120, 0));
    }

    private NotificationSnapshotService Snapshot(NotificationsDbContext db, DateTimeOffset now) =>
        new(db, database.DataProtection, new TestClock(now));

    private static NotificationReaderIdentity Identity(string issuer, string subject, string session, params string[] roles) =>
        new(issuer, subject, roles, session);

    private static DateTimeOffset PersistedNow()
    {
        var value = DateTimeOffset.UtcNow;
        return value.AddTicks(-(value.Ticks % 10));
    }

    private static CreateNotificationRequest Request(
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        NotificationAudienceDTO? audience = null,
        NotificationDismissalPolicyDTO? policy = null,
        NotificationContentDTO? content = null,
        NotificationSeverity severity = NotificationSeverity.Warning) =>
        new(content ?? Content, severity, audience ?? new(NotificationAudienceKind.Everyone),
            start ?? PersistedNow(), end, policy);

    private static Notification Notice(DateTimeOffset now)
    {
        var id = Guid.NewGuid();
        return new()
        {
            Id = id, Version = 1, EnglishTitle = "A test notice", EnglishBody = "English body",
            NorwegianBokmalTitle = "En testmelding", NorwegianBokmalBody = "Norsk tekst",
            Severity = NotificationSeverity.Info, AudienceKind = NotificationAudienceKind.Everyone,
            StartsAtUtc = now.AddMinutes(-1), AllowsSessionDismissal = true, AllowsPermanentDismissal = true,
            DefaultDismissalScope = NotificationDismissalScope.Permanent, CreatedAtUtc = now, UpdatedAtUtc = now
        };
    }

    private static async Task<NotificationAdministrationDTO> CreateAsync(
        PostgresFixture database,
        DateTimeOffset start,
        DateTimeOffset? end = null,
        NotificationAudienceDTO? audience = null,
        NotificationDismissalPolicyDTO? policy = null,
        NotificationSeverity severity = NotificationSeverity.Warning)
    {
        await using var db = database.CreateContext();
        return await new NotificationService(db, new TestClock(start))
            .CreateAsync(Request(start, end, audience, policy, severity: severity));
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
