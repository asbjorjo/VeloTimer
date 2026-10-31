using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class PublicStatisticsSelectionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Results_ResolveOnlySelectedSimpleEndpointsWithoutNamesOrCatalogue(
        bool multi, bool sameEndpoint)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        var simple = PublicStatisticsDiscoveryTests.Simple(Item());
        if (sameEndpoint) simple.CoursePointEnd = simple.CoursePointStart;
        StatisticsItemConfig selected = multi ? Multi(simple) : simple;
        // An unrelated valid definition must not make this selection depend on its
        // unavailable Facilities service, nor on any display-name service.
        var unrelated = PublicStatisticsDiscoveryTests.Simple(Item());
        unrelated.CoursePointStart = Guid.NewGuid();
        unrelated.CoursePointEnd = Guid.NewGuid();
        host.Points.FacilityFailures[unrelated.CoursePointStart] = new HttpRequestException("unrelated outage");
        host.Points.FacilityFailures[unrelated.CoursePointEnd] = new HttpRequestException("unrelated outage");
        host.Points.NameFailure = new HttpRequestException("names must not be requested");
        await host.SeedAsync(selected, unrelated);
        host.Sql.Commands.Clear();
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);

        using var response = await client.GetAsync(Url(selected));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var page = (await response.Content.ReadFromJsonAsync<PublicStatisticsResultsPageDTO>())!;
        Assert.Empty(page.Items);
        Assert.Null(page.Next);
        Assert.Null(page.Previous);
        Assert.Equal(sameEndpoint ? new[] { simple.CoursePointStart }.Order() :
                new[] { simple.CoursePointStart, simple.CoursePointEnd }.Order(),
            host.Points.FacilityCalls.Order());
        Assert.Empty(host.Points.NameCalls);
        AssertTargetedDefinitionQueries(host);
    }

    [Theory]
    [InlineData(false, "missing-configuration")]
    [InlineData(true, "missing-configuration")]
    [InlineData(false, "wrong-item")]
    [InlineData(true, "wrong-item")]
    [InlineData(false, "empty-start")]
    [InlineData(true, "empty-start")]
    [InlineData(false, "empty-end")]
    [InlineData(true, "empty-end")]
    [InlineData(true, "zero-repetitions")]
    [InlineData(true, "negative-repetitions")]
    public async Task Results_RejectInvalidSelectedDefinitionsWithoutFacilityOrNameCalls(bool multi, string invalid)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        var simple = PublicStatisticsDiscoveryTests.Simple(Item());
        if (invalid == "empty-start") simple.CoursePointStart = Guid.Empty;
        if (invalid == "empty-end") simple.CoursePointEnd = Guid.Empty;
        StatisticsItemConfig selected = multi ? Multi(simple) : simple;
        if (selected is MultiStatisticsItemConfig aggregate)
        {
            if (invalid == "zero-repetitions") aggregate.Repetitions = 0;
            if (invalid == "negative-repetitions") aggregate.Repetitions = -1;
        }
        await host.SeedAsync(selected);
        host.Points.Failure = new HttpRequestException("invalid definition must not resolve dependencies");
        host.Sql.Commands.Clear();
        var url = Url(selected);
        if (invalid == "missing-configuration") url = url.Replace(selected.Id.ToString("D"), Guid.NewGuid().ToString("D"));
        if (invalid == "wrong-item") url = url.Replace(selected.StatisticsItem.Id.ToString("D"), Guid.NewGuid().ToString("D"));
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(host.Points.FacilityCalls);
        Assert.Empty(host.Points.NameCalls);
        AssertNoEntryQuery(host);
        AssertTargetedDefinitionQueries(host);
    }

    [Theory]
    [InlineData("missing-parent")]
    [InlineData("non-simple-parent")]
    [InlineData("missing-item")]
    [InlineData("missing-parent-item")]
    public async Task Results_RejectBrokenMultiReferencesWithoutResolvingDependencies(string invalid)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        var parent = PublicStatisticsDiscoveryTests.Simple(Item());
        var selected = Multi(parent);
        var otherMulti = Multi(PublicStatisticsDiscoveryTests.Simple(Item()));
        await host.SeedAsync(selected, otherMulti);
        var missingId = Guid.NewGuid();
        // Synthetic legacy/corrupt rows exercise validation before cross-service
        // resolution. Normal EF writes deliberately cannot create dangling FKs.
        await host.MutateAsync(async storage =>
        {
            await storage.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
            try
            {
                if (invalid is "missing-parent" or "non-simple-parent")
                    await storage.Set<MultiStatisticsItemConfig>().Where(config => config.Id == selected.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(config => config.ParentConfigId,
                            invalid == "missing-parent" ? missingId : otherMulti.Id));
                else
                {
                    var id = invalid == "missing-item" ? selected.Id : parent.Id;
                    await storage.Set<StatisticsItemConfig>().Where(config => config.Id == id)
                        .ExecuteUpdateAsync(update => update.SetProperty(config => config.StatisticsItemId, missingId));
                }
            }
            finally
            {
                await storage.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON");
            }
        });
        host.Points.Failure = new HttpRequestException("broken references must not resolve dependencies");
        host.Sql.Commands.Clear();
        var url = PublicStatisticsQuery.ResultsPath + "?" + PublicStatisticsDiscoveryTests.SelectionQuery(
            invalid == "missing-item" ? missingId : selected.StatisticsItem.Id, selected.Id);
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(host.Points.FacilityCalls);
        Assert.Empty(host.Points.NameCalls);
        AssertNoEntryQuery(host);
        AssertTargetedDefinitionQueries(host);
    }

    [Theory]
    [InlineData(false, "missing-facility")]
    [InlineData(true, "missing-facility")]
    [InlineData(false, "different-facilities")]
    [InlineData(true, "different-facilities")]
    [InlineData(false, "wrong-facility")]
    [InlineData(true, "wrong-facility")]
    public async Task Results_RejectInvalidSelectedFacilityUsingOnlySelectedEndpoints(bool multi, string invalid)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        var simple = PublicStatisticsDiscoveryTests.Simple(Item());
        StatisticsItemConfig selected = multi ? Multi(simple) : simple;
        await host.SeedAsync(selected);
        if (invalid == "missing-facility") host.Points.Facilities[simple.CoursePointEnd] = null;
        if (invalid == "different-facilities") host.Points.Facilities[simple.CoursePointEnd] = PublicStatisticsTestHost.OtherFacility;
        host.Points.NameFailure = new HttpRequestException("names must not be requested");
        host.Sql.Commands.Clear();
        var url = Url(selected);
        if (invalid == "wrong-facility") url = url.Replace(PublicStatisticsTestHost.Facility.ToString("D"),
            PublicStatisticsTestHost.OtherFacility.ToString("D"));
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new[] { simple.CoursePointStart, simple.CoursePointEnd }.Order(), host.Points.FacilityCalls.Order());
        Assert.Empty(host.Points.NameCalls);
        AssertNoEntryQuery(host);
        AssertTargetedDefinitionQueries(host);
    }

    [Theory]
    [InlineData(false, 502)]
    [InlineData(true, 502)]
    [InlineData(false, 503)]
    [InlineData(true, 503)]
    public async Task Results_SelectedDependencyFailureRemainsProblemNotEmptySuccess(bool multi, int status)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        var simple = PublicStatisticsDiscoveryTests.Simple(Item());
        StatisticsItemConfig selected = multi ? Multi(simple) : simple;
        await host.SeedAsync(selected);
        host.Points.FacilityFailures[simple.CoursePointStart] =
            new HttpRequestException("private upstream detail", null, (HttpStatusCode)status);
        host.Sql.Commands.Clear();
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);

        using var response = await client.GetAsync(Url(selected));

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("private upstream detail", await response.Content.ReadAsStringAsync());
        Assert.Equal(simple.CoursePointStart, Assert.Single(host.Points.FacilityCalls));
        Assert.Empty(host.Points.NameCalls);
        AssertNoEntryQuery(host);
    }

    private static StatisticsItem Item() => new() { Id = Guid.NewGuid(), Name = "Lap", Distance = 250 };
    private static MultiStatisticsItemConfig Multi(SimpleStatisticsItemConfig parent) => new()
    {
        Id = Guid.NewGuid(), StatisticsItem = Item(), ParentConfig = parent, ParentConfigId = parent.Id, Repetitions = 3
    };
    private static string Url(StatisticsItemConfig config) => PublicStatisticsQuery.ResultsPath + "?" +
        PublicStatisticsDiscoveryTests.SelectionQuery(config.StatisticsItem.Id, config.Id);

    private static void AssertNoEntryQuery(PublicStatisticsTestHost host) =>
        Assert.DoesNotContain(host.Sql.Commands, command =>
            command.Text.Contains("statistics_entry", StringComparison.OrdinalIgnoreCase) ||
            command.Text.Contains("StatisticsEntry", StringComparison.OrdinalIgnoreCase));

    private static void AssertTargetedDefinitionQueries(PublicStatisticsTestHost host)
    {
        var definitions = host.Sql.Commands.Where(command =>
            command.Text.Contains("statistics_item", StringComparison.OrdinalIgnoreCase) ||
            command.Text.Contains("StatisticsItem", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.NotEmpty(definitions);
        Assert.All(definitions, command =>
        {
            Assert.Contains("WHERE", command.Text, StringComparison.OrdinalIgnoreCase);
            Assert.NotEmpty(command.Parameters);
        });
    }
}
