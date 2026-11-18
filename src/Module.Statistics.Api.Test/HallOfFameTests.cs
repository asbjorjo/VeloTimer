using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class HallOfFameTests
{
    [Theory]
    [InlineData("lap")]
    [InlineData("200m")]
    [InlineData("1000m")]
    [InlineData("3000m")]
    [InlineData("4000m")]
    public async Task Times_SelectTenPersonalBestsAndFilterBeforeRanking(string category) =>
        await AssertTimes(category, postgres: false);

    [PublicStatisticsPostgreSqlFact]
    public async Task PostgreSql_TimesSelectTenPersonalBests() => await AssertTimes("lap", postgres: true);

    private static async Task AssertTimes(string category, bool postgres)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres);
        var config = Config(HallOfFameQuery.ItemName(category));
        var profiles = Enumerable.Range(1, 12).Select(index => Profile("Rider " + index)).ToArray();
        var rows = profiles.Select((profile, index) => Entry(config, profile, index + 10)).ToList();
        rows.Add(Entry(config, profiles[0], 50));
        rows.Add(Entry(config, profiles[0], 5, PublicStatisticsTestHost.OtherFacility));
        var invalid = Entry(config, Profile("Invalid"), 1);
        invalid.IsValid = false;
        invalid.InvalidReason = StatisticsEntryInvalidReason.BelowMinimumDuration;
        rows.AddRange([invalid, Entry(config, Profile("Private", false), 1), Entry(config, null, 1)]);
        // Public profiles must only be ranked by entries for the requested item at the requested facility.
        // The other item must not share a hall-of-fame name, since the theory also runs for category "200m".
        rows.Add(Entry(Config("Other item"), Profile("Other item only"), 1));
        rows.Add(Entry(config, Profile("Other facility only"), 7, PublicStatisticsTestHost.OtherFacility));
        await host.SeedAsync(rows.Cast<object>().ToArray());
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        var global = await Get(client, $"category={category}");
        Assert.True(global.IsAvailable);
        Assert.Empty(global.Laps);
        Assert.Equal(10, global.Times.Count);
        Assert.Equal("Rider 1", global.Times[0].ProfileName);
        Assert.Equal(TimeSpan.FromSeconds(5), global.Times[0].Duration);
        Assert.Equal(50, global.Times[0].SpeedMetresPerSecond);
        Assert.Equal(PublicStatisticsTestHost.OtherFacility, global.Times[0].FacilityId);
        Assert.Equal(10, global.Times.Select(row => row.ProfileName).Distinct().Count());
        Assert.DoesNotContain(global.Times, row => row.ProfileName == "Other item only");
        Assert.Equal("Other facility only", global.Times[1].ProfileName);
        Assert.Equal(PublicStatisticsTestHost.OtherFacility, global.Times[1].FacilityId);
        var filtered = await Get(client, $"category={category}&facilityId={PublicStatisticsTestHost.Facility}");
        Assert.Equal(TimeSpan.FromSeconds(10), filtered.Times[0].Duration);
        Assert.All(filtered.Times, row => Assert.Equal(PublicStatisticsTestHost.Facility, row.FacilityId));
        Assert.Equal(10, filtered.Times.Count);
        Assert.DoesNotContain(filtered.Times, row => row.ProfileName is "Other item only" or "Other facility only");
        await host.MutateAsync(async storage =>
            (await storage.Set<StatsProfile>().SingleAsync(profile => profile.UserId == profiles[0].UserId)).IsPublic = false);
        Assert.DoesNotContain((await Get(client, $"category={category}")).Times, row => row.ProfileName == "Rider 1");
        if (postgres)
        {
            var sql = string.Join("\n", host.Sql.Commands.Select(command => command.Text));
            Assert.Contains("ROW_NUMBER() OVER(PARTITION BY", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("NOT EXISTS", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Laps_CountOnlyValidLapsAcrossTranspondersAndUseCurrentDistance() => await AssertLaps(false);

    [PublicStatisticsPostgreSqlFact]
    public async Task PostgreSql_LapAggregationIsBoundedAndUsesCurrentDistance() => await AssertLaps(true);

    private static async Task AssertLaps(bool postgres)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres);
        var lap = Config("Lap");
        var sprint = Config("200m");
        var profile = Profile("Shared name");
        var sameName = Profile("Shared name");
        var invalid = Entry(lap, profile, 1);
        invalid.IsValid = false;
        invalid.InvalidReason = StatisticsEntryInvalidReason.BelowMinimumDuration;
        await host.SeedAsync(Entry(lap, profile, 10), Entry(lap, profile, 11, PublicStatisticsTestHost.OtherFacility),
            Entry(lap, profile, 12), Entry(lap, sameName, 10), Entry(sprint, profile, 1),
            invalid, Entry(lap, null, 10), Entry(lap, Profile("Hidden", false), 10));
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        var result = await Get(client, "category=laps");
        Assert.Empty(result.Times);
        Assert.Equal(new long[] { 3, 1 }, result.Laps.Select(row => row.LapCount));
        Assert.Equal(750, result.Laps[0].TotalDistanceMetres);
        Assert.Equal(2, (await Get(client, $"category=laps&facilityId={PublicStatisticsTestHost.Facility}")).Laps[0].LapCount);
        await host.MutateAsync(async storage =>
            (await storage.Set<StatisticsItem>().SingleAsync(item => item.Name == "Lap")).Distance = 400);
        Assert.Equal(1200, (await Get(client, "category=laps")).Laps[0].TotalDistanceMetres);
        using var response = await client.GetAsync(HallOfFameQuery.Path + "?category=laps");
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("userId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transponder", json, StringComparison.OrdinalIgnoreCase);
        if (postgres)
            Assert.Contains(host.Sql.Commands, command => command.Text.Contains("GROUP BY") && command.Text.Contains("LIMIT"));
    }

    [Fact]
    public async Task Times_TiesUseCompletionThenEntryIdAndDistinctProfilesNotNames()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        var config = Config("Lap");
        var profile = Profile("Same");
        var first = Entry(config, profile, 10, id: Guid.Parse("10000000-0000-0000-0000-000000000001"));
        var second = Entry(config, profile, 10, PublicStatisticsTestHost.OtherFacility,
            Guid.Parse("20000000-0000-0000-0000-000000000001"));
        var other = Entry(config, Profile("Same"), 10);
        await host.SeedAsync(first, second, other);
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        var result = await Get(client, "");
        Assert.Equal(2, result.Times.Count);
        Assert.All(result.Times, row => Assert.Equal("Same", row.ProfileName));
        Assert.All(result.Times, row => Assert.Equal(PublicStatisticsTestHost.Facility, row.FacilityId));
    }

    [Fact]
    public async Task Cutoffs_AreExactlyTenAndDeterministicForBothModes() => await AssertCutoffs(false);

    [PublicStatisticsPostgreSqlFact]
    public async Task PostgreSql_CutoffsAreExactlyTenAndDeterministicForBothModes() => await AssertCutoffs(true);

    private static async Task AssertCutoffs(bool postgres)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres);
        var config = Config("Lap");
        var entries = new List<StatisticsEntry>();
        for (var index = 1; index <= 12; index++)
        {
            var profile = Profile("Profile " + index);
            for (var lap = 0; lap < index; lap++)
                entries.Add(Entry(config, profile, 10, id: Guid.Parse($"{index:x8}-{lap:x4}-0000-0000-000000000001")));
        }
        await host.SeedAsync(entries.AsEnumerable().Reverse().Cast<object>().ToArray());
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        var times = (await Get(client, "category=lap")).Times;
        Assert.Equal(Enumerable.Range(1, 10).Select(index => "Profile " + index), times.Select(row => row.ProfileName));
        var laps = (await Get(client, "category=laps")).Laps;
        Assert.Equal(Enumerable.Range(3, 10).Reverse().Select(index => (long)index), laps.Select(row => row.LapCount));
        Assert.All(laps, row => Assert.Equal(row.LapCount * 250d, row.TotalDistanceMetres));
    }

    [Fact]
    public async Task LapDistance_MustBePositiveAndFinite()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        await host.SeedAsync(new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap", Distance = 0 });
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        using var response = await client.GetAsync(HallOfFameQuery.Path + "?category=laps");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task LapDistance_RankingAndSnapshots() => await AssertVariableLaps(false);

    [PublicStatisticsPostgreSqlFact]
    public async Task PostgreSql_LapDistance_RankingAndSnapshots() => await AssertVariableLaps(true);

    private static async Task AssertVariableLaps(bool postgres)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres);
        var config = Config("Lap");
        config.StatisticsItem.DistanceMode = StatisticsDistanceMode.CourseLayout;
        config.StatisticsItem.Distance = 0;
        var first = Profile("First");
        var second = Profile("Second");
        await host.SeedAsync(
            Entry(config, first, 20, distance: 250),
            Entry(config, first, 24, PublicStatisticsTestHost.OtherFacility, distance: 333.33),
            Entry(config, second, 21, distance: 333.33),
            Entry(config, second, 25, PublicStatisticsTestHost.OtherFacility, distance: 250),
            Entry(config, Profile("Shorter but slower"), 19, distance: 250));
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);

        var global = (await Get(client, "category=lap")).Times;
        Assert.Equal(new[] { "Second", "First", "Shorter but slower" }, global.Select(row => row.ProfileName));
        Assert.Equal(TimeSpan.FromSeconds(21), global[0].Duration);
        Assert.Equal(333.33 / 21, global[0].SpeedMetresPerSecond, 8);
        Assert.Equal(TimeSpan.FromSeconds(24), global[1].Duration);
        Assert.Equal(333.33 / 24, global[1].SpeedMetresPerSecond, 8);
        Assert.Equal(PublicStatisticsTestHost.OtherFacility, global[1].FacilityId);
        Assert.Equal(TimeSpan.FromSeconds(19), global[2].Duration);
        Assert.True(global[0].SpeedMetresPerSecond > global[2].SpeedMetresPerSecond);

        var filtered = (await Get(client, $"category=lap&facilityId={PublicStatisticsTestHost.Facility}")).Times;
        Assert.Equal(new[] { "Shorter but slower", "First", "Second" }, filtered.Select(row => row.ProfileName));
        Assert.Equal(new[] { TimeSpan.FromSeconds(19), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(21) }, filtered.Select(row => row.Duration));
        Assert.All(filtered, row => Assert.Equal(PublicStatisticsTestHost.Facility, row.FacilityId));

        var laps = (await Get(client, "category=laps")).Laps;
        Assert.Equal(3, laps.Count);
        Assert.All(laps.Where(row => row.ProfileName != "Shorter but slower"), row =>
            { Assert.Equal(2, row.LapCount); Assert.Equal(583.33, row.TotalDistanceMetres, 8); });
        Assert.Equal(250, Assert.Single(laps, row => row.ProfileName == "Shorter but slower").TotalDistanceMetres);
        var facilityLaps = (await Get(client, $"category=laps&facilityId={PublicStatisticsTestHost.Facility}")).Laps;
        Assert.Equal(3, facilityLaps.Count);
        Assert.Equal(250, Assert.Single(facilityLaps, row => row.ProfileName == "First").TotalDistanceMetres);
        Assert.Equal(333.33, Assert.Single(facilityLaps, row => row.ProfileName == "Second").TotalDistanceMetres, 8);
        Assert.All(facilityLaps, row => Assert.Equal(1, row.LapCount));
        if (postgres)
            Assert.Contains(host.Sql.Commands, command => command.Text.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)
                && command.Text.Contains("SUM(", StringComparison.OrdinalIgnoreCase)
                && command.Text.Contains("COUNT(", StringComparison.OrdinalIgnoreCase)
                && command.Text.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CourseLayoutLaps_MissingSnapshotIsConflict()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        var config = Config("Lap");
        config.StatisticsItem.DistanceMode = StatisticsDistanceMode.CourseLayout;
        config.StatisticsItem.Distance = 0;
        await host.SeedAsync(Entry(config, Profile("Rider"), 20));
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        using var response = await client.GetAsync(HallOfFameQuery.Path + "?category=laps");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("category=unknown")]
    [InlineData("category=")]
    [InlineData("category=lap&category=laps")]
    [InlineData("facilityId=")]
    [InlineData("facilityId=00000000-0000-0000-0000-000000000000")]
    [InlineData("facilityId=no")]
    [InlineData("limit=100")]
    public async Task RejectsInvalidQueries(string query)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        using var response = await client.GetAsync(HallOfFameQuery.Path + "?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Definitions_DistinguishMissingEmptyAndAmbiguous()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        Assert.False((await Get(client, "")).IsAvailable);
        await host.SeedAsync(new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap", Distance = 250 });
        Assert.True((await Get(client, "")).IsAvailable);
        Assert.Empty((await Get(client, "")).Times);
        await host.SeedAsync(new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap", Distance = 400 });
        using var response = await client.GetAsync(HallOfFameQuery.Path);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private static async Task<HallOfFameDTO> Get(HttpClient client, string query)
    {
        using var response = await client.GetAsync(HallOfFameQuery.Path + "?" + query);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<HallOfFameDTO>())!;
    }

    private static StatsProfile Profile(string name, bool isPublic = true) =>
        new() { UserId = Guid.NewGuid(), ProfileName = name, IsPublic = isPublic };

    private static SimpleStatisticsItemConfig Config(string name) => new()
    {
        Id = Guid.NewGuid(), StatisticsItem = new() { Id = Guid.NewGuid(), Name = name, Distance = 250 },
        CoursePointStart = PublicStatisticsTestHost.Start, CoursePointEnd = PublicStatisticsTestHost.End
    };

    private static StatisticsEntry Entry(SimpleStatisticsItemConfig config, StatsProfile? profile, int seconds, Guid? facility = null, Guid? id = null, double? distance = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(), StatisticsItem = config.StatisticsItem, StatisticsItemConfig = config,
            StatsProfile = profile, TransponderId = Guid.NewGuid(), Duration = TimeSpan.FromSeconds(seconds),
            Speed = (distance ?? 250d) / seconds, DistanceMetres = distance, FacilityId = facility ?? PublicStatisticsTestHost.Facility,
            TimeStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeEnd = new DateTime(2026, 1, 1, 0, 0, seconds, DateTimeKind.Utc)
        };
}
