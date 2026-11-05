using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Statistics.Client;
using VeloTime.WebUI.Mud.Client.Components;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;
using VeloTime.WebUI.Mud.Services;
using Contracts = VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.Platform.Test;

public sealed class HallOfFameTests
{
    [Fact]
    public void ProductionHost_LoadsApplicationScopedCssBundle()
    {
        var markup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "App.razor"));
        Assert.Contains("<link href=@Assets[\"VeloTime.WebUI.Mud.styles.css\"] rel=\"stylesheet\" />", markup);
    }

    [Theory]
    [InlineData(503, 503)]
    [InlineData(500, 502)]
    public async Task FacilityService_NormalizesUpstreamFailuresForRetryUi(int upstream, int expected)
    {
        var facilities = PlatformServiceTests.TestProxy.Create<IFacilitiesClient>((_, _) =>
            throw new VeloTime.Module.Facilities.Client.ApiException("secret", upstream, "secret", null!, null!));
        var timing = PlatformServiceTests.TestProxy.Create<VeloTime.Module.Timing.Client.ITimingClient>((_, _) =>
            throw new InvalidOperationException("No timing call expected"));
        using var cache = new WebUiCacheTestHost(facilities);
        var service = new FacilityService(facilities, timing, cache.Cache);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetFacilitiesAsync(CancellationToken.None));
        Assert.Equal((HttpStatusCode)expected, error.StatusCode);
        Assert.IsType<VeloTime.Module.Facilities.Client.ApiException>(error.InnerException);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Theory]
    [InlineData("lap")]
    [InlineData("200m")]
    [InlineData("1000m")]
    [InlineData("3000m")]
    [InlineData("4000m")]
    [InlineData("laps")]
    public async Task GeneratedClient_AllFacilitiesOmitsFacilityParameter(string category)
    {
        using var handler = new GlobalRequestHandler(category);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://statistics.invalid/") };
        var facilities = PlatformServiceTests.TestProxy.Create<IFacilitiesClient>((_, _) => throw new InvalidOperationException("No results"));
        using var cache = new WebUiCacheTestHost(facilities);
        var result = await new StatisticsService(new StatisticsClient(http), cache.Cache).GetHallOfFameAsync(new(category));
        Assert.True(result.IsAvailable);
        Assert.Equal(1, handler.Requests);
    }

    private sealed class GlobalRequestHandler(string category) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Equal(Contracts.HallOfFameQuery.Path, request.RequestUri!.AbsolutePath);
            Assert.Equal("?category=" + category, request.RequestUri.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"isAvailable\":true,\"times\":[],\"laps\":[]}", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    public async Task ServerTransport_MapsSiAndResolvesEachDistinctFacilityOnce()
    {
        var facilityId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((method, args) =>
        {
            Assert.Equal(nameof(IStatisticsClient.GetHallOfFameAsync), method.Name);
            Assert.Equal("lap", args[0]);
            Assert.Equal(facilityId.ToString("D"), args[1]);
            Assert.Equal(cancellation.Token, args[2]);
            return new HallOfFameDTO
            {
                IsAvailable = true, Laps = [],
                Times = [new() { ProfileName = "First", Duration = "00:00:20", SpeedMetresPerSecond = 12.5, FacilityId = facilityId },
                    new() { ProfileName = "Second", Duration = "00:00:25", SpeedMetresPerSecond = 10, FacilityId = facilityId }]
            };
        });
        var facilities = PlatformServiceTests.TestProxy.Create<IFacilitiesClient>((method, args) =>
        {
            Assert.Equal("Facility2Async", method.Name);
            Assert.Equal(facilityId, args[0]);
            return new FacilityDTO { Id = facilityId, Name = "Arena" };
        });
        using var cache = new WebUiCacheTestHost(facilities);
        var result = await new StatisticsService(statistics, cache.Cache).GetHallOfFameAsync(new("lap", facilityId), cancellation.Token);
        Assert.Equal(12.5, result.Times[0].SpeedMetresPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(20), result.Times[0].Duration);
        Assert.All(result.Times, row => Assert.Equal("Arena", row.FacilityName));
        Assert.Single(((PlatformServiceTests.TestProxy)(object)facilities).Calls);
    }

    [Fact]
    public async Task ServerTransport_LapTotalsNeedNoFacilityLookups()
    {
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((_, _) =>
            new HallOfFameDTO { IsAvailable = true, Times = [], Laps = [new() { ProfileName = "Rider", LapCount = 100, TotalDistanceMetres = 25000 }] });
        var facilities = PlatformServiceTests.TestProxy.Create<IFacilitiesClient>((_, _) => throw new InvalidOperationException("No facility lookup"));
        using var cache = new WebUiCacheTestHost(facilities);
        var result = await new StatisticsService(statistics, cache.Cache).GetHallOfFameAsync(new("laps"));
        Assert.Equal(25000, Assert.Single(result.Laps).TotalDistanceMetres);
        Assert.Empty(((PlatformServiceTests.TestProxy)(object)facilities).Calls);
    }

    [Theory]
    [InlineData(409)]
    [InlineData(503)]
    public async Task ServerTransport_PreservesSafeErrorStatus(int status)
    {
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((_, _) =>
            throw new VeloTime.Module.Statistics.Client.ApiException("secret", status, "secret", null!, null!));
        var facilities = PlatformServiceTests.TestProxy.Create<IFacilitiesClient>((_, _) => throw new InvalidOperationException());
        using var cache = new WebUiCacheTestHost(facilities);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new StatisticsService(statistics, cache.Cache).GetHallOfFameAsync(new("laps")));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Theory]
    [InlineData(false, 40)]
    [InlineData(true, 30)]
    public async Task Table_SkeletonsPreserveTenRowsAndFixedHeaders(bool laps, int cells)
    {
        var html = await Render(new() { [nameof(HallOfFameTable.Loading)] = true, [nameof(HallOfFameTable.LapCounter)] = laps });
        Assert.Equal(10, Count(html, "class=\"skeleton-row\""));
        Assert.Equal(cells, Count(html, "mud-skeleton-text"));
        Assert.Contains("aria-busy=\"true\"", html);
        Assert.Contains("Loading Hall of Fame", html);
        Assert.Contains("scope=\"col\"", html);
        Assert.Equal(!laps, html.Contains(">Facility</th>"));
        Assert.DoesNotContain("No eligible", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Table_UsesFixedRowFootprintAndFormatsRequiredValues(bool laps)
    {
        var view = new HallOfFameView(true, laps ? [] : [new("Rider", TimeSpan.FromSeconds(20.123), 12.5, "Arena")],
            laps ? [new("Rider", 10, 2500)] : []);
        var html = await Render(new() { [nameof(HallOfFameTable.Results)] = view, [nameof(HallOfFameTable.LapCounter)] = laps });
        Assert.Equal(9, Count(html, "class=\"placeholder-row\""));
        Assert.Contains("Rider", html);
        Assert.Contains(laps ? "2,500 m" : "45.000 km/h", html);
        if (laps) Assert.DoesNotContain("Arena", html);
        else Assert.Contains("00:00:20.123", html);
        Assert.Contains("aria-busy=\"false\"", html);
    }

    private static int Count(string text, string value) => (text.Length - text.Replace(value, "").Length) / value.Length;

    private static async Task<string> Render(Dictionary<string, object?> parameters)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddMudServices();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<HallOfFameTable>(ParameterView.FromDictionary(parameters));
            return root.ToHtmlString();
        });
    }
}

public sealed partial class WebUiBffTests
{
    [Fact]
    public async Task HallOfFame_AnonymousBrowserBffRoundTripAndNoStore()
    {
        var facility = Guid.NewGuid();
        var expected = new HallOfFameView(true, [], [new("Rider", 15, 3750)]);
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsService>((method, args) =>
        {
            Assert.Equal(nameof(IStatisticsService.GetHallOfFameAsync), method.Name);
            Assert.Equal(new Contracts.HallOfFameRequest("laps", facility), args[0]);
            Assert.IsType<CancellationToken>(args[1]);
            return expected;
        });
        await using var app = await CreateAppAsync(statistics);
        using var client = CreateClient(app, antiforgery: false);
        var result = await new StatisticsHttpService(client).GetHallOfFameAsync(new("laps", facility));
        Assert.Equal(expected.Laps, result.Laps);
        using var response = await client.GetAsync($"{Contracts.HallOfFameQuery.Path}?category=laps&facilityId={facility}");
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("category=no")]
    [InlineData("category=lap&category=laps")]
    [InlineData("facilityId=")]
    [InlineData("facilityId=00000000-0000-0000-0000-000000000000")]
    [InlineData("limit=11")]
    public async Task HallOfFame_BffRejectsInvalidQueriesBeforeService(string query)
    {
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsService>((_, _) => throw new InvalidOperationException("must not call"));
        await using var app = await CreateAppAsync(statistics);
        using var client = CreateClient(app, antiforgery: false);
        using var response = await client.GetAsync(Contracts.HallOfFameQuery.Path + "?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(((PlatformServiceTests.TestProxy)(object)statistics).Calls);
    }
}
