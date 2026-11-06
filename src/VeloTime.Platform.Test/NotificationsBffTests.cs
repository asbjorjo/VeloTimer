using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VeloTime.Module.Notifications.Client;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Data;
using VeloTime.WebUI.Mud.Endpoints;

namespace VeloTime.Platform.Test;

public sealed class NotificationsBffTests
{
    [Fact]
    public async Task ActiveAllowsActualAnonymousAndForwardsPageWithoutIdentity()
    {
        var now = DateTimeOffset.UtcNow;
        var api = PlatformServiceTests.TestProxy.Create<INotificationsClient>((method, args) =>
        {
            Assert.Equal(nameof(INotificationsClient.GetActiveNotificationsAsync), method.Name);
            Assert.Equal(100, args[0]); Assert.Equal("opaque", args[1]); Assert.IsType<CancellationToken>(args[2]);
            return new ActiveNotificationsDTO(now, now, now.AddMinutes(5), null, [], null);
        });
        await using var app = await App(api);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/api/notifications/active?pageSize=100&continuationToken=opaque");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("false", Assert.Single(response.Headers.GetValues("X-VeloTime-Authenticated")));
        Assert.DoesNotContain("login", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new NotificationSessionStatus(false, false),
            await client.GetFromJsonAsync<NotificationSessionStatus>("/api/notifications/session"));
        Assert.Single(((PlatformServiceTests.TestProxy)(object)api).Calls);
    }

    [Theory]
    [InlineData("Cookie", ".AspNetCore.Cookies=invalid")]
    [InlineData("Authorization", "Bearer invalid")]
    public async Task InvalidCredentialsDoNotDowngradeToPublic(string header, string value)
    {
        var api = PlatformServiceTests.TestProxy.Create<INotificationsClient>((_, _) => throw new Exception("Must not call"));
        await using var app = await App(api);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(header, value);
        using var response = await client.GetAsync("/api/notifications/active");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("notifications.authentication_required",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Empty(((PlatformServiceTests.TestProxy)(object)api).Calls);
    }

    [Fact]
    public async Task MutationRequiresAuthenticationAndCsrfAndForwardsOnlyScope()
    {
        var id = Guid.NewGuid();
        var api = PlatformServiceTests.TestProxy.Create<INotificationsClient>((method, args) =>
        {
            Assert.Equal(nameof(INotificationsClient.DismissNotificationAsync), method.Name);
            Assert.Equal(id, args[0]);
            Assert.Equal(new DismissNotificationRequest(NotificationDismissalScope.Session), args[1]);
            Assert.IsType<CancellationToken>(args[2]);
            return new NotificationDismissalDTO(id, DateTimeOffset.UtcNow, null, null);
        });
        await using var app = await App(api);
        using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync($"/api/notifications/{id}/dismissal", new DismissNotificationRequest(NotificationDismissalScope.Session))).StatusCode);
        var cookie = await SignIn(client, admin: false);
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(new NotificationSessionStatus(true, false),
            await client.GetFromJsonAsync<NotificationSessionStatus>("/api/notifications/session"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/notifications/{id}/dismissal", new DismissNotificationRequest(NotificationDismissalScope.Session))).StatusCode);
        using var csrf = await client.GetAsync("/csrf");
        var requestToken = await csrf.Content.ReadFromJsonAsync<string>();
        var antiCookie = Assert.Single(csrf.Headers.GetValues("Set-Cookie")).Split(';')[0];
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", cookie + "; " + antiCookie);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", requestToken);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/notifications/{id}/dismissal", new DismissNotificationRequest(NotificationDismissalScope.Session))).StatusCode);
        Assert.Single(((PlatformServiceTests.TestProxy)(object)api).Calls);
    }

    [Fact]
    public async Task AdministrationRequiresExactGlobalPermission()
    {
        var now = DateTimeOffset.UtcNow;
        var api = PlatformServiceTests.TestProxy.Create<INotificationsClient>((_, _) =>
            new NotificationAdministrationPageDTO(now, now, now.AddMinutes(5), [], null));
        await using var app = await App(api);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", await SignIn(client, admin: false));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/notifications")).StatusCode);
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", await SignIn(client, admin: true));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/notifications")).StatusCode);
        Assert.Single(((PlatformServiceTests.TestProxy)(object)api).Calls);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("keycloak-session", true)]
    public async Task SessionCapabilityUsesProtectedCookieClaimsOnly(string? sid, bool expected)
    {
        var api = PlatformServiceTests.TestProxy.Create<INotificationsClient>((_, _) => throw new Exception("Must not call"));
        await using var app = await App(api);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", await SignIn(client, false, sid));
        using var response = await client.GetAsync("/api/notifications/session?sid=browser-forged");
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal(new NotificationSessionStatus(true, expected),
            await response.Content.ReadFromJsonAsync<NotificationSessionStatus>());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, json.RootElement.EnumerateObject().Count());
        Assert.DoesNotContain("keycloak-session", json.RootElement.GetRawText());
        Assert.Empty(((PlatformServiceTests.TestProxy)(object)api).Calls);
    }

    [Fact]
    public async Task TypedProblemPreservesCodeAndStatusButNotSensitiveDetails()
    {
        var api = PlatformServiceTests.TestProxy.Create<INotificationsClient>((_, _) =>
            throw new ApiException<ProblemDetails>("secret", 409, "secret", new Dictionary<string, IEnumerable<string>>(),
                new ProblemDetails { Code = NotificationErrorCodes.SnapshotExpired, Detail = "private upstream payload" }, null));
        await using var app = await App(api);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/api/notifications/active");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains(NotificationErrorCodes.SnapshotExpired, json);
        Assert.DoesNotContain("private", json); Assert.DoesNotContain("secret", json);
    }

    private static async Task<string> SignIn(HttpClient client, bool admin, string? sid = null)
    {
        using var response = await client.GetAsync("/signin?admin=" + admin.ToString().ToLowerInvariant() +
            (sid is null ? "" : "&sid=" + Uri.EscapeDataString(sid)));
        return Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';')[0];
    }
    private static async Task<WebApplication> App(INotificationsClient api)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Critical);
        builder.Services.AddSingleton(api);
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            // Match production's OIDC default challenge: API endpoints must choose
            // cookie challenge explicitly instead of redirecting to the provider.
            options.DefaultChallengeScheme = "OidcMustNotBeChallenged";
        }).AddCookie(options =>
        {
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        builder.Services.AddAuthorization(options => options.AddPolicy("Notifications.Admin", policy =>
            policy.RequireAuthenticatedUser().RequireClaim("velotime_permission", "velotime.admin")));
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.UseAntiforgery();
        app.MapGet("/signin", async (HttpContext http, bool admin, string? sid) =>
        {
            var claims = new List<Claim> { new("sub", "rider"), new("iss", "https://identity.invalid"),
                new(ClaimTypes.NameIdentifier, "rider") };
            claims.Add(new("velotime_permission", admin ? "velotime.admin" : "velotime.facilities.admin"));
            if (sid is not null) claims.Add(new("sid", sid));
            await http.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies")));
            return Results.NoContent();
        });
        app.MapGet("/csrf", (HttpContext http, IAntiforgery csrf) => Results.Ok(csrf.GetAndStoreTokens(http).RequestToken));
        app.MapNotificationBffEndpoints();
        await app.StartAsync();
        return app;
    }
}
