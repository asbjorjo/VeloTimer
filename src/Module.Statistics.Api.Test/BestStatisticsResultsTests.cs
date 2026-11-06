using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class BestStatisticsResultsTests(StatisticsApiFactory factory) : IClassFixture<StatisticsApiFactory>, IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Facility = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherFacility = Guid.Parse("10000000-0000-0000-0000-000000000002");

    public Task InitializeAsync() => factory.InitializeDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task BestResults_SelectsPublicWinnerPerItemAcrossConfigurations()
    {
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap", Distance = 250 };
        var winner = Entry(item, 20, Now.AddDays(-2));
        var hidden = Entry(item, 1, Now.AddDays(-1), new StatsProfile { UserId = Guid.NewGuid(), ProfileName = "Hidden", IsPublic = false });
        var unlinked = Entry(item, 2, Now.AddDays(-1));
        unlinked = CopyWithoutProfile(unlinked);
        var secondItem = Entry(new StatisticsItem { Id = Guid.NewGuid(), Name = "Sprint", Distance = 200 }, 10, Now);
        await Seed(winner, hidden, unlinked, Entry(item, 30, Now.AddDays(-1)), secondItem);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        var response = await client.GetAsync("/api/statistics/best-results");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = (await response.Content.ReadFromJsonAsync<List<BestStatisticsResultDTO>>())!;
        Assert.Equal(2, results.Count);
        Assert.Equal(winner.Id, results[0].Id);
        Assert.Equal("Public rider", results[0].ProfileName);
        Assert.Equal(Facility, results[0].FacilityId);
        Assert.Equal(TimeSpan.FromSeconds(20), results[0].Duration);
        Assert.Equal(45, results[0].Speed, 6);
        Assert.Equal(secondItem.Id, results[1].Id);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Hidden", json);
        Assert.DoesNotContain("userId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transponderId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BestResults_FiltersBeforeRankingAndUsesInclusiveFromExclusiveTo()
    {
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap", Distance = 250 };
        var lower = Now.AddDays(-30);
        var winner = Entry(item, 25, lower);
        await Seed(winner, Entry(item, 1, lower.AddTicks(-1)), Entry(item, 2, Now),
            Entry(item, 3, Now.AddDays(-1), facility: OtherFacility));
        var query = $"facilityId={Facility}&from={Uri.EscapeDataString(lower.ToString("O"))}&to={Uri.EscapeDataString(Now.ToString("O"))}";
        Assert.Equal(winner.Id, Assert.Single(await Get(query)).Id);
        Assert.Equal(3, Assert.Single(await Get($"facilityId={OtherFacility}")).Duration.TotalSeconds);
        Assert.Equal(1, Assert.Single(await Get($"to={Uri.EscapeDataString(lower.ToString("O"))}")).Duration.TotalSeconds);
        Assert.Equal(2, Assert.Single(await Get($"facilityId={Facility}&from={Uri.EscapeDataString(Now.ToString("O"))}")).Duration.TotalSeconds);
        Assert.Empty(await Get($"facilityId={Guid.NewGuid()}"));
    }

    [Fact]
    public async Task BestResults_BreaksTiesByCompletionThenId()
    {
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap", Distance = 250 };
        var first = Entry(item, 20, Now);
        var second = Entry(item, 20, Now);
        var expected = new[] { first, second }.OrderBy(entry => entry.Id).First();
        await Seed(first, second, Entry(item, 20, Now.AddSeconds(1)));
        Assert.Equal(expected.Id, Assert.Single(await Get("")).Id);
    }

    [Fact]
    public async Task BestResults_OmitsItemsWithoutEligibleResults()
    {
        var hidden = new StatsProfile { UserId = Guid.NewGuid(), ProfileName = "Hidden", IsPublic = false };
        await Seed(Entry(new StatisticsItem { Id = Guid.NewGuid(), Name = "Hidden only" }, 10, Now, hidden),
            CopyWithoutProfile(Entry(new StatisticsItem { Id = Guid.NewGuid(), Name = "Unlinked only" }, 10, Now)));
        Assert.Empty(await Get(""));
    }

    [Fact]
    public async Task BestResults_ReturnsEmptyListWithoutRecordedResults()
    {
        Assert.Empty(await Get(""));
    }

    [Fact]
    public async Task BestResults_ReflectsProfileVisibilityChangesOnNextRequest()
    {
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap", Distance = 250 };
        var fastest = Entry(item, 10, Now);
        var next = Entry(item, 20, Now);
        await Seed(fastest, next);
        Assert.Equal(fastest.Id, Assert.Single(await Get("")).Id);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
            var profile = await storage.Set<StatsProfile>().FindAsync(fastest.StatsProfile!.UserId);
            profile!.IsPublic = false;
            await storage.SaveChangesAsync();
        }
        Assert.Equal(next.Id, Assert.Single(await Get("")).Id);
    }

    [Fact]
    public async Task BestResults_NormalizesOffsetBoundsToUtc()
    {
        var winner = Entry(new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap", Distance = 250 }, 20, Now);
        await Seed(winner);
        Assert.Equal(winner.Id, Assert.Single(await Get(
            "from=2026-10-29T14%3A00%3A00%2B02%3A00&to=2026-10-29T14%3A00%3A01%2B02%3A00")).Id);
    }

    [Theory]
    [InlineData("facilityId=00000000-0000-0000-0000-000000000000")]
    [InlineData("facilityId=bad")]
    [InlineData("from=bad")]
    [InlineData("to=bad")]
    [InlineData("from=2026-10-29T12:00:00Z&to=2026-10-29T12:00:00Z")]
    [InlineData("from=2026-10-30T12:00:00Z&to=2026-10-29T12:00:00Z")]
    public async Task BestResults_RejectsInvalidFilters(string query)
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/statistics/best-results?" + query)).StatusCode);
    }

    private async Task<List<BestStatisticsResultDTO>> Get(string query)
    {
        using var client = factory.CreateClient();
        return (await client.GetFromJsonAsync<List<BestStatisticsResultDTO>>("/api/statistics/best-results?" + query))!;
    }

    private async Task Seed(params StatisticsEntry[] entries)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
        storage.AddRange(entries);
        await storage.SaveChangesAsync();
    }

    private static StatisticsEntry Entry(StatisticsItem item, double seconds, DateTime end,
        StatsProfile? profile = null, Guid? facility = null) => new()
    {
        Id = Guid.NewGuid(), StatisticsItem = item,
        StatisticsItemConfig = new SimpleStatisticsItemConfig
        {
            Id = Guid.NewGuid(), StatisticsItem = item, CoursePointStart = Guid.NewGuid(), CoursePointEnd = Guid.NewGuid()
        },
        StatsProfile = profile ?? new StatsProfile { UserId = Guid.NewGuid(), ProfileName = "Public rider" },
        TransponderId = Guid.NewGuid(), FacilityId = facility ?? Facility,
        TimeStart = end.AddSeconds(-seconds), TimeEnd = end,
        Duration = TimeSpan.FromSeconds(seconds), Speed = item.Distance / seconds
    };

    private static StatisticsEntry CopyWithoutProfile(StatisticsEntry entry) => new()
    {
        Id = entry.Id, StatisticsItem = entry.StatisticsItem, StatisticsItemConfig = entry.StatisticsItemConfig,
        TransponderId = entry.TransponderId, FacilityId = entry.FacilityId,
        TimeStart = entry.TimeStart, TimeEnd = entry.TimeEnd, Duration = entry.Duration, Speed = entry.Speed
    };
}
