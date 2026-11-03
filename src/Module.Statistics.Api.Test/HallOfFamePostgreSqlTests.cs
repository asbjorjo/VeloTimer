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

    [PublicStatisticsPostgreSqlFact]
    public async Task HallOfFame_TiedCutoffsAtOneAnd100NeverExpandAndDuplicateNamesRemainDistinct()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Tied item", Description = "", Distance = 250 };
        var config = PublicStatisticsDiscoveryTests.Simple(item);
        // Reverse insertion order: public order must depend on PostgreSQL UUID order, not insertion.
        var entries = Enumerable.Range(1, 101).Reverse().Select(index => Entry(index, config,
            Profile("Same name", $"00000000-0000-0000-0000-{index:D12}"),
            10, Completed(1), PublicStatisticsTestHost.Facility, index)).ToArray();
        await host.SeedAsync(new object[] { config }.Concat(entries).ToArray());
        using var admin = Admin(host);
        using var anonymous = PublicStatisticsDiscoveryTests.Anonymous(host);
        long version = 0;
        foreach (var limit in new[] { 1, 100 })
        {
            using var saved = await admin.PutAsJsonAsync("/api/admin/statistics/hall-of-fame",
                new SaveHallOfFameSettingsRequest(version++,
                [
                    new(item.Id, HallOfFameRankingMode.BestTime, true, limit, [HallOfFameColumn.SpeedOfBestResult]),
                    new(item.Id, HallOfFameRankingMode.MostPassings, true, limit,
                        [HallOfFameColumn.SpeedOfBestResult, HallOfFameColumn.PassingCount])
                ]));
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var results = (await anonymous.GetFromJsonAsync<HallOfFameDTO>("/api/statistics/hall-of-fame"))!;
            Assert.Equal(2, results.Sections.Count);
            Assert.All(results.Sections, section =>
            {
                Assert.Equal(limit, section.Entries.Count);
                Assert.All(section.Entries, entry => Assert.Equal(1, entry.Rank));
                Assert.Equal(Enumerable.Range(1, limit).Select(index => (double?)index),
                    section.Entries.Select(entry => entry.SpeedOfBestResult));
            });
        }
        Assert.Equal(0, host.Facilities.Calls);
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task HallOfFame_BestFieldsAreCoherentInBothModesWithExactPrecisionAndAllTieBreakers()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Precision", Description = "", Distance = 250 };
        var config = PublicStatisticsDiscoveryTests.Simple(item);
        var rider = Profile("Rider", "00000000-0000-0000-0000-000000000001");
        var other = Profile("Other", "00000000-0000-0000-0000-000000000002");
        var entries = new[]
        {
            Entry(5, config, rider, 10.000001, Completed(1), PublicStatisticsTestHost.Facility, 99),
            Entry(4, config, rider, 10, Completed(3), PublicStatisticsTestHost.Facility, 88),
            Entry(3, config, rider, 10, Completed(2), PublicStatisticsTestHost.OtherFacility, 77),
            Entry(2, config, rider, 10, Completed(2), PublicStatisticsTestHost.Facility, 66),
            Entry(1, config, rider, 10, Completed(2), PublicStatisticsTestHost.Facility, 12.5),
            Entry(6, config, other, 10.000001, Completed(1), PublicStatisticsTestHost.Facility, 11)
        };
        var samples = Enumerable.Range(1, 12).Select(index => new Sample
        {
            Id = Guid.NewGuid(), TransponderId = entries[0].TransponderId,
            CoursePointStartId = PublicStatisticsTestHost.Start, CoursePointEndId = PublicStatisticsTestHost.End,
            FacilityId = PublicStatisticsTestHost.Facility,
            TimeStart = Completed(0), TimeEnd = Completed(1), Duration = TimeSpan.FromSeconds(1),
            Distance = 250, Speed = 250
        });
        Assert.Equal(5, entries.Where(entry => entry.StatsProfile == rider).Select(entry => entry.TransponderId).Distinct().Count());
        await host.SeedAsync(new object[] { config }.Concat(entries).Concat(samples).ToArray());
        using var admin = Admin(host);
        var columns = Enum.GetValues<HallOfFameColumn>().Reverse().ToArray();
        using var save = await admin.PutAsJsonAsync("/api/admin/statistics/hall-of-fame",
            new SaveHallOfFameSettingsRequest(0,
            [
                new(item.Id, HallOfFameRankingMode.MostPassings, true, 10, columns),
                new(item.Id, HallOfFameRankingMode.BestTime, true, 10, columns)
            ]));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        var results = (await client.GetFromJsonAsync<HallOfFameDTO>("/api/statistics/hall-of-fame"))!;
        Assert.All(results.Sections, section =>
        {
            Assert.Equal(columns, section.Columns);
            var best = section.Entries[0];
            Assert.Equal("Rider", best.ProfileName);
            Assert.Equal(TimeSpan.FromSeconds(10), best.BestTime);
            Assert.Equal(12.5, best.SpeedOfBestResult);
            Assert.Equal(5L, best.PassingCount); // Completed entries across five transponders, not 12 raw samples.
            Assert.Equal("Velodrome", best.FacilityOfBestResult);
            Assert.Equal(new DateTimeOffset(Completed(2)), best.DateOfBestResult);
            Assert.Equal(TimeSpan.Zero, best.DateOfBestResult!.Value.Offset);
            Assert.Equal(new[] { 1, 2 }, section.Entries.Select(entry => entry.Rank));
            Assert.Equal(TimeSpan.FromTicks(100000010), section.Entries[1].BestTime);
        });
        Assert.Equal(1, host.Facilities.Calls); // One dependency call across both sections and all rows.
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task HallOfFame_NameOrderingIsUtf8BytewiseBeforeUuid()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Names", Description = "", Distance = 100 };
        var config = PublicStatisticsDiscoveryTests.Simple(item);
        var names = new[] { "Å", "a", "Z", "A" };
        await host.SeedAsync(new object[] { config }.Concat(names.Select((name, index) =>
            Entry(index + 1, config, Profile(name, $"00000000-0000-0000-0000-{index + 1:D12}"),
                10, Completed(1), PublicStatisticsTestHost.Facility, 10))).ToArray());
        using var admin = Admin(host);
        using var saved = await admin.PutAsJsonAsync("/api/admin/statistics/hall-of-fame",
            new SaveHallOfFameSettingsRequest(0, [new(item.Id, HallOfFameRankingMode.BestTime, true, 3, [])]));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        var results = (await client.GetFromJsonAsync<HallOfFameDTO>("/api/statistics/hall-of-fame"))!;
        Assert.Equal(new[] { "A", "Z", "a" }, Assert.Single(results.Sections).Entries.Select(entry => entry.ProfileName));
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task HallOfFame_FacilityFailuresAreExplicitAndUnselectedColumnsNeverCallDependency()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Facility", Description = "", Distance = 100 };
        var config = PublicStatisticsDiscoveryTests.Simple(item);
        await host.SeedAsync(config, Entry(1, config, Profile("Rider", Guid.NewGuid().ToString()),
            10, Completed(1), PublicStatisticsTestHost.Facility, 10));
        using var admin = Admin(host);
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        long version = 0;
        foreach (var columnSelected in new[] { false, true })
        {
            using var saved = await admin.PutAsJsonAsync("/api/admin/statistics/hall-of-fame",
                new SaveHallOfFameSettingsRequest(version++,
                    [new(item.Id, HallOfFameRankingMode.BestTime, true, 10,
                        columnSelected ? [HallOfFameColumn.FacilityOfBestResult] : [])]));
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            foreach (var status in new[] { HttpStatusCode.BadGateway, HttpStatusCode.ServiceUnavailable })
            {
                host.Facilities.StatusCode = status;
                using var response = await client.GetAsync("/api/statistics/hall-of-fame");
                Assert.Equal(columnSelected ? status : HttpStatusCode.OK, response.StatusCode);
                Assert.True(response.Headers.CacheControl?.NoStore);
            }
            Assert.Equal(columnSelected ? 2 : 0, host.Facilities.Calls);
        }
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task HallOfFame_ConcurrentReplacementsHaveExactlyOneWinnerAndDoNotLoseSectionOrder()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Concurrent", Description = "", Distance = 100 };
        await host.SeedAsync(item);
        using var first = Admin(host);
        using var second = Admin(host);
        var firstRequest = new SaveHallOfFameSettingsRequest(0,
            [new(item.Id, HallOfFameRankingMode.MostPassings, false, 1, [HallOfFameColumn.DateOfBestResult])]);
        var secondRequest = new SaveHallOfFameSettingsRequest(0,
            [new(item.Id, HallOfFameRankingMode.BestTime, true, 100, [HallOfFameColumn.PassingCount, HallOfFameColumn.BestTime])]);
        var responses = await Task.WhenAll(
            first.PutAsJsonAsync("/api/admin/statistics/hall-of-fame", firstRequest),
            second.PutAsJsonAsync("/api/admin/statistics/hall-of-fame", secondRequest));
        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            var winner = responses[0].StatusCode == HttpStatusCode.OK ? firstRequest : secondRequest;
            using var read = await first.GetAsync("/api/admin/statistics/hall-of-fame");
            Assert.True(read.Headers.CacheControl?.NoStore);
            var current = (await read.Content.ReadFromJsonAsync<HallOfFameSettingsDTO>())!;
            Assert.Equal(1, current.Version);
            var section = Assert.Single(current.Sections);
            Assert.Equal(winner.Sections[0].RankingMode, section.RankingMode);
            Assert.Equal(winner.Sections[0].Columns, section.Columns);
            Assert.Equal(winner.Sections[0].IsEnabled, section.IsEnabled);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task HallOfFame_PersistedStructuralErrorsAndMissingEnabledItemsFailExplicitly()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        using var admin = Admin(host);
        var section = new HallOfFameSectionSettingsDTO(Guid.NewGuid(), HallOfFameRankingMode.BestTime, true, 10, []);
        foreach (var nonArray in new[] { "{}", "null" })
        {
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => host.MutateAsync(async storage =>
                (await storage.HallOfFameSettingsDocuments.SingleAsync()).SectionsJson = nonArray));
            var postgres = Assert.IsType<Npgsql.PostgresException>(failure.InnerException);
            Assert.Equal("23514", postgres.SqlState);
            Assert.Equal("ck_hall_of_fame_settings_sections_array", postgres.ConstraintName);
        }
        var invalidDocuments = new[]
        {
            JsonSerializer.Serialize(new[] { section with { RowCount = 0 } }),
            JsonSerializer.Serialize(new[] { section with { RowCount = 101 } }),
            JsonSerializer.Serialize(new[] { section with { RankingMode = (HallOfFameRankingMode)99 } }),
            JsonSerializer.Serialize(new[] { section with { Columns = [HallOfFameColumn.BestTime, HallOfFameColumn.BestTime] } }),
            JsonSerializer.Serialize(new[] { section, section }),
            JsonSerializer.Serialize(new[] { section with { Columns = null! } }),
            JsonSerializer.Serialize(new[] { section with { StatisticsItemId = Guid.Empty } }),
            JsonSerializer.Serialize(new[] { section }) // Valid shape, missing enabled item.
        };
        foreach (var json in invalidDocuments)
        {
            await host.MutateAsync(async storage =>
                (await storage.HallOfFameSettingsDocuments.SingleAsync()).SectionsJson = json);
            using var response = await client.GetAsync("/api/statistics/hall-of-fame");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
        // Disabled stale references are not ranked, but item deletion still protects real references.
        await host.MutateAsync(async storage =>
            (await storage.HallOfFameSettingsDocuments.SingleAsync()).SectionsJson =
                JsonSerializer.Serialize(new[] { section with { IsEnabled = false } }));
        var disabled = (await client.GetFromJsonAsync<HallOfFameDTO>("/api/statistics/hall-of-fame"))!;
        Assert.Empty(disabled.Sections);
        Assert.Equal(0, host.Facilities.Calls);
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task HallOfFame_ConcurrentItemDeletionAndSettingsSaveCannotCreateDanglingReference()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        // Repeat independent item races; either ordering is valid, dangling settings are not.
        for (var index = 0; index < 4; index++)
        {
            var item = new StatisticsItem { Id = Guid.NewGuid(), Name = $"Race {index}", Description = "", Distance = 100 };
            await host.SeedAsync(item);
            using var saveClient = Admin(host);
            using var deleteClient = Admin(host);
            var before = (await saveClient.GetFromJsonAsync<HallOfFameSettingsDTO>("/api/admin/statistics/hall-of-fame"))!;
            var responses = await Task.WhenAll(
                saveClient.PutAsJsonAsync("/api/admin/statistics/hall-of-fame",
                    new SaveHallOfFameSettingsRequest(before.Version,
                        [new(item.Id, HallOfFameRankingMode.BestTime, index % 2 == 0, 10, [])])),
                deleteClient.DeleteAsync($"/api/statistics/admin/items/{item.Id}"));
            try
            {
                var save = responses[0].StatusCode;
                var delete = responses[1].StatusCode;
                Assert.True(
                    (save == HttpStatusCode.OK && delete == HttpStatusCode.Conflict) ||
                    (save == HttpStatusCode.BadRequest && delete == HttpStatusCode.NoContent),
                    $"Save={save}, delete={delete}");
                var after = (await saveClient.GetFromJsonAsync<HallOfFameSettingsDTO>("/api/admin/statistics/hall-of-fame"))!;
                await host.MutateAsync(async storage =>
                {
                    var ids = after.Sections.Select(section => section.StatisticsItemId).ToArray();
                    Assert.Equal(ids.Length, await storage.Set<StatisticsItem>().CountAsync(candidate => ids.Contains(candidate.Id)));
                });
            }
            finally { foreach (var response in responses) response.Dispose(); }
        }
    }

    private static HttpClient Admin(PublicStatisticsTestHost host)
    {
        var client = host.App.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "statistics-admin");
        return client;
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
        // Avoid the double-seconds factory truncating 10.000001 to 100000009 ticks
        // before Npgsql truncates intervals to PostgreSQL microsecond precision.
        Duration = TimeSpan.FromTicks((long)Math.Round(durationSeconds * TimeSpan.TicksPerSecond)),
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
