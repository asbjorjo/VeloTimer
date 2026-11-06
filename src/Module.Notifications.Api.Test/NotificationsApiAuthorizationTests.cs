using System.Security.Claims;
using System.Net;
using System.Net.Http.Json;
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

        factory.Principal = Principal(new Claim(ClaimTypes.NameIdentifier, "admin"));
        factory.ReaderIdentity = new("issuer", "admin", [], "login");
        using var denied = await client.GetAsync(NotificationRoutes.Administration);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("application/problem+json", denied.Content.Headers.ContentType?.MediaType);

        factory.Principal = Principal(
            new Claim(ClaimTypes.NameIdentifier, "admin"),
            new Claim(NotificationAuthorization.PermissionClaimType, NotificationAuthorization.GlobalAdministrationPermission));
        using var authorized = await client.GetAsync(NotificationRoutes.Administration);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        var adminPage = await authorized.Content.ReadFromJsonAsync<NotificationAdministrationPageDTO>();
        Assert.NotNull(adminPage);
        Assert.Equal(2, adminPage.Items.Count);
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "NotificationsTest"));

    private static async Task<NotificationAdministrationDTO> CreateNotice(
        PostgresFixture database, DateTimeOffset now, NotificationAudienceDTO audience)
    {
        var content = new NotificationContentDTO(
            new("Public test", "English"), new("Test offentlig", "Norsk"));
        await using var db = database.CreateContext();
        return await new NotificationService(db, new TestClock(now)).CreateAsync(new(
            content, NotificationSeverity.Warning, audience, now.AddSeconds(-1), null));
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
