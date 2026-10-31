using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Statistics.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Services;
using Contracts = VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.Platform.Test;

public sealed class PublicStatisticsTransportTests
{
    [Fact]
    public async Task ServerTransport_ForwardsAllInvariantValuesCancellationAndMapsLosslessResponseWithoutFacilityLookups()
    {
        var request = Request();
        var expected = Page();
        using var cancellation = new CancellationTokenSource();
        var values = Contracts.PublicStatisticsQuery.ToQuery(request);
        var client = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((method, args) =>
        {
            Assert.Equal("GetPublicStatisticsResultsAsync", method.Name);
            var keys = new[] { "facilityId", "itemId", "configurationId", "name", "minTime", "maxTime", "minSpeed", "maxSpeed", "cursor", "direction" };
            Assert.Equal(keys.Length + 1, args.Length);
            for (var index = 0; index < keys.Length; index++) Assert.Equal(values[keys[index]], args[index]);
            Assert.Equal(cancellation.Token, args[^1]);
            return GeneratedPage(expected);
        });
        var facilities = PlatformServiceTests.TestProxy.Create<IFacilitiesClient>((_, _) => throw new InvalidOperationException("No result-level lookup"));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new StatisticsService(client, facilities, cache);
        var actual = await service.GetPublicResultsAsync(request, cancellation.Token);
        Assert.Equal(expected.Items, actual.Items);
        Assert.Equal(expected.Previous, actual.Previous);
        Assert.Equal(expected.Next, actual.Next);
        Assert.Empty(((PlatformServiceTests.TestProxy)(object)facilities).Calls);
        await service.GetPublicResultsAsync(request, cancellation.Token);
        Assert.Equal(2, ((PlatformServiceTests.TestProxy)(object)client).Calls.Count);
    }

    [Fact]
    public async Task ServerTransport_DiscoveryMapsSelectableMultiAndUnavailableReasons()
    {
        using var cancellation = new CancellationTokenSource();
        var request = Request();
        var parent = Guid.NewGuid();
        var client = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((method, args) =>
        {
            Assert.Equal("GetPublicStatisticsDiscoveryAsync", method.Name);
            Assert.Equal(cancellation.Token, args[0]);
            return new PublicStatisticsDiscoveryDTO
            {
                Items = [new StatisticsItemDTO { Id = request.StatisticsItemId, Name = "Lap", Description = "250m", Distance = 250 }],
                Configurations = [new PublicStatisticsConfigurationDTO
                {
                    Id = request.StatisticsItemConfigId, StatisticsItemId = request.StatisticsItemId,
                    FacilityId = request.FacilityId, Type = (int)Contracts.StatisticsConfigurationType.Multi,
                    CoursePointStart = ResourceId(5), CoursePointStartName = "Start",
                    CoursePointEnd = ResourceId(6), CoursePointEndName = "", ParentConfigId = parent, Repetitions = 3
                }],
                UnavailableConfigurations = [new UnavailablePublicStatisticsConfigurationDTO
                {
                    Configuration = new StatisticsConfigurationDTO { Id = ResourceId(7), StatisticsItemId = request.StatisticsItemId,
                        Type = (int)Contracts.StatisticsConfigurationType.Simple, CoursePointStart = ResourceId(8), CoursePointEnd = ResourceId(9) },
                    Reason = (int)Contracts.PublicStatisticsConfigurationUnavailableReason.MissingCoursePoint
                }]
            };
        });
        var facilities = PlatformServiceTests.TestProxy.Create<IFacilitiesClient>((_, _) => throw new InvalidOperationException());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var actual = await new StatisticsService(client, facilities, cache).GetPublicDiscoveryAsync(cancellation.Token);
        Assert.Equal("Lap", Assert.Single(actual.Items).Name);
        var option = Assert.Single(actual.Configurations);
        Assert.Equal(Contracts.StatisticsConfigurationType.Multi, option.Type);
        Assert.Equal(parent, option.ParentConfigId);
        Assert.Equal(3, option.Repetitions);
        Assert.Equal("", option.CoursePointEndName);
        Assert.Equal(Contracts.PublicStatisticsConfigurationUnavailableReason.MissingCoursePoint,
            Assert.Single(actual.UnavailableConfigurations).Reason);
    }

    [Theory]
    [InlineData("duration")]
    [InlineData("cursor")]
    [InlineData("oversize")]
    public async Task ServerTransport_RejectsMalformedResponseWithoutRoundingOrAcceptingOversizePage(string kind)
    {
        var generated = GeneratedPage(Page());
        if (kind == "duration") generated.Items.First().Duration = "invalid";
        if (kind == "cursor") generated.Next!.TimeEnd = generated.Next.TimeEnd.AddTicks(1);
        if (kind == "oversize") generated.Items = Enumerable.Repeat(generated.Items.First(), 26).ToList();
        var client = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((_, _) => generated);
        var facilities = PlatformServiceTests.TestProxy.Create<IFacilitiesClient>((_, _) => throw new InvalidOperationException());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new StatisticsService(client, facilities, cache).GetPublicResultsAsync(Request()));
        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
    }

    [Theory]
    [InlineData(400, 400)]
    [InlineData(503, 503)]
    [InlineData(500, 502)]
    public async Task ServerTransport_PreservesSafeUpstreamErrors(int upstream, int expected)
    {
        var client = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((_, _) =>
            throw new VeloTime.Module.Statistics.Client.ApiException("secret", upstream, "secret body", null!, null!));
        var facilities = PlatformServiceTests.TestProxy.Create<IFacilitiesClient>((_, _) => throw new InvalidOperationException());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new StatisticsService(client, facilities, cache).GetPublicResultsAsync(Request()));
        Assert.Equal((HttpStatusCode)expected, error.StatusCode);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Fact]
    public async Task BrowserTransport_EncodesLiteralNameAndPropagatesCallerCancellation()
    {
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new CallbackHandler(async (message, token) =>
        {
            Assert.Equal(Contracts.PublicStatisticsQuery.ResultsPath, message.RequestUri!.AbsolutePath);
            Assert.Contains("%25", message.RequestUri.Query);
            Assert.Contains("%2B", message.RequestUri.Query);
            Assert.Contains("%26", message.RequestUri.Query);
            Assert.Contains("%7C", message.RequestUri.Query);
            started.SetResult(token);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://webui.invalid/") };
        using var cancellation = new CancellationTokenSource();
        var pending = new StatisticsHttpService(http).GetPublicResultsAsync(Request(), cancellation.Token);
        var handlerToken = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(handlerToken.IsCancellationRequested);
    }

    [Fact]
    public async Task BrowserTransport_RejectsInvalidTypedRequestBeforeSending()
    {
        using var handler = new CallbackHandler((_, _) => throw new InvalidOperationException("must not send"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://webui.invalid/") };
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new StatisticsHttpService(http).GetPublicResultsAsync(Request() with { MinSpeedKmh = double.NaN }));
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
    }

    internal static Contracts.GetPublicStatisticsResultsRequest Request() => new(
        ResourceId(1), ResourceId(2), ResourceId(3), "  O'Brien 50%_A+B & C\\D  ",
        TimeSpan.FromDays(1) + TimeSpan.FromMilliseconds(1234),
        TimeSpan.FromDays(2), 12.5, 72.123456789,
        new Contracts.PublicStatisticsCursorDTO(Timestamp(), ResourceId(4)), Contracts.PublicStatisticsPageDirection.Previous);
    internal static Contracts.PublicStatisticsResultsPageDTO Page()
    {
        var request = Request();
        var row = new Contracts.PublicStatisticsResultDTO(ResourceId(4), "Public rider", request.StatisticsItemId,
            request.StatisticsItemConfigId, request.FacilityId, TimeSpan.FromTicks(12_345_678), 37.123456789, Timestamp());
        return new([row], new(Timestamp().AddSeconds(1), ResourceId(10)), new(Timestamp(), row.Id));
    }
    private static Guid ResourceId(int value) => Guid.Parse($"{value:x8}-0000-0000-0000-000000000001");
    private static DateTimeOffset Timestamp() => new DateTimeOffset(2026, 10, 31, 8, 47, 54, TimeSpan.Zero).AddTicks(1_234_560);
    private static PublicStatisticsResultsPageDTO GeneratedPage(Contracts.PublicStatisticsResultsPageDTO page) => new()
    {
        Items = page.Items.Select(row => new PublicStatisticsResultDTO
        {
            Id = row.Id, ProfileName = row.ProfileName, StatisticsItemId = row.StatisticsItemId,
            StatisticsItemConfigId = row.StatisticsItemConfigId, FacilityId = row.FacilityId,
            Duration = row.Duration.ToString("c", System.Globalization.CultureInfo.InvariantCulture),
            SpeedKmh = row.SpeedKmh, TimeEnd = row.TimeEnd
        }).ToList(),
        Previous = page.Previous is null ? null : new PublicStatisticsCursorDTO { Id = page.Previous.Id, TimeEnd = page.Previous.TimeEnd },
        Next = page.Next is null ? null : new PublicStatisticsCursorDTO { Id = page.Next.Id, TimeEnd = page.Next.TimeEnd }
    };
    private sealed class CallbackHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            callback(request, cancellationToken);
    }
}
