using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class PublicStatisticsDiscoveryTests
{
    [Fact]
    public async Task Discovery_EmptyIsExplicitAndRejectsUnsupportedParameters()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        using var client = Anonymous(host);
        var empty = (await client.GetFromJsonAsync<PublicStatisticsDiscoveryDTO>(PublicStatisticsQuery.DiscoveryPath))!;
        Assert.Empty(empty.Items);
        Assert.Empty(empty.Configurations);
        Assert.Empty(empty.UnavailableConfigurations);
        using var malformed = await client.GetAsync(PublicStatisticsQuery.DiscoveryPath + "?name=ignored");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("application/problem+json", malformed.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("timing-admin")]
    [InlineData("facilities-admin")]
    public async Task Discovery_IsPublicButDoesNotGrantStatisticsAdministration(string role)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        using var client = host.App.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(PublicStatisticsQuery.DiscoveryPath)).StatusCode);
        foreach (var path in new[] { "/api/statistics/admin/items", "/api/statistics/admin/configurations" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync(path + "/" + Guid.NewGuid())).StatusCode);
        }
    }

    [Fact]
    public async Task Discovery_IsAnonymousAndResolvesSimpleMultiWithoutRecordedEntries()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap", Description = "Lap", Distance = 250 };
        var simple = Simple(item);
        var multi = new MultiStatisticsItemConfig
        {
            Id = Guid.NewGuid(), StatisticsItem = item, ParentConfig = simple, ParentConfigId = simple.Id, Repetitions = 3
        };
        await host.SeedAsync(simple, multi);
        using var client = Anonymous(host);
        using var response = await client.GetAsync(PublicStatisticsQuery.DiscoveryPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var discovery = (await response.Content.ReadFromJsonAsync<PublicStatisticsDiscoveryDTO>())!;
        Assert.Single(discovery.Items);
        Assert.Empty(discovery.UnavailableConfigurations);
        Assert.Equal(2, discovery.Configurations.Count);
        var direct = Assert.Single(discovery.Configurations, option => option.Type == StatisticsConfigurationType.Simple);
        var inherited = Assert.Single(discovery.Configurations, option => option.Type == StatisticsConfigurationType.Multi);
        Assert.Null(direct.ParentConfigId);
        Assert.Null(direct.Repetitions);
        Assert.Equal(simple.Id, inherited.ParentConfigId);
        Assert.Equal(3, inherited.Repetitions);
        Assert.All(discovery.Configurations, option =>
        {
            Assert.Equal(PublicStatisticsTestHost.Facility, option.FacilityId);
            Assert.Equal(PublicStatisticsTestHost.Start, option.CoursePointStart);
            Assert.Equal(PublicStatisticsTestHost.End, option.CoursePointEnd);
            Assert.Equal("Start", option.CoursePointStartName);
            Assert.Equal("Finish", option.CoursePointEndName);
        });
        Assert.Equal(2, host.Points.FacilityCalls.Count);
        Assert.Equal(2, host.Points.NameCalls.Count);
        Assert.DoesNotContain("timingPoint", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/statistics/admin/items")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/statistics/admin/configurations")).StatusCode);
    }

    [Theory]
    [InlineData("point", PublicStatisticsConfigurationUnavailableReason.MissingCoursePoint)]
    [InlineData("facility", PublicStatisticsConfigurationUnavailableReason.MissingFacility)]
    [InlineData("different", PublicStatisticsConfigurationUnavailableReason.DifferentFacilities)]
    public async Task Discovery_ReportsUnavailableDefinitionsInsteadOfSelectableOptions(
        string failure, PublicStatisticsConfigurationUnavailableReason expected)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap" };
        var simple = Simple(item);
        await host.SeedAsync(simple);
        if (failure == "point") host.Points.Names[PublicStatisticsTestHost.End] = null;
        if (failure == "facility") host.Points.Facilities[PublicStatisticsTestHost.End] = null;
        if (failure == "different") host.Points.Facilities[PublicStatisticsTestHost.End] = PublicStatisticsTestHost.OtherFacility;
        using var client = Anonymous(host);
        var discovery = (await client.GetFromJsonAsync<PublicStatisticsDiscoveryDTO>(PublicStatisticsQuery.DiscoveryPath))!;
        Assert.Empty(discovery.Configurations);
        Assert.Equal(expected, Assert.Single(discovery.UnavailableConfigurations).Reason);
    }

    [Theory]
    [InlineData(502)]
    [InlineData(503)]
    public async Task Discovery_PropagatesUpstreamFailureWithoutSuccessShapedEmptyData(int status)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        await host.SeedAsync(Simple(new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap" }));
        host.Points.Failure = new HttpRequestException("secret upstream body", null, (HttpStatusCode)status);
        using var client = Anonymous(host);
        using var response = await client.GetAsync(PublicStatisticsQuery.DiscoveryPath);
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("facilityId=bad&itemId=bad&configurationId=bad")]
    [InlineData("minTime=00%3A00%3A01.0001")]
    [InlineData("minTime=-00%3A00%3A01.000")]
    [InlineData("minSpeed=NaN")]
    [InlineData("maxSpeed=Infinity")]
    [InlineData("minSpeed=-1")]
    [InlineData("minSpeed=20&maxSpeed=10")]
    [InlineData("minTime=00%3A00%3A02.000&maxTime=00%3A00%3A01.000")]
    [InlineData("direction=next")]
    [InlineData("cursor=bad&direction=next")]
    [InlineData("direction=Next")]
    [InlineData("direction=0")]
    [InlineData("pageSize=25")]
    [InlineData("offset=0")]
    [InlineData("name=a&name=b")]
    public async Task Results_RejectsMalformedParametersWithProblemResponse(string invalid)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        using var client = Anonymous(host);
        var query = invalid.Length == 0 ? "" : SelectionQuery(Guid.NewGuid(), Guid.NewGuid()) + "&" + invalid;
        using var response = await client.GetAsync(PublicStatisticsQuery.ResultsPath + "?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, document.RootElement.GetProperty("status").GetInt32());
        Assert.Empty(host.Points.FacilityCalls);
        Assert.Empty(host.Points.NameCalls);
    }

    [Fact]
    public async Task Results_RejectsMismatchedSelectionBeforeEntryQuery()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        var simple = Simple(new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap" });
        await host.SeedAsync(simple);
        using var client = Anonymous(host);
        foreach (var query in new[]
        {
            SelectionQuery(Guid.NewGuid(), simple.Id),
            SelectionQuery(simple.StatisticsItem.Id, Guid.NewGuid()),
            SelectionQuery(simple.StatisticsItem.Id, simple.Id).Replace(
                PublicStatisticsTestHost.Facility.ToString("D"), PublicStatisticsTestHost.OtherFacility.ToString("D"))
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(PublicStatisticsQuery.ResultsPath + "?" + query)).StatusCode);
    }

    internal static SimpleStatisticsItemConfig Simple(StatisticsItem item) => new()
    {
        Id = Guid.NewGuid(), StatisticsItem = item,
        CoursePointStart = PublicStatisticsTestHost.Start, CoursePointEnd = PublicStatisticsTestHost.End
    };
    internal static HttpClient Anonymous(PublicStatisticsTestHost host)
    {
        var client = host.App.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        return client;
    }
    internal static string SelectionQuery(Guid item, Guid configuration) =>
        $"facilityId={PublicStatisticsTestHost.Facility:D}&itemId={item:D}&configurationId={configuration:D}";
}
