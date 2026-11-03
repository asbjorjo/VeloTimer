using System.Net;
using System.Net.Http.Json;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.Platform.Test;

public sealed partial class WebUiBffTests
{
    [Fact]
    public async Task HallOfFame_AnonymousBrowserBffRoundTripIsNoStore()
    {
        var expected = HallOfFameTransportTests.Results();
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsService>((method, args) =>
        {
            Assert.Equal(nameof(IStatisticsService.GetHallOfFameAsync), method.Name);
            Assert.IsType<CancellationToken>(Assert.Single(args));
            return expected;
        });
        await using var app = await CreateAppAsync(statistics);
        using var client = CreateClient(app, antiforgery: false);
        HallOfFameTransportTests.EqualResults(expected, await new StatisticsHttpService(client).GetHallOfFameAsync());
        using var response = await client.GetAsync("/api/statistics/hall-of-fame");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    [InlineData("timing-admin", HttpStatusCode.Forbidden)]
    [InlineData("facilities-admin", HttpStatusCode.Forbidden)]
    [InlineData("statistics-admin", HttpStatusCode.OK)]
    [InlineData("velotime-admin", HttpStatusCode.OK)]
    public async Task HallOfFame_BffReadAndWriteEnforceStatisticsPolicy(string? role, HttpStatusCode expected)
    {
        var calls = 0;
        var administration = PlatformServiceTests.TestProxy.Create<IStatisticsAdministrationService>((method, args) =>
        {
            calls++;
            var save = method.Name == nameof(IStatisticsAdministrationService.SaveHallOfFameSettingsAsync);
            if (save) Assert.Equal(17, Assert.IsType<SaveHallOfFameSettingsRequest>(args[0]).ExpectedVersion);
            return new HallOfFameSettingsDTO(save ? 18 : 17, []);
        });
        await using var app = await CreateAppAsync(administration: administration);
        using var client = CreateClient(app);
        if (role is not null) await client.GetAsync($"signin?role={role}");
        Assert.Equal(expected, (await client.GetAsync("/api/admin/statistics/hall-of-fame")).StatusCode);
        Assert.Equal(expected, (await client.PutAsJsonAsync("/api/admin/statistics/hall-of-fame",
            new SaveHallOfFameSettingsRequest(17, []))).StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? 2 : 0, calls);
    }

    [Fact]
    public async Task HallOfFame_BffBrowserPreserves409AndRequiresAntiforgery()
    {
        var calls = 0;
        var administration = PlatformServiceTests.TestProxy.Create<IStatisticsAdministrationService>((_, _) =>
        {
            calls++;
            throw new HttpRequestException("Settings changed. Reload before retrying.", null, HttpStatusCode.Conflict);
        });
        await using var app = await CreateAppAsync(administration: administration);
        using var client = CreateClient(app);
        await client.GetAsync("signin?role=statistics-admin");
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new StatisticsAdministrationHttpService(client).SaveHallOfFameSettingsAsync(new(0, [])));
        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.Equal(1, calls);
        using var unprotected = CreateClient(app, antiforgery: false);
        await unprotected.GetAsync("signin?role=statistics-admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await unprotected.PutAsJsonAsync("/api/admin/statistics/hall-of-fame",
            new SaveHallOfFameSettingsRequest(0, []))).StatusCode);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task HallOfFame_BffPropagatesExplicitPublicFailures(int status)
    {
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsService>((_, _) =>
            throw new HttpRequestException("Hall of Fame unavailable.", null, (HttpStatusCode)status));
        await using var app = await CreateAppAsync(statistics);
        using var client = CreateClient(app, antiforgery: false);
        using var response = await client.GetAsync("/api/statistics/hall-of-fame");
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }
}
