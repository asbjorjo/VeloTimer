using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Api.Test;

// Standard output-cache eligibility must be preserved by the named Relationships policy:
// authenticated/Authorization-bearing requests neither read nor populate the shared store and
// cookie-setting responses are never stored.
public sealed class FacilitiesOutputEligibilityTests(FacilitiesApiFactory factory) : IClassFixture<FacilitiesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AuthenticatedRequests_NeitherReadNorPopulateSharedOutputCache()
    {
        var route = await SeedRouteAsync();
        factory.OutputCache.Reset();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(route)).StatusCode);
        Assert.Equal(0, factory.OutputCache.GetCalls);
        Assert.Equal(0, factory.OutputCache.SetCalls);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, route, anonymous: true)).StatusCode);
        Assert.Equal(1, factory.OutputCache.SetCalls);
        var lookupsAfterWarm = factory.OutputCache.GetCalls;

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(route)).StatusCode);
        Assert.Equal(lookupsAfterWarm, factory.OutputCache.GetCalls);
        Assert.Equal(1, factory.OutputCache.SetCalls);
    }

    [Fact]
    public async Task AuthorizationHeaderWithoutAuthenticatedUser_IsNotOutputCached()
    {
        var route = await SeedRouteAsync();
        factory.OutputCache.Reset();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, route, anonymous: true, configure: request =>
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "opaque"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, factory.OutputCache.GetCalls);
        Assert.Equal(0, factory.OutputCache.SetCalls);
    }

    [Fact]
    public async Task CookieSettingResponse_IsNotStored()
    {
        var route = await SeedRouteAsync();
        factory.OutputCache.Reset();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, route, anonymous: true, configure: request =>
            request.Headers.Add(TestSetCookieStartupFilter.Header, "true"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(0, factory.OutputCache.SetCalls);
        Assert.Empty(factory.OutputCache.Stored);
    }

    [Fact]
    public async Task AnonymousRequestCookie_UsesStandardPublicEligibility()
    {
        // Characterization: the default policy does not exclude request cookies; the five
        // relationship routes do not read cookies, so the anonymous representation is shared.
        var route = await SeedRouteAsync();
        factory.OutputCache.Reset();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, route, anonymous: true)).StatusCode);
        Assert.Equal(1, factory.OutputCache.SetCalls);
        var lookups = factory.OutputCache.GetCalls;

        using var response = await SendAsync(client, route, anonymous: true, configure: request =>
            request.Headers.Add("Cookie", "session=anonymous-visitor"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(lookups + 1, factory.OutputCache.GetCalls);
        Assert.Equal(1, factory.OutputCache.SetCalls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    private async Task<string> SeedRouteAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
        var point = new CoursePoint { Id = Guid.NewGuid(), Name = "Eligibility", TimingPoint = Guid.NewGuid() };
        storage.Add(point);
        await storage.SaveChangesAsync();
        return $"/api/facilities/coursepoint/{point.Id}";
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, bool anonymous,
        Action<HttpRequestMessage>? configure = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (anonymous) request.Headers.Add("X-Test-Anonymous", "true");
        configure?.Invoke(request);
        return await client.SendAsync(request);
    }
}

// Test-only outer middleware: emits Set-Cookie before the app pipeline when requested, so the
// production output-cache middleware observes a cookie-setting response.
public sealed class TestSetCookieStartupFilter : IStartupFilter
{
    public const string Header = "X-Test-Set-Cookie";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (context.Request.Headers.ContainsKey(Header))
                context.Response.Headers.SetCookie = "test-cookie=1; Path=/";
            await nextMiddleware(context);
        });
        next(app);
    };
}
