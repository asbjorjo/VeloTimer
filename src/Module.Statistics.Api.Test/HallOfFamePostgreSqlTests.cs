using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;
using Xunit.Abstractions;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class HallOfFamePostgreSqlTests(ITestOutputHelper output)
{
    [PublicStatisticsPostgreSqlFact]
    public async Task HallOfFame_AggregatesPublicProfilesAcrossConfigurationsAndRanksWithFixedCutoffs()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var item = new StatisticsItem
        {
            Id = Guid.NewGuid(), Name = "All-time item", Description = "", Distance = 250
        };
        var firstConfig = PublicStatisticsDiscoveryTests.Simple(item);
        var secondConfig = PublicStatisticsDiscoveryTests.Simple(item);
        var riderA = Profile("Rider", "00000000-0000-0000-0000-000000000001");
        var riderB = Profile("Rider", "00000000-0000-0000-0000-000000000002");
        var riderC = Profile("Rider", "00000000-0000-0000-0000-000000000003");
        var privateRider = Profile("Private rider", "00000000-0000-0000-0000-000000000004", isPublic: false);
        var entries = new List<StatisticsEntry>
        {
            Entry(1, firstConfig, riderA, 10, Completed(2), PublicStatisticsTestHost.OtherFacility, 5),
            Entry(2, secondConfig, riderA, 10, Completed(1), PublicStatisticsTestHost.Facility, 7.5),
            Entry(3, secondConfig, riderB, 10, Completed(3), PublicStatisticsTestHost.OtherFacility, 4),
            Entry(4, firstConfig, riderB, 14, Completed(4), PublicStatisticsTestHost.Facility, 3),
            Entry(5, firstConfig, riderC, 12, Completed(5), PublicStatisticsTestHost.Facility, 2),
            Entry(6, secondConfig, riderC, 15, Completed(6), PublicStatisticsTestHost.OtherFacility, 1.5),
            Entry(7, firstConfig, riderC, 16, Completed(7), PublicStatisticsTestHost.Facility, 1)
        };
        entries.AddRange(Enumerable.Range(8, 8).Select(index =>
            Entry(index, firstConfig, privateRider, 1, Completed(index), PublicStatisticsTestHost.Facility, 10)));
        entries.Add(Entry(16, secondConfig, null, 1, Completed(16), PublicStatisticsTestHost.Facility, 10));
        await host.SeedAsync(new object[] { firstConfig, secondConfig }.Concat(entries).ToArray());

        using var admin = host.App.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Test-Role", "statistics-admin");
        var savedResponse = await admin.PutAsJsonAsync("/api/admin/statistics/hall-of-fame",
            new SaveHallOfFameSettingsRequest(0,
            [
                new(item.Id, HallOfFameRankingMode.BestTime, true, 3,
                    [HallOfFameColumn.BestTime, HallOfFameColumn.SpeedOfBestResult,
                     HallOfFameColumn.PassingCount, HallOfFameColumn.FacilityOfBestResult,
                     HallOfFameColumn.DateOfBestResult]),
                new(item.Id, HallOfFameRankingMode.MostPassings, true, 2,
                    [HallOfFameColumn.PassingCount])
            ]));
        Assert.Equal(HttpStatusCode.OK, savedResponse.StatusCode);
        Assert.Equal(1, (await savedResponse.Content.ReadFromJsonAsync<HallOfFameSettingsDTO>())!.Version);

        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        using var response = await client.GetAsync("/api/statistics/hall-of-fame");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var hall = (await response.Content.ReadFromJsonAsync<HallOfFameDTO>())!;
        Assert.Collection(hall.Sections,
            bestTime =>
            {
                Assert.Equal(HallOfFameRankingMode.BestTime, bestTime.RankingMode);
                Assert.Equal(3, bestTime.Entries.Count);
                Assert.Equal(new[] { 1, 1, 3 }, bestTime.Entries.Select(entry => entry.Rank));
                Assert.Equal(new[] { "Rider", "Rider", "Rider" }, bestTime.Entries.Select(entry => entry.ProfileName));
                Assert.Equal(TimeSpan.FromSeconds(10), bestTime.Entries[0].BestTime);
                Assert.Equal(7.5, bestTime.Entries[0].SpeedOfBestResult);
                Assert.Equal(2L, bestTime.Entries[0].PassingCount);
                Assert.Equal("Velodrome", bestTime.Entries[0].FacilityOfBestResult);
                Assert.Equal(new DateTimeOffset(Completed(1)), bestTime.Entries[0].DateOfBestResult);
                Assert.Equal(TimeSpan.FromSeconds(12), bestTime.Entries[2].BestTime);
            },
            mostPassings =>
            {
                Assert.Equal(HallOfFameRankingMode.MostPassings, mostPassings.RankingMode);
                Assert.Equal(2, mostPassings.Entries.Count);
                Assert.Equal(new[] { 1, 2 }, mostPassings.Entries.Select(entry => entry.Rank));
                Assert.Equal(new long?[] { 3, 2 }, mostPassings.Entries.Select(entry => entry.PassingCount));
                Assert.Equal(new[] { "Rider", "Rider" }, mostPassings.Entries.Select(entry => entry.ProfileName));
                Assert.All(mostPassings.Entries, entry =>
                {
                    Assert.Null(entry.BestTime);
                    Assert.Null(entry.SpeedOfBestResult);
                    Assert.Null(entry.FacilityOfBestResult);
                    Assert.Null(entry.DateOfBestResult);
                });
            });
        Assert.Equal(1, host.Facilities.Calls);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var responseJson = json.RootElement.GetRawText();
        foreach (var privateId in new[]
        {
            riderA.UserId, riderB.UserId, riderC.UserId, privateRider.UserId,
            entries[0].TransponderId, entries[^1].TransponderId, entries[0].Id
        })
            Assert.DoesNotContain(privateId.ToString(), responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Private rider", responseJson);

        var query = host.Sql.DetailedCommands.Last(command =>
            command.Text.Contains("RANK() OVER", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("ROW_NUMBER() OVER", query.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COLLATE \"C\"", query.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_public", query.Text, StringComparison.OrdinalIgnoreCase);
        var plan = await ExplainAsync(host, query);
        output.WriteLine("Hall of Fame PostgreSQL ranking query plan:\n{0}", plan);
        Assert.Contains("Execution Time", plan, StringComparison.OrdinalIgnoreCase);

        await host.MutateAsync(async storage =>
        {
            var rider = await storage.Set<StatsProfile>().SingleAsync(profile => profile.UserId == riderC.UserId);
            rider.IsPublic = false;
        });
        using var refreshed = await client.GetAsync("/api/statistics/hall-of-fame");
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var afterVisibilityChange = (await refreshed.Content.ReadFromJsonAsync<HallOfFameDTO>())!;
        Assert.All(afterVisibilityChange.Sections, section => Assert.Equal(2, section.Entries.Count));
        Assert.All(afterVisibilityChange.Sections.SelectMany(section => section.Entries),
            entry => Assert.Equal("Rider", entry.ProfileName));
        Assert.Equal(2, host.Facilities.Calls);
    }

    private static StatsProfile Profile(string name, string id, bool isPublic = true) =>
        new() { UserId = Guid.Parse(id), ProfileName = name, IsPublic = isPublic };

    private static StatisticsEntry Entry(
        int id,
        SimpleStatisticsItemConfig config,
        StatsProfile? profile,
        double durationSeconds,
        DateTime completion,
        Guid facilityId,
        double speed) => new()
    {
        Id = Guid.Parse($"30000000-0000-0000-0000-{id:D12}"),
        StatisticsItem = config.StatisticsItem,
        StatisticsItemConfig = config,
        TransponderId = Guid.Parse($"40000000-0000-0000-0000-{id:D12}"),
        StatsProfile = profile,
        FacilityId = facilityId,
        Duration = TimeSpan.FromSeconds(durationSeconds),
        Speed = speed,
        TimeStart = completion.AddSeconds(-durationSeconds),
        TimeEnd = completion
    };

    private static DateTime Completed(int seconds) =>
        new(2026, 11, 1, 12, 0, seconds, DateTimeKind.Utc);

    private static async Task<string> ExplainAsync(
        PublicStatisticsTestHost host,
        (string Text, (string Name, object? Value)[] Parameters) query)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
        await storage.Database.OpenConnectionAsync();
        await using var command = storage.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT) {query.Text}";
        foreach (var (name, value) in query.Parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await reader.ReadAsync())
            lines.Add(reader.GetString(0));
        return string.Join(Environment.NewLine, lines);
    }
}
