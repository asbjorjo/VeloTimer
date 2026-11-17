using System.Net;
using System.Net.Http.Json;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class StatisticsEntrySamplesTests
{
    private static readonly DateTime Start = new(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = Start.AddMinutes(2);

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    [InlineData("timing-admin", HttpStatusCode.Forbidden)]
    [InlineData("facilities-admin", HttpStatusCode.Forbidden)]
    [InlineData("statistics-admin", HttpStatusCode.NotFound)]
    [InlineData("velotime-admin", HttpStatusCode.NotFound)]
    public async Task SamplesEndpointEnforcesStatisticsPolicyAndReturns404ForUnknownEntry(string? role, HttpStatusCode status)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        using var client = Client(host, role);
        using var response = await client.GetAsync(Url(Guid.NewGuid()));
        Assert.Equal(status, response.StatusCode);
        if (status == HttpStatusCode.NotFound) Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task SamplesUseInclusiveContainedBoundsTransponderOrderingAndUtcSiProjection()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var entry = Entry(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        var low = Sample(Guid.Parse("10000000-0000-0000-0000-000000000001"), entry.TransponderId, Start, Start.AddSeconds(1), double.PositiveInfinity);
        var tie2 = Sample(Guid.Parse("10000000-0000-0000-0000-000000000004"), entry.TransponderId, Start.AddSeconds(30), Start.AddSeconds(40));
        var tie1 = Sample(Guid.Parse("10000000-0000-0000-0000-000000000003"), entry.TransponderId, Start.AddSeconds(30), Start.AddSeconds(40));
        var high = Sample(Guid.Parse("10000000-0000-0000-0000-000000000002"), entry.TransponderId, End.AddSeconds(-1), End);
        var negativeInfinity = Sample(Guid.Parse("10000000-0000-0000-0000-000000000005"), entry.TransponderId, Start.AddSeconds(5), Start.AddSeconds(6), double.NegativeInfinity);
        var nan = Sample(Guid.Parse("10000000-0000-0000-0000-000000000006"), entry.TransponderId, Start.AddSeconds(7), Start.AddSeconds(8), double.NaN);
        await host.SeedAsync(entry, low, tie2, tie1, high, negativeInfinity, nan,
            Sample(Guid.NewGuid(), entry.TransponderId, Start.AddMilliseconds(-1), Start.AddSeconds(1)),
            Sample(Guid.NewGuid(), entry.TransponderId, End.AddSeconds(-1), End.AddMilliseconds(1)),
            Sample(Guid.NewGuid(), Guid.NewGuid(), Start, End));

        using var client = Client(host, "statistics-admin");
        using var response = await client.GetAsync(Url(entry.Id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var result = (await response.Content.ReadFromJsonAsync<AdminStatisticsEntrySamplesDTO>())!;
        Assert.Equal(entry.Id, result.EntryId);
        Assert.Equal(entry.TransponderId, result.TransponderId);
        Assert.Equal(new DateTimeOffset(Start), result.TimeStart);
        Assert.Equal(TimeSpan.Zero, result.TimeStart.Offset);
        Assert.Equal(new DateTimeOffset(End), result.TimeEnd);
        Assert.Equal(TimeSpan.Zero, result.TimeEnd.Offset);
        Assert.False(result.Truncated);
        Assert.Equal(new[] { low.Id, negativeInfinity.Id, nan.Id, tie1.Id, tie2.Id, high.Id }, result.Samples.Select(sample => sample.Id));
        Assert.Null(result.Samples[0].SpeedMetresPerSecond);
        Assert.Equal(9.25, result.Samples[3].SpeedMetresPerSecond);
        Assert.All(result.Samples.Where(sample => sample.Id == negativeInfinity.Id || sample.Id == nan.Id), sample => Assert.Null(sample.SpeedMetresPerSecond));
        Assert.All(result.Samples, sample =>
        {
            Assert.Equal(TimeSpan.Zero, sample.TimeStart.Offset);
            Assert.Equal(TimeSpan.Zero, sample.TimeEnd.Offset);
            Assert.Equal(125.5, sample.DistanceMetres);
            Assert.Equal(TimeSpan.FromTicks(1234560), sample.Duration);
            Assert.Equal(PublicStatisticsTestHost.Facility, sample.FacilityId);
        });
        Assert.Contains(host.Sql.Commands, command => command.Text.Contains("LIMIT", StringComparison.OrdinalIgnoreCase) && command.Parameters.Contains(501));
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task NonfiniteStoredDistanceReturnsExplicitConflictWithoutSerializingAValue()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var entry = Entry(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        await host.SeedAsync(entry, Sample(Guid.NewGuid(), entry.TransponderId, Start, End, distance: double.NaN));
        using var client = Client(host, "statistics-admin");
        using var response = await client.GetAsync(Url(entry.Id));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("NaN", await response.Content.ReadAsStringAsync());
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task SamplesDistinguishEmpty500And501Matches()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var full = Entry(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        var exactly500 = Entry(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        var samples = Enumerable.Range(0, 501).Select(i => Sample(
            Guid.Parse($"e0000000-0000-0000-0000-{i + 1:D12}"), full.TransponderId,
            Start.AddMilliseconds(i), Start.AddMilliseconds(i))).ToArray();
        var otherSamples = samples.Take(500).Select((sample, index) => Sample(
            Guid.Parse($"f0000000-0000-0000-0000-{index + 1:D12}"), exactly500.TransponderId,
            sample.TimeStart, sample.TimeEnd)).ToArray();
        await host.SeedAsync(new object[] { full, exactly500 }.Concat(samples).Concat(otherSamples).ToArray());
        using var client = Client(host, "statistics-admin");
        var first = await Samples(client, full.Id);
        Assert.Equal(500, first.Samples.Count);
        Assert.True(first.Truncated);
        Assert.Equal(samples.Take(500).Select(sample => sample.Id), first.Samples.Select(sample => sample.Id));
        var second = await Samples(client, exactly500.Id);
        Assert.Equal(500, second.Samples.Count);
        Assert.False(second.Truncated);
        var emptyEntry = Entry(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        await host.SeedAsync(emptyEntry);
        var empty = await Samples(client, emptyEntry.Id);
        Assert.Empty(empty.Samples);
        Assert.False(empty.Truncated);
    }

    private static HttpClient Client(PublicStatisticsTestHost host, string? role)
    {
        var client = host.App.CreateClient();
        if (role is null) client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        else client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }
    private static string Url(Guid id) => $"/api/statistics/admin/entries/{id:D}/samples";
    private static async Task<AdminStatisticsEntrySamplesDTO> Samples(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(Url(id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<AdminStatisticsEntrySamplesDTO>())!;
    }
    private static StatisticsEntry Entry(Guid id, Guid transponder, DateTime start, DateTime end)
    {
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Regression", Distance = 125.5f };
        var config = new SimpleStatisticsItemConfig { Id = Guid.NewGuid(), StatisticsItem = item,
            StatisticsItemId = item.Id, CoursePointStart = Guid.NewGuid(), CoursePointEnd = Guid.NewGuid() };
        return new StatisticsEntry { Id = id, StatisticsItem = item, StatisticsItemId = item.Id,
            StatisticsItemConfig = config, StatisticsItemConfigId = config.Id, TransponderId = transponder,
            TimeStart = start, TimeEnd = end, Duration = end - start, Speed = 1,
            FacilityId = PublicStatisticsTestHost.Facility };
    }
    private static Sample Sample(Guid id, Guid transponder, DateTime start, DateTime end, double speed = 9.25, double distance = 125.5) => new()
    {
        Id = id, TransponderId = transponder, TimeStart = start, TimeEnd = end,
        CoursePointStartId = Guid.NewGuid(), CoursePointEndId = Guid.NewGuid(),
        Distance = distance, Duration = TimeSpan.FromTicks(1234560), Speed = speed,
        FacilityId = PublicStatisticsTestHost.Facility
    };
}
