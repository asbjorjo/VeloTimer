using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.Platform.Test;

public sealed partial class WebUiBffTests
{
    [Fact]
    public async Task OwnerNames_SelfEndpointNeverSerializesKeycloakNames()
    {
        var backend = PlatformServiceTests.TestProxy.Create<ITransponderOwnerService>((method, _) =>
        {
            Assert.Equal(nameof(ITransponderOwnerService.GetOwnProfileAsync), method.Name);
            return new OwnTransponderProfile(ResourceId, "Private rider");
        });
        await using var app = await CreateAppAsync(owners: backend);
        using var client = CreateClient(app);
        await client.GetAsync("signin?role=user");

        var response = await client.GetAsync($"api/transponder-owners/me?userId={Guid.NewGuid()}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Private rider", body);
        Assert.DoesNotContain("firstName", body);
        Assert.DoesNotContain("lastName", body);
        Assert.Equal(new OwnTransponderProfile(ResourceId, "Private rider"), await new TransponderOwnerHttpService(client).GetOwnProfileAsync());
    }

    [Theory]
    [InlineData("user", false)]
    [InlineData("facilities-admin", false)]
    [InlineData("statistics-admin", false)]
    [InlineData("timing-admin", true)]
    [InlineData("velotime-admin", true)]
    public async Task OwnerNames_SearchRequiresTimingButResolutionAllowsAllAdministrators(string role, bool allowed)
    {
        var backend = PlatformServiceTests.TestProxy.Create<ITransponderOwnerService>((method, _) => method.Name switch
        {
            nameof(ITransponderOwnerService.SearchAsync) => new TransponderOwnerPage([], null, null),
            nameof(ITransponderOwnerService.ResolveAsync) => (IReadOnlyList<TransponderOwnerView>)Array.Empty<TransponderOwnerView>(),
            _ => throw new NotSupportedException(method.Name)
        });
        await using var app = await CreateAppAsync(owners: backend);
        using var client = CreateClient(app);
        await client.GetAsync($"signin?role={role}");
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await client.GetAsync("api/admin/timing/transponder-owners/search?query=racer&profileOffset=0&keycloakOffset=0")).StatusCode);
        var canResolve = role != "user";
        Assert.Equal(canResolve ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("api/admin/timing/transponder-owners/resolve", new[] { ResourceId })).StatusCode);
        Assert.Equal((allowed ? 1 : 0) + (canResolve ? 1 : 0), ((PlatformServiceTests.TestProxy)(object)backend).Calls.Count);
    }

    [Fact]
    public async Task OwnerNames_HttpAdapterPreservesIndependentSearchOffsetsAndOwnerDetails()
    {
        var owner = new TransponderOwnerView(ResourceId, "Racer", "First", "Last", true);
        var backend = PlatformServiceTests.TestProxy.Create<ITransponderOwnerService>((method, arguments) =>
        {
            if (method.Name == nameof(ITransponderOwnerService.ResolveAsync))
            {
                Assert.Equal(ResourceId, Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<Guid>>(arguments[0])));
                return (IReadOnlyList<TransponderOwnerView>)new[] { owner };
            }
            Assert.Equal(nameof(ITransponderOwnerService.SearchAsync), method.Name);
            Assert.Equal("Racer & Name", arguments[0]);
            Assert.Null(arguments[1]);
            Assert.Equal(20, arguments[2]);
            return new TransponderOwnerPage([owner], null, 30);
        });
        await using var app = await CreateAppAsync(owners: backend);
        using var client = CreateClient(app);
        await client.GetAsync("signin?role=timing-admin");
        var service = new TransponderOwnerHttpService(client);

        var result = await service.SearchAsync("Racer & Name", null, 20);
        Assert.Equal(owner, Assert.Single(result.Owners));
        Assert.Null(result.NextProfileOffset);
        Assert.Equal(30, result.NextKeycloakOffset);
        Assert.Equal(owner, Assert.Single(await service.ResolveAsync([ResourceId])));
    }

    [Fact]
    public async Task OwnerNames_BatchLookupRequiresAntiforgery()
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app, antiforgery: false);
        await client.GetAsync("signin?role=timing-admin");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("api/admin/timing/transponder-owners/resolve", new[] { ResourceId })).StatusCode);
        Assert.Empty(((PlatformServiceTests.TestProxy)(object)app.Services.GetRequiredService(typeof(ITransponderOwnerService))!).Calls);
    }

    [Fact]
    public async Task OwnerNames_OutageIsNotDisplayedAsMissingProfile()
    {
        var backend = PlatformServiceTests.TestProxy.Create<ITransponderOwnerService>((_, _) =>
            throw new HttpRequestException("Statistics profile names could not be loaded. Check access and retry.", null, HttpStatusCode.ServiceUnavailable));
        await using var app = await CreateAppAsync(owners: backend);
        using var client = CreateClient(app);
        await client.GetAsync("signin?role=user");
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => new TransponderOwnerHttpService(client).GetOwnProfileAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Contains("could not be loaded", exception.Message);
        Assert.DoesNotContain("No statistics profile", exception.Message);
    }
}
