using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VeloTime.Module.Profiles.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Localization;

namespace VeloTime.Platform.Test;

public sealed class LanguageSelectionEndpointTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task State_UsesBrowserCultureWhenPreferenceIsUnsetOrUnavailable(bool authenticated, bool unavailable)
    {
        var profiles = new RecordingProfilesClient
        {
            ReadFailure = unavailable ? new HttpRequestException("Profiles unavailable") : null
        };
        await using var app = await CreateApp(profiles);
        using var client = CreateClient(app);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("nn-NO, en;q=0.5");
        if (authenticated)
            await client.GetAsync("/signin");

        var state = await client.GetFromJsonAsync<LanguageState>("/language/state");

        Assert.Equal("nb-NO", state!.Language);
        Assert.Equal(unavailable, state.PreferenceUnavailable);
        Assert.Equal(authenticated ? 1 : 0, profiles.Reads.Count);
        if (!unavailable)
        {
            await SelectAnonymous(client, "en", "/profile");
            state = await client.GetFromJsonAsync<LanguageState>("/language/state");
            Assert.Equal("en", state!.Language);
        }
    }

    [Fact]
    public async Task AnonymousSelection_IsAntiforgeryProtected_AndSavedOnlyInAnonymousCookie()
    {
        var profiles = new RecordingProfilesClient();
        await using var app = await CreateApp(profiles);
        using var client = CreateClient(app);
        using var unprotectedClient = CreateRawClient(app);

        var missingToken = await unprotectedClient.PostAsJsonAsync("/language/select",
            new { language = "nb-NO", returnUrl = "/profile" });
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        Assert.Empty(profiles.Reads);
        Assert.Empty(profiles.Writes);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/language/select")
        {
            Content = JsonContent.Create(new { language = "nb-NO", returnUrl = "/profile?tab=preferences" })
        };
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/profile?tab=preferences",
            (await response.Content.ReadFromJsonAsync<RedirectResponse>())!.RedirectUrl);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("velotime.language=nb-NO", StringComparison.Ordinal));
        Assert.Empty(profiles.Reads);
        Assert.Empty(profiles.Writes);
        Assert.Empty(KeycloakProfileCalls(app));

        var state = await client.GetFromJsonAsync<LanguageState>("/language/state");
        Assert.Equal("nb-NO", state!.Language);
        Assert.False(state.PreferenceUnavailable);
    }

    [Fact]
    public async Task AuthenticatedSelection_WritesProfileBeforeReturningAndClearsAnonymousPreference()
    {
        var write = new TaskCompletionSource<VeloTime.Module.Profiles.Client.LanguagePreferenceDTO>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var profiles = new RecordingProfilesClient
        {
            OnWrite = (_, _) => write.Task,
            Language = "en"
        };
        await using var app = await CreateApp(profiles);
        using var client = CreateClient(app);
        await SelectAnonymous(client, "en", "/");
        await client.GetAsync("/signin");

        using var request = SelectionRequest("nb-NO", "/profile");
        var pendingResponse = client.SendAsync(request);
        await profiles.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(pendingResponse.IsCompleted);
        Assert.Equal("nb-NO", Assert.Single(profiles.Writes).Language);

        write.SetResult(new VeloTime.Module.Profiles.Client.LanguagePreferenceDTO { Language = "nb-NO" });
        using var response = await pendingResponse;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("velotime.language=", StringComparison.Ordinal) &&
                     (value.Contains("expires=", StringComparison.OrdinalIgnoreCase) || value.Contains("max-age=0", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("/profile", (await response.Content.ReadFromJsonAsync<RedirectResponse>())!.RedirectUrl);
        Assert.Equal("nb-NO", Assert.Single(profiles.Writes).Language);
        Assert.Empty(KeycloakProfileCalls(app));
    }

    [Fact]
    public async Task AuthenticatedState_UsesSavedPreferenceAndDistinguishesReadFailureFromUnset()
    {
        var profiles = new RecordingProfilesClient { Language = "nb-NO" };
        await using var app = await CreateApp(profiles);
        using var client = CreateClient(app);
        await SelectAnonymous(client, "en", "/");
        await client.GetAsync("/signin");
        var state = await client.GetFromJsonAsync<LanguageState>("/language/state");
        Assert.Equal("nb-NO", state!.Language);
        Assert.False(state.PreferenceUnavailable);
        Assert.Single(profiles.Reads);

        profiles.ReadFailure = new HttpRequestException("private profile dependency detail", null,
            HttpStatusCode.ServiceUnavailable);
        state = await client.GetFromJsonAsync<LanguageState>("/language/state");
        Assert.Equal("en", state!.Language);
        Assert.True(state.PreferenceUnavailable);
        Assert.Equal(2, profiles.Reads.Count);
    }

    [Theory]
    [InlineData("fr-FR", "/profile")]
    [InlineData("en-US", "/profile")]
    [InlineData("nb-NO", "//evil.example/path")]
    [InlineData("nb-NO", "/\\evil.example/path")]
    [InlineData("nb-NO", "https://evil.example/path")]
    [InlineData("nb-NO", "/profile\\@evil.example")]
    [InlineData("nb-NO", "")]
    public async Task Selection_RejectsNoncanonicalCultureAndNonlocalReturnUrl(string language, string returnUrl)
    {
        var profiles = new RecordingProfilesClient();
        await using var app = await CreateApp(profiles);
        using var client = CreateClient(app);
        using var request = SelectionRequest(language, returnUrl);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(profiles.Writes);
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            value => value.StartsWith("velotime.language=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/profile")]
    [InlineData("/profile?tab=preferences&mode=compact")]
    [InlineData("/transponders/123e4567-e89b-12d3-a456-426614174000?page=2")]
    public async Task Selection_PreservesRootedLocalReturnPathAndQuery(string returnUrl)
    {
        var profiles = new RecordingProfilesClient();
        await using var app = await CreateApp(profiles);
        using var client = CreateClient(app);
        using var request = SelectionRequest("nb-NO", returnUrl);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(returnUrl, (await response.Content.ReadFromJsonAsync<RedirectResponse>())!.RedirectUrl);
        Assert.Empty(profiles.Writes);
    }

    [Theory]
    [InlineData("/profile#fragment")]
    [InlineData("/profile?tab=preferences#fragment")]
    [InlineData("/profile\nSet-Cookie: x=y")]
    [InlineData("/profile\r\nLocation: https://evil.example")]
    [InlineData("/profile\t")]
    [InlineData("/profile\0")]
    public async Task Selection_RejectsFragmentsAndControlCharacters(string returnUrl)
    {
        var profiles = new RecordingProfilesClient();
        await using var app = await CreateApp(profiles);
        using var client = CreateClient(app);
        using var request = SelectionRequest("nb-NO", returnUrl);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(profiles.Writes);
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            value => value.StartsWith("velotime.language=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailedAuthenticatedWrite_ReturnsUnavailableAndDoesNotDeleteBrowserPreference()
    {
        var profiles = new RecordingProfilesClient
        {
            WriteFailure = new HttpRequestException("private service failure", null, HttpStatusCode.ServiceUnavailable)
        };
        await using var app = await CreateApp(profiles);
        using var client = CreateClient(app);
        await SelectAnonymous(client, "en", "/");
        await client.GetAsync("/signin");
        using var request = SelectionRequest("nb-NO", "/profile");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(profiles.Writes);
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            value => value.StartsWith("velotime.language=", StringComparison.Ordinal));
    }

    private static async Task<WebApplication> CreateApp(RecordingProfilesClient profiles)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Critical);
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        builder.Services.AddSingleton<IProfilesClient>(profiles);
        builder.Services.AddSingleton<IProfileService>(PlatformServiceTests.TestProxy.Create<IProfileService>(
            (_, _) => throw new InvalidOperationException("Language preferences must not use the Keycloak account service.")));
        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapGet("/api/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(antiforgery.GetAndStoreTokens(context).RequestToken)).AllowAnonymous();
        app.MapGet("/signin", async (HttpContext context) =>
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test-subject")],
                CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));
            return Results.NoContent();
        }).AllowAnonymous();
        app.MapLanguageSelectionEndpoints();
        await app.StartAsync();
        return app;
    }

    private static IReadOnlyList<PlatformServiceTests.ProxyCall> KeycloakProfileCalls(WebApplication app) =>
        ((PlatformServiceTests.TestProxy)(object)app.Services.GetRequiredService<IProfileService>()).Calls;

    private static HttpClient CreateClient(WebApplication app)
    {
        var cookies = new CookieHandler { InnerHandler = app.GetTestServer().CreateHandler() };
        var origin = new Uri("http://localhost/");
        return new HttpClient(new BffAntiforgeryHandler(origin) { InnerHandler = cookies }) { BaseAddress = origin };
    }

    private static HttpClient CreateRawClient(WebApplication app)
    {
        var cookies = new CookieHandler { InnerHandler = app.GetTestServer().CreateHandler() };
        return new HttpClient(cookies) { BaseAddress = new Uri("http://localhost/") };
    }

    private static async Task SelectAnonymous(HttpClient client, string language, string returnUrl)
    {
        using var request = SelectionRequest(language, returnUrl);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static HttpRequestMessage SelectionRequest(string language, string returnUrl)
    {
        return new HttpRequestMessage(HttpMethod.Post, "/language/select")
        {
            Content = JsonContent.Create(new { language, returnUrl })
        };
    }

    private sealed record RedirectResponse(string RedirectUrl);
    private sealed record LanguageState(string Language, bool PreferenceUnavailable);

    private sealed class CookieHandler : DelegatingHandler
    {
        private readonly CookieContainer _cookies = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var cookieHeader = _cookies.GetCookieHeader(uri);
            if (!string.IsNullOrEmpty(cookieHeader))
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                foreach (var header in values)
                    _cookies.SetCookies(uri, header);
            }
            return response;
        }
    }

    private sealed class RecordingProfilesClient : IProfilesClient
    {
        public string? Language { get; set; }
        public Exception? ReadFailure { get; set; }
        public Exception? WriteFailure { get; init; }
        public Func<UpdateLanguagePreferenceRequest, CancellationToken,
            Task<VeloTime.Module.Profiles.Client.LanguagePreferenceDTO>>? OnWrite { get; init; }
        public List<CancellationToken> Reads { get; } = [];
        public List<UpdateLanguagePreferenceRequest> Writes { get; } = [];
        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<VeloTime.Module.Profiles.Client.LanguagePreferenceDTO> GetCurrentLanguagePreferenceAsync() =>
            GetCurrentLanguagePreferenceAsync(CancellationToken.None);

        public Task<VeloTime.Module.Profiles.Client.LanguagePreferenceDTO> GetCurrentLanguagePreferenceAsync(
            CancellationToken cancellationToken)
        {
            Reads.Add(cancellationToken);
            return ReadFailure is null
                ? Task.FromResult(new VeloTime.Module.Profiles.Client.LanguagePreferenceDTO { Language = Language })
                : Task.FromException<VeloTime.Module.Profiles.Client.LanguagePreferenceDTO>(ReadFailure);
        }

        public Task<VeloTime.Module.Profiles.Client.LanguagePreferenceDTO> UpdateLanguagePreferenceAsync(
            VeloTime.Module.Profiles.Client.UpdateLanguagePreferenceRequest body) =>
            UpdateLanguagePreferenceAsync(body, CancellationToken.None);

        public Task<VeloTime.Module.Profiles.Client.LanguagePreferenceDTO> UpdateLanguagePreferenceAsync(
            VeloTime.Module.Profiles.Client.UpdateLanguagePreferenceRequest body, CancellationToken cancellationToken)
        {
            Writes.Add(body);
            WriteStarted.TrySetResult();
            if (WriteFailure is not null)
                return Task.FromException<VeloTime.Module.Profiles.Client.LanguagePreferenceDTO>(WriteFailure);
            if (OnWrite is not null)
                return OnWrite(body, cancellationToken);
            Language = body.Language;
            return Task.FromResult(new VeloTime.Module.Profiles.Client.LanguagePreferenceDTO { Language = Language });
        }
    }
}
