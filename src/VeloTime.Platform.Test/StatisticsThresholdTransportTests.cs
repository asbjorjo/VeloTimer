using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VeloTime.Module.Statistics.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Services;
using Contracts = VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.Platform.Test;

public sealed class StatisticsThresholdTransportTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ServerAdapterDoesNotDropItemOrSimpleMultiDurationFields(bool update, bool clear)
    {
        var id = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        TimeSpan? min = clear ? null : TimeSpan.FromTicks(123456780);
        TimeSpan? max = clear ? null : TimeSpan.FromTicks(234567890);
        var minText = min?.ToString("c");
        var maxText = max?.ToString("c");
        using var cancellation = new CancellationTokenSource();
        var itemCall = 0;
        var configCall = 0;
        var client = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((method, args) =>
        {
            Assert.Equal(cancellation.Token, args[^1]);
            if (method.Name.Contains("StatisticsItem"))
            {
                Assert.Equal(update ? "UpdateStatisticsItemAsync" : "CreateStatisticsItemAsync", method.Name);
                var request = Assert.IsType<SaveStatisticsItemRequest>(args[update ? 1 : 0]);
                Assert.Equal(minText, request.MinDuration);
                Assert.Equal(maxText, request.MaxDuration);
                Assert.Equal(250, request.Distance);
                itemCall++;
                return new StatisticsItemDTO { Id = id, Name = "Lap", Description = "", Distance = 250,
                    MinDuration = minText, MaxDuration = maxText };
            }
            Assert.Equal(update ? "UpdateStatisticsConfigurationAsync" : "CreateStatisticsConfigurationAsync", method.Name);
            var body = Assert.IsType<SaveStatisticsConfigurationRequest>(args[update ? 1 : 0]);
            Assert.Equal(minText, body.MinDuration);
            Assert.Equal(maxText, body.MaxDuration);
            Assert.Equal(itemId, body.StatisticsItemId);
            configCall++;
            return new StatisticsConfigurationDTO { Id = id, StatisticsItemId = body.StatisticsItemId, Type = body.Type,
                CoursePointStart = body.CoursePointStart, CoursePointEnd = body.CoursePointEnd,
                ParentConfigId = body.ParentConfigId, Repetitions = body.Repetitions, MinDuration = minText, MaxDuration = maxText };
        });
        using var cache = new WebUiCacheTestHost(client);
        var service = new StatisticsAdministrationService(client, cache.Cache);
        await service.SaveItemAsync(update ? id : null, new("Lap", "", 250, min, max), cancellation.Token);
        await service.SaveConfigurationAsync(update ? id : null, new(itemId, Contracts.StatisticsConfigurationType.Simple,
            Guid.NewGuid(), Guid.NewGuid(), null, null, min, max), cancellation.Token);
        await service.SaveConfigurationAsync(update ? id : null, new(itemId, Contracts.StatisticsConfigurationType.Multi,
            null, null, Guid.NewGuid(), 2, min, max), cancellation.Token);
        Assert.Equal(1, itemCall);
        Assert.Equal(2, configCall);
    }

    [Fact]
    public async Task ServerAdapterReadsExactCanonicalBoundsAndCachesAdminMaps()
    {
        var id = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var min = TimeSpan.FromTicks(123456780);
        var max = TimeSpan.FromTicks(234567890);
        var client = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((method, _) => method.Name switch
        {
            "ListStatisticsItemsAsync" => new List<StatisticsItemDTO>
            {
                new() { Id = itemId, Name = "Lap", Distance = 250, MinDuration = min.ToString("c"), MaxDuration = max.ToString("c") }
            },
            "ListStatisticsConfigurationsAsync" => new List<StatisticsConfigurationDTO>
            {
                new() { Id = id, StatisticsItemId = itemId, Type = (int)Contracts.StatisticsConfigurationType.Multi,
                    ParentConfigId = Guid.NewGuid(), Repetitions = 2, MinDuration = min.ToString("c"), MaxDuration = max.ToString("c") }
            },
            _ => throw new NotSupportedException(method.Name)
        });
        using var cache = new WebUiCacheTestHost(client);
        var service = new StatisticsAdministrationService(client, cache.Cache);
        Assert.Equal(min, Assert.Single(await service.GetItemsAsync()).MinDuration);
        Assert.Equal(max, Assert.Single(await service.GetItemsAsync()).MaxDuration);
        Assert.Equal(min, Assert.Single(await service.GetConfigurationsAsync()).MinDuration);
        Assert.Equal(max, Assert.Single(await service.GetConfigurationsAsync()).MaxDuration);
    }

    [Fact]
    public async Task BrowserAdministrationSerializesExactBoundsAndExplicitNullsForAllSaveShapes()
    {
        var min = TimeSpan.FromTicks(123456780);
        var max = TimeSpan.FromTicks(234567890);
        var calls = 0;
        using var handler = new Callback(async request =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var expectedMin = calls < 3 ? min.ToString("c") : null;
            var expectedMax = calls < 3 ? max.ToString("c") : null;
            Assert.Equal(expectedMin, json.RootElement.GetProperty("minDuration").GetString());
            Assert.Equal(expectedMax, json.RootElement.GetProperty("maxDuration").GetString());
            Assert.Equal(calls < 3 ? HttpMethod.Post : HttpMethod.Put, request.Method);
            calls++;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://browser.invalid") };
        var service = new StatisticsAdministrationHttpService(client);
        var itemId = Guid.NewGuid();
        foreach (var clear in new[] { false, true })
        {
            Guid? id = clear ? Guid.NewGuid() : null;
            await service.SaveItemAsync(id, new("Lap", "", 250, clear ? null : min, clear ? null : max));
            await service.SaveConfigurationAsync(id, new(itemId, Contracts.StatisticsConfigurationType.Simple,
                Guid.NewGuid(), Guid.NewGuid(), null, null, clear ? null : min, clear ? null : max));
            await service.SaveConfigurationAsync(id, new(itemId, Contracts.StatisticsConfigurationType.Multi,
                null, null, Guid.NewGuid(), 2, clear ? null : min, clear ? null : max));
        }
        Assert.Equal(6, calls);
    }

    private sealed class Callback(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => callback(request);
    }
}
