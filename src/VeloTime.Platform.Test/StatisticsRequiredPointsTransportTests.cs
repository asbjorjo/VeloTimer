using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VeloTime.Module.Statistics.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Services;
using Contracts = VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.Platform.Test;

public sealed class StatisticsRequiredPointsTransportTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task ServerAndGeneratedClientPreserveReplacementNullAndClear(bool update, int shape)
    {
        var id = Guid.NewGuid();
        var item = Guid.NewGuid();
        var start = Guid.NewGuid();
        var end = Guid.NewGuid();
        var layout = Guid.NewGuid();
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid()];
        var requirements = shape == 0 ? null : new Contracts.StatisticsRequiredCoursePoints(shape == 1 ? layout : null, shape == 1 ? ids : []);
        var calls = 0;
        using var handler = new Callback(async request =>
        {
            Assert.Equal(update ? HttpMethod.Put : HttpMethod.Post, request.Method);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            if (shape == 0)
                Assert.True(!json.RootElement.TryGetProperty("requiredCoursePoints", out var property) || property.ValueKind == JsonValueKind.Null);
            else
            {
                var property = json.RootElement.GetProperty("requiredCoursePoints");
                Assert.Equal(shape == 1 ? layout.ToString() : null, property.GetProperty("courseLayoutId").GetString());
                Assert.Equal(shape == 1 ? ids : [], property.GetProperty("coursePointIds").EnumerateArray().Select(value => value.GetGuid()));
            }
            calls++;
            return new HttpResponseMessage(update ? HttpStatusCode.OK : HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new Contracts.StatisticsConfigurationDTO(id, item, Contracts.StatisticsConfigurationType.Simple,
                    start, end, null, null) { RequiredCoursePoints = shape == 1 ? requirements : null })
            };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://statistics.invalid") };
        var client = new StatisticsClient(http);
        using var cache = new WebUiCacheTestHost(client);
        await new StatisticsAdministrationService(client, cache.Cache).SaveConfigurationAsync(update ? id : null,
            new(item, Contracts.StatisticsConfigurationType.Simple, start, end, null, null) { RequiredCoursePoints = requirements });
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ServerReadsAndCachesIndependentOrderedSnapshots()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var layout = Guid.NewGuid();
        var calls = 0;
        var client = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((_, _) =>
        {
            calls++;
            return new List<StatisticsConfigurationDTO>
            {
                new() { Id = Guid.NewGuid(), StatisticsItemId = Guid.NewGuid(), Type = 0,
                    CoursePointStart = Guid.NewGuid(), CoursePointEnd = Guid.NewGuid(),
                    RequiredCoursePoints = new() { CourseLayoutId = layout, CoursePointIds = new[] { first, second } } }
            };
        });
        using var cache = new WebUiCacheTestHost(client);
        var service = new StatisticsAdministrationService(client, cache.Cache);
        var snapshot = Assert.Single(await service.GetConfigurationsAsync());
        Assert.Equal(layout, snapshot.RequiredCoursePoints!.CourseLayoutId);
        Assert.Equal(new[] { first, second }, snapshot.RequiredCoursePoints.CoursePointIds);
        ((Guid[])snapshot.RequiredCoursePoints.CoursePointIds!)[0] = Guid.NewGuid();
        Assert.Equal(new[] { first, second }, Assert.Single(await service.GetConfigurationsAsync()).RequiredCoursePoints!.CoursePointIds);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task BrowserSerializesAndReadsRequirementsWithoutDroppingOrder()
    {
        var requirements = new Contracts.StatisticsRequiredCoursePoints(Guid.NewGuid(), new[] { Guid.NewGuid(), Guid.NewGuid() });
        var config = new Contracts.StatisticsConfigurationDTO(Guid.NewGuid(), Guid.NewGuid(), Contracts.StatisticsConfigurationType.Simple,
            Guid.NewGuid(), Guid.NewGuid(), null, null) { RequiredCoursePoints = requirements };
        var calls = 0;
        using var handler = new Callback(async request =>
        {
            calls++;
            if (request.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { config }) };
            var body = await request.Content!.ReadFromJsonAsync<Contracts.SaveStatisticsConfigurationRequest>();
            Assert.Equal(requirements.CourseLayoutId, body!.RequiredCoursePoints!.CourseLayoutId);
            Assert.Equal(requirements.CoursePointIds, body.RequiredCoursePoints.CoursePointIds);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://browser.invalid") };
        var service = new StatisticsAdministrationHttpService(http);
        await service.SaveConfigurationAsync(config.Id, new(config.StatisticsItemId, config.Type, config.CoursePointStart, config.CoursePointEnd, null, null)
        { RequiredCoursePoints = requirements });
        Assert.Equal(requirements.CoursePointIds, Assert.Single(await service.GetConfigurationsAsync()).RequiredCoursePoints!.CoursePointIds);
        Assert.Equal(2, calls);
    }

    private sealed class Callback(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => callback(request);
    }
}
