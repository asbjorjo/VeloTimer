using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.Platform.Test;

public sealed partial class WebUiBffTests
{
    [Fact]
    public async Task PublicStatistics_BrowserTransportAndAnonymousBffRoundTripEveryFilterAndCursor()
    {
        var request = PublicStatisticsTransportTests.Request();
        var page = PublicStatisticsTransportTests.Page();
        var discovery = new PublicStatisticsDiscoveryDTO([], [], []);
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsService>((method, arguments) =>
        {
            if (method.Name == nameof(IStatisticsService.GetPublicDiscoveryAsync))
            {
                Assert.IsType<CancellationToken>(arguments[0]);
                return discovery;
            }
            Assert.Equal(nameof(IStatisticsService.GetPublicResultsAsync), method.Name);
            Assert.Equal(PublicStatisticsQuery.Normalize(request), arguments[0]);
            Assert.IsType<CancellationToken>(arguments[1]);
            return page;
        });
        await using var app = await CreateAppAsync(statistics);
        using var client = CreateClient(app, antiforgery: false);
        var browser = new StatisticsHttpService(client);
        Assert.Empty((await browser.GetPublicDiscoveryAsync()).Items);
        var result = await browser.GetPublicResultsAsync(request);
        Assert.Equal(page.Items, result.Items);
        Assert.Equal(page.Previous, result.Previous);
        Assert.Equal(page.Next, result.Next);
        using var form = new FormUrlEncodedContent(PublicStatisticsQuery.ToQuery(request));
        using var response = await client.GetAsync(PublicStatisticsQuery.ResultsPath + "?" + await form.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.TryGetProperty("totalCount", out _));
        using var discoveryResponse = await client.GetAsync(PublicStatisticsQuery.DiscoveryPath);
        Assert.True(discoveryResponse.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("name=a&name=b")]
    [InlineData("minSpeed=NaN")]
    [InlineData("minSpeed=-1")]
    [InlineData("minSpeed=20&maxSpeed=10")]
    [InlineData("minTime=")]
    [InlineData("maxTime=00%3A00%3A01.0001")]
    [InlineData("minTime=00%3A00%3A02.000&maxTime=00%3A00%3A01.000")]
    [InlineData("direction=next")]
    [InlineData("cursor=bad&direction=previous")]
    [InlineData("cursor=2026-10-31T08%3A47%3A54.1234567%2B00%3A00%7C40000000-0000-0000-0000-000000000004&direction=next")]
    [InlineData("direction=0")]
    [InlineData("pageSize=25")]
    [InlineData("offset=0")]
    public async Task PublicStatistics_BffRejectsInvalidQueriesBeforeCallingService(string invalid)
    {
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsService>((_, _) => throw new InvalidOperationException("must not call"));
        await using var app = await CreateAppAsync(statistics);
        using var client = CreateClient(app);
        var request = PublicStatisticsTransportTests.Request() with { Cursor = null, Direction = null };
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["facilityId"] = request.FacilityId.ToString("D"),
            ["itemId"] = request.StatisticsItemId.ToString("D"),
            ["configurationId"] = request.StatisticsItemConfigId.ToString("D")
        });
        using var response = await client.GetAsync(PublicStatisticsQuery.ResultsPath + "?" + await form.ReadAsStringAsync() + "&" + invalid);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(((PlatformServiceTests.TestProxy)(object)statistics).Calls);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task PublicStatistics_BffPreservesSafeUpstreamStatusWithoutLeakingDetails(int status)
    {
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsService>((_, _) =>
            throw new HttpRequestException("secret dependency payload", null, (HttpStatusCode)status));
        await using var app = await CreateAppAsync(statistics);
        using var client = CreateClient(app);
        using var response = await client.GetAsync(PublicStatisticsQuery.DiscoveryPath);
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
    }
}
