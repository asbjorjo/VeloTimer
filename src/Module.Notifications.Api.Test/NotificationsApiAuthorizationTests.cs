using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Data;
using VeloTime.Module.Notifications.Interface.Identity;
using VeloTime.Module.Notifications.Service;

namespace VeloTime.Module.Notifications.Api.Test;

[Trait("Category", "PostgreSQL")]
public sealed class NotificationsApiAuthorizationTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    [PostgresFact]
    public async Task PublicActiveIsAnonymousAndAdminRoutesRequireTheExistingPermission()
    {
        await database.ResetAsync();
        var now = DateTimeOffset.UtcNow;
        var publicNotice = await CreateNotice(database, now, new(NotificationAudienceKind.Everyone));
        _ = await CreateNotice(database, now, new(NotificationAudienceKind.Role, Role: "RaceOfficial"));

        using var factory = new NotificationsApiFactory(database);
        using var client = factory.CreateContractClient();
        using var anonymous = await client.GetAsync(NotificationRoutes.Active);
        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
        Assert.Equal("no-store", anonymous.Headers.CacheControl?.ToString());
        var payload = await anonymous.Content.ReadFromJsonAsync<ActiveNotificationsDTO>();
        Assert.NotNull(payload);
        Assert.Equal([publicNotice.Id], payload.Items.Select(item => item.Id));

        factory.Authenticate(client, new Claim("sub", "admin"));
        using var denied = await client.GetAsync(NotificationRoutes.Administration);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("application/problem+json", denied.Content.Headers.ContentType?.MediaType);

        factory.Authenticate(client, new Claim("sub", "admin"),
            new Claim(NotificationAuthorization.PermissionClaimType, NotificationAuthorization.GlobalAdministrationPermission));
        using var authorized = await client.GetAsync(NotificationRoutes.Administration);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        var adminPage = await authorized.Content.ReadFromJsonAsync<NotificationAdministrationPageDTO>();
        Assert.NotNull(adminPage);
        Assert.Equal(2, adminPage.Items.Count);
    }

    [PostgresFact]
    public async Task SignedJwtIdentityUsesExactIssuerSubjectRolesAndSid()
    {
        await database.ResetAsync();
        var now = DateTimeOffset.UtcNow;
        var exactUserNotice = await CreateNotice(database, now,
            new(NotificationAudienceKind.User, RecipientIssuer: NotificationsApiFactory.Issuer, RecipientSubject: "exact-subject"));
        var roleNotice = await CreateNotice(database, now, new(NotificationAudienceKind.Role, Role: "RaceOfficial"));
        _ = await CreateNotice(database, now,
            new(NotificationAudienceKind.User, RecipientIssuer: "https://issuer.example/Realm", RecipientSubject: "exact-subject"));

        using var factory = new NotificationsApiFactory(database);
        using var client = factory.CreateContractClient();
        factory.Authenticate(client, new("sub", "exact-subject"), new("role", "RaceOfficial"),
            new("roles", "TimingVolunteer"), new("sid", "trusted-keycloak-sso-session"));

        using var response = await client.GetAsync(NotificationRoutes.Active);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var active = await response.Content.ReadFromJsonAsync<ActiveNotificationsDTO>();
        Assert.NotNull(active);
        Assert.Equal(new[] { exactUserNotice.Id, roleNotice.Id }.Order(), active.Items.Select(item => item.Id).Order());

        using var dismissed = await client.PutAsJsonAsync(
            DismissPath(exactUserNotice.Id),
            new DismissNotificationRequest(NotificationDismissalScope.Session));
        Assert.Equal(HttpStatusCode.OK, dismissed.StatusCode);
        var state = await dismissed.Content.ReadFromJsonAsync<NotificationDismissalDTO>();
        Assert.NotNull(state?.CurrentSession);
        await using var db = database.CreateContext();
        var persisted = await db.SessionDismissals.SingleAsync();
        Assert.Equal(NotificationsApiFactory.Issuer, persisted.Issuer);
        Assert.Equal("exact-subject", persisted.Subject);
        Assert.Equal("trusted-keycloak-sso-session", persisted.LoginSessionId);
    }

    [PostgresFact]
    public async Task MissingSidAllowsReadsAndAccountScopesButSessionScopesReturn403WithoutPartialRestore()
    {
        await database.ResetAsync();
        var now = DateTimeOffset.UtcNow;
        var durationNotice = await CreateNotice(database, now);
        var permanentNotice = await CreateNotice(database, now);
        var sessionNotice = await CreateNotice(database, now);

        using var factory = new NotificationsApiFactory(database);
        using var client = factory.CreateContractClient();
        factory.Authenticate(client, new Claim("sub", "rider"),
            new Claim(NotificationAuthorization.PermissionClaimType, NotificationAuthorization.GlobalAdministrationPermission));

        using var activeBefore = await client.GetAsync(NotificationRoutes.Active);
        Assert.Equal(HttpStatusCode.OK, activeBefore.StatusCode);
        using var dismissalsBefore = await client.GetAsync(NotificationRoutes.Dismissals);
        Assert.Equal(HttpStatusCode.OK, dismissalsBefore.StatusCode);
        using var adminBefore = await client.GetAsync(NotificationRoutes.Administration);
        Assert.Equal(HttpStatusCode.OK, adminBefore.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Dismiss(client, durationNotice.Id, NotificationDismissalScope.Duration)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Dismiss(client, permanentNotice.Id, NotificationDismissalScope.Permanent)).StatusCode);

        factory.Authenticate(client, new Claim("sub", "rider"), new Claim("sid", "trusted-session"));
        Assert.Equal(HttpStatusCode.OK, (await Dismiss(client, sessionNotice.Id, NotificationDismissalScope.Session)).StatusCode);

        factory.Authenticate(client, new Claim("sub", "rider"));
        using var noSidDismiss = await Dismiss(client, sessionNotice.Id, NotificationDismissalScope.Session);
        Assert.Equal(HttpStatusCode.Forbidden, noSidDismiss.StatusCode);
        Assert.Equal(NotificationErrorCodes.LoginSessionRequired,
            (await noSidDismiss.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        using var currentSessionRestore = await client.DeleteAsync(RestorePath(sessionNotice.Id, "currentSession"));
        Assert.Equal(HttpStatusCode.Forbidden, currentSessionRestore.StatusCode);
        using var combinedRestore = await client.DeleteAsync(RestorePath(durationNotice.Id, "accountAndCurrentSession"));
        Assert.Equal(HttpStatusCode.Forbidden, combinedRestore.StatusCode);

        await using (var db = database.CreateContext())
        {
            Assert.Equal(2, await db.AccountDismissals.CountAsync());
            Assert.Single(await db.SessionDismissals.ToListAsync());
            Assert.Equal(NotificationDismissalScope.Duration,
                (await db.AccountDismissals.SingleAsync(row => row.NotificationId == durationNotice.Id)).Scope);
            Assert.Equal(NotificationDismissalScope.Permanent,
                (await db.AccountDismissals.SingleAsync(row => row.NotificationId == permanentNotice.Id)).Scope);
        }

        using var accountRestore = await client.DeleteAsync(RestorePath(durationNotice.Id, "account"));
        Assert.Equal(HttpStatusCode.OK, accountRestore.StatusCode);
        using var permanentRestore = await client.DeleteAsync(RestorePath(permanentNotice.Id, "account"));
        Assert.Equal(HttpStatusCode.OK, permanentRestore.StatusCode);
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.AccountDismissals.ToListAsync());
        Assert.Single(await verify.SessionDismissals.ToListAsync());
    }

    [PostgresFact]
    public async Task SameSidAcrossRefreshedTokensAndTabsSharesDismissalButDifferentSidIsIsolated()
    {
        await database.ResetAsync();
        var notice = await CreateNotice(database, DateTimeOffset.UtcNow);
        using var factory = new NotificationsApiFactory(database);
        using var firstTab = factory.CreateContractClient();
        factory.Authenticate(firstTab, new("sub", "rider"), new("sid", "same-sso-session"));

        using var dismissed = await Dismiss(firstTab, notice.Id, NotificationDismissalScope.Session);
        Assert.Equal(HttpStatusCode.OK, dismissed.StatusCode);
        using var refreshedTokenTab = factory.CreateContractClient();
        factory.Authenticate(refreshedTokenTab, new("sub", "rider"), new("sid", "same-sso-session"));
        using var hiddenForSameSession = await refreshedTokenTab.GetAsync(NotificationRoutes.Active);
        Assert.Equal(HttpStatusCode.OK, hiddenForSameSession.StatusCode);
        var sameSessionPage = await hiddenForSameSession.Content.ReadFromJsonAsync<ActiveNotificationsDTO>();
        Assert.NotNull(sameSessionPage);
        Assert.DoesNotContain(notice.Id, sameSessionPage.Items.Select(item => item.Id));

        using var otherSession = factory.CreateContractClient();
        factory.Authenticate(otherSession, new("sub", "rider"), new("sid", "different-sso-session"));
        using var visibleForOtherSession = await otherSession.GetAsync(NotificationRoutes.Active);
        Assert.Equal(HttpStatusCode.OK, visibleForOtherSession.StatusCode);
        var otherSessionPage = await visibleForOtherSession.Content.ReadFromJsonAsync<ActiveNotificationsDTO>();
        Assert.NotNull(otherSessionPage);
        Assert.Contains(notice.Id, otherSessionPage.Items.Select(item => item.Id));
    }

    private static Task<HttpResponseMessage> Dismiss(HttpClient client, Guid id, NotificationDismissalScope scope) =>
        client.PutAsJsonAsync(DismissPath(id), new DismissNotificationRequest(scope));

    private static string RestorePath(Guid id, string scope) =>
        $"{DismissPath(id)}?scope={scope}";

    private static string DismissPath(Guid id) =>
        NotificationRoutes.Dismissal.Replace("{id:guid}", id.ToString());

    private static async Task<NotificationAdministrationDTO> CreateNotice(
        PostgresFixture database, DateTimeOffset now, NotificationAudienceDTO? audience = null)
    {
        await using var db = database.CreateContext();
        var content = new NotificationContentDTO(new("Test notice", "English"), new("Testmelding", "Norsk"));
        var policy = new NotificationDismissalPolicyDTO(
            [NotificationDismissalScope.Duration, NotificationDismissalScope.Session, NotificationDismissalScope.Permanent],
            NotificationDismissalScope.Duration, 3600);
        return await new NotificationService(db, new TestClock(now)).CreateAsync(new(
            content, NotificationSeverity.Warning, audience ?? new(NotificationAudienceKind.Everyone),
            now.AddMinutes(-1), null, policy));
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
