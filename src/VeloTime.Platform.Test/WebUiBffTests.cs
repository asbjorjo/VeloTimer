using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VeloTime.WebUI.Mud;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;
using VeloTime.WebUI.Mud.Endpoints;

namespace VeloTime.Platform.Test;

public sealed class WebUiBffTests
{
    private static readonly Guid ResourceId = Guid.Parse("fa886b15-035b-43fe-984a-04c8ce566122");

    [Fact]
    public async Task StatisticsEndpoint_ForwardsPagination()
    {
        var cursor = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsService>((_, arguments) =>
        {
            Assert.Equal(cursor, arguments[0]);
            Assert.Equal(true, arguments[1]);
            Assert.Equal(25, arguments[2]);
            return new[] { new SampleView { TransponderLabel = "rider", StartPoint = "Start", EndPoint = "Finish" } };
        });
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(statistics);
        await using var app = builder.Build();
        app.MapGroup("/api").MapStatisticsEndpoints();
        await app.StartAsync();
        using var client = app.GetTestClient();

        var response = await client.GetAsync($"/api/statistics/samples?cursor={Uri.EscapeDataString(cursor.ToString("O"))}&isNextPage=true&pageSize=25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("rider", Assert.Single((await response.Content.ReadFromJsonAsync<SampleView[]>())!).TransponderLabel);
    }

    [Theory]
    [InlineData("facilities")]
    [InlineData("facilities/fa886b15-035b-43fe-984a-04c8ce566122/layouts")]
    [InlineData("layouts/fa886b15-035b-43fe-984a-04c8ce566122")]
    [InlineData("statistics/samples")]
    public async Task PublicReads_DoNotRequireLogin(string path)
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        using var response = await client.GetAsync("api/" + path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("timing/installations")]
    [InlineData("timing/installations/fa886b15-035b-43fe-984a-04c8ce566122")]
    [InlineData("facilities/fa886b15-035b-43fe-984a-04c8ce566122/installations")]
    [InlineData("profile/username")]
    [InlineData("profile/accounts")]
    [InlineData("antiforgery")]
    public async Task ProtectedReads_Return401WithoutRedirect(string path)
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        using var response = await client.GetAsync("api/" + path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("PUT", "timing/installations/fa886b15-035b-43fe-984a-04c8ce566122")]
    [InlineData("POST", "profile/accounts/google/link")]
    [InlineData("DELETE", "profile/accounts/google")]
    public async Task Mutations_RejectMissingAntiforgeryToken(string method, string path)
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app, antiforgery: false);
        await client.GetAsync("signin");
        using var request = new HttpRequestMessage(new HttpMethod(method), "api/" + path);
        request.Content = method == "PUT" ? JsonContent.Create(new InstallationView { Id = ResourceId })
            : method == "POST" ? JsonContent.Create("/profile") : null;
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task HttpServices_ReadAndMutateThroughCookieAuthenticatedBff()
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        await client.GetAsync("signin");
        var timing = new TimingHttpService(client);
        var profile = new ProfileHttpService(client);
        var facilities = new FacilityHttpService(client);

        Assert.Equal(ResourceId, Assert.Single(await timing.GetInstallationsAsync(CancellationToken.None)).Id);
        Assert.Equal(ResourceId, (await timing.GetInstallationAsync(ResourceId, CancellationToken.None)).Id);
        Assert.Single(await facilities.GetInstallationsAsync(ResourceId, CancellationToken.None));
        await timing.UpdateInstallationAsync(new InstallationView { Id = ResourceId, Description = "Edited" });
        Assert.Equal("rider", await profile.GetUsernameAsync());
        Assert.Empty(await profile.GetLinkedAccountsAsync());
        Assert.Equal("https://identity.example/link", await profile.GetLinkAccountAsync("/profile", "google"));
        await profile.DeleteLinkedAccountAsync("google");

        var calls = ((PlatformServiceTests.TestProxy)(object)app.Services.GetRequiredService<ITimingService>()).Calls;
        Assert.Contains(calls, call => call.Method.Name == nameof(ITimingService.UpdateInstallationAsync)
            && ((InstallationView)call.Arguments[0]!).Description == "Edited");
        var profileCalls = ((PlatformServiceTests.TestProxy)(object)app.Services.GetRequiredService<IProfileService>()).Calls;
        Assert.Contains(profileCalls, call => call.Method.Name == nameof(IProfileService.GetLinkAccountAsync)
            && Equals(call.Arguments[0], "http://localhost/profile"));
        Assert.Contains(profileCalls, call => call.Method.Name == nameof(IProfileService.DeleteLinkedAccountAsync));
        using var response = await client.GetAsync("api/profile/username");
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Update_RejectsMismatchedRouteId()
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        await client.GetAsync("signin");
        using var response = await client.PutAsJsonAsync($"api/timing/installations/{ResourceId}", new InstallationView { Id = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var service = (PlatformServiceTests.TestProxy)(object)app.Services.GetRequiredService<ITimingService>();
        Assert.Empty(service.Calls);
    }

    [Theory]
    [InlineData("https://outside.example/profile")]
    [InlineData("//outside.example/profile")]
    [InlineData("\\outside.example/profile")]
    [InlineData("javascript:alert(1)")]
    public async Task ProfileLink_RejectsExternalRedirects(string redirect)
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        await client.GetAsync("signin");
        using var response = await client.PostAsJsonAsync("api/profile/accounts/google/link", redirect);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=-1")]
    [InlineData("cursor=invalid")]
    [InlineData("isNextPage=invalid")]
    public async Task StatisticsEndpoint_RejectsInvalidPagination(string query)
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        using var response = await client.GetAsync("api/statistics/samples?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StatisticsHttpService_ForwardsPaginationAndReturnsSamples()
    {
        var cursor = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsService>((_, arguments) =>
        {
            Assert.Equal(cursor, arguments[0]);
            Assert.Equal(true, arguments[1]);
            Assert.Equal(17, arguments[2]);
            Assert.IsType<CancellationToken>(arguments[3]);
            return Array.Empty<SampleView>();
        });
        await using var app = await CreateAppAsync(statistics);
        using var client = CreateClient(app);
        Assert.Empty(await new StatisticsHttpService(client).GetSamplesAsync(cursor, true, 17));
    }

    [Theory]
    [InlineData(404, 404)]
    [InlineData(403, 403)]
    [InlineData(500, 502)]
    public async Task UpstreamFailures_ReturnSanitizedErrors(int upstreamStatus, int expectedStatus)
    {
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsService>((_, _) =>
            throw new VeloTime.Module.Statistics.Client.ApiException("secret", upstreamStatus, "secret upstream body", null!, null!));
        await using var app = await CreateAppAsync(statistics);
        using var client = CreateClient(app);
        using var response = await client.GetAsync("api/statistics/samples");
        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
        await Assert.ThrowsAsync<HttpRequestException>(() => new StatisticsHttpService(client).GetSamplesAsync(null, false, 10));
    }

    [Fact]
    public async Task AntiforgeryHandler_RejectsOffOriginRequests()
    {
        using var handler = new BffAntiforgeryHandler(new Uri("https://webui.example/")) { InnerHandler = new HttpClientHandler() };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DeleteAsync("https://outside.example/api/profile/accounts/google"));
    }

    private static async Task<WebApplication> CreateAppAsync(IStatisticsService? statistics = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Critical);
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options => options.EventsType = typeof(TestCookieEvents));
        builder.Services.AddTransient<TestCookieEvents>();
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        builder.Services.AddSingleton(statistics ?? PlatformServiceTests.TestProxy.Create<IStatisticsService>((_, _) => Array.Empty<SampleView>()));
        builder.Services.AddSingleton(PlatformServiceTests.TestProxy.Create<IFacilityService>((method, _) => method.Name switch
        {
            nameof(IFacilityService.GetFacilitiesAsync) => Array.Empty<FacilityView>(),
            nameof(IFacilityService.GetFacilityLayoutsAsync) => Array.Empty<CourseLayoutView>(),
            nameof(IFacilityService.GetCourseLayoutDetailAsync) => new CourseLayoutDetailView { Id = ResourceId, Segments = [] },
            nameof(IFacilityService.GetInstallationsAsync) => new[] { new InstallationView { Id = ResourceId } },
            _ => throw new NotSupportedException(method.Name)
        }));
        builder.Services.AddSingleton(PlatformServiceTests.TestProxy.Create<ITimingService>((method, _) => method.Name switch
        {
            nameof(ITimingService.GetInstallationsAsync) => new[] { new InstallationView { Id = ResourceId } },
            nameof(ITimingService.GetInstallationAsync) => new InstallationView { Id = ResourceId },
            nameof(ITimingService.UpdateInstallationAsync) => null,
            _ => throw new NotSupportedException(method.Name)
        }));
        builder.Services.AddSingleton(PlatformServiceTests.TestProxy.Create<IProfileService>((method, _) => method.Name switch
        {
            nameof(IProfileService.GetUsernameAsync) => "rider",
            nameof(IProfileService.GetLinkedAccountsAsync) => Array.Empty<LinkedAccountView>(),
            nameof(IProfileService.GetLinkAccountAsync) => "https://identity.example/link",
            nameof(IProfileService.DeleteLinkedAccountAsync) => null,
            _ => throw new NotSupportedException(method.Name)
        }));
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapGet("/signin", async (HttpContext context) =>
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "rider"), new Claim(ClaimTypes.Name, "rider")], "Cookies");
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Results.NoContent();
        });
        app.MapBffEndpoints();
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app, bool antiforgery = true)
    {
        var origin = new Uri("http://localhost/");
        var cookies = new CookieHandler { InnerHandler = app.GetTestServer().CreateHandler() };
        HttpMessageHandler handler = antiforgery ? new BffAntiforgeryHandler(origin) { InnerHandler = cookies } : cookies;
        return new HttpClient(handler) { BaseAddress = origin };
    }

    private sealed class CookieHandler : DelegatingHandler
    {
        private readonly CookieContainer cookies = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cookieHeader = cookies.GetCookieHeader(request.RequestUri!);
            if (!string.IsNullOrEmpty(cookieHeader))
            {
                request.Headers.Add("Cookie", cookieHeader);
            }
            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                foreach (var cookie in values)
                {
                    cookies.SetCookies(request.RequestUri!, cookie);
                }
            }

            return response;
        }
    }

    private sealed class TestCookieEvents() : CookieEvents(null!)
    {
        public override Task ValidatePrincipal(CookieValidatePrincipalContext context) => Task.CompletedTask;
    }
}