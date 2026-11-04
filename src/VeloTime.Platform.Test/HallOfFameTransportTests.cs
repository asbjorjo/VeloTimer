using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VeloTime.Module.Statistics.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Services;
using C = VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.Platform.Test;

public sealed class HallOfFameTransportTests
{
    internal static readonly Guid Item = Guid.Parse("10000000-0000-0000-0000-000000000001");
    internal static readonly C.HallOfFameEntryDTO FullEntry = new(1, "<Rider>", TimeSpan.FromTicks(123456789),
        12.5, (long)int.MaxValue + 123, "Arena", new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero).AddTicks(1234567));
    internal static C.HallOfFameDTO Results() => new([
        new(Item, "Count first", C.HallOfFameRankingMode.MostPassings, 10,
            [C.HallOfFameColumn.PassingCount, C.HallOfFameColumn.DateOfBestResult, C.HallOfFameColumn.BestTime,
             C.HallOfFameColumn.SpeedOfBestResult, C.HallOfFameColumn.FacilityOfBestResult], [FullEntry]),
        new(Item, "Time second", C.HallOfFameRankingMode.BestTime, 1, [], [new(1, "Rider", null, null, null, null, null)])
    ]);

    [Fact]
    public async Task ServerTransport_PreservesOrderedEnumsNullableColumnsPrecisionSiUtcAndLongCountsWithoutLookups()
    {
        var expected = Results();
        using var cancellation = new CancellationTokenSource();
        var client = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((method, args) =>
        {
            Assert.Equal("GetHallOfFameAsync", method.Name);
            Assert.Equal(cancellation.Token, Assert.Single(args));
            return new HallOfFameDTO
            {
                Sections = expected.Sections.Select(section => new HallOfFameSectionDTO
                {
                    StatisticsItemId = section.StatisticsItemId, StatisticsItemName = section.StatisticsItemName,
                    RankingMode = (int)section.RankingMode, RowCount = section.RowCount,
                    Columns = section.Columns.Select(column => (int)column).ToArray(),
                    Entries = section.Entries.Select(row => new HallOfFameEntryDTO
                    {
                        Rank = row.Rank, ProfileName = row.ProfileName,
                        BestTime = row.BestTime?.ToString("c", CultureInfo.InvariantCulture),
                        SpeedOfBestResult = row.SpeedOfBestResult, PassingCount = row.PassingCount,
                        FacilityOfBestResult = row.FacilityOfBestResult, DateOfBestResult = row.DateOfBestResult
                    }).ToArray()
                }).ToArray()
            };
        });
        using var cache = new WebUiCacheTestHost(client);
        var service = new StatisticsService(client, cache.Cache);
        EqualResults(expected, await service.GetHallOfFameAsync(cancellation.Token));
        EqualResults(expected, await service.GetHallOfFameAsync(cancellation.Token));
        Assert.Equal(2, ((PlatformServiceTests.TestProxy)(object)client).Calls.Count);
    }

    [Fact]
    public async Task BrowserTransport_PreservesExactPublicShapeIncludingNullsAndNoRiderIdentifiers()
    {
        var expected = Results();
        using var handler = new Handler(async request =>
        {
            Assert.Equal("/api/statistics/hall-of-fame", request.RequestUri!.AbsolutePath);
            Assert.Equal(HttpMethod.Get, request.Method);
            await Task.Yield();
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(expected) };
        });
        using var http = new HttpClient(handler) { BaseAddress = new("https://webui.invalid") };
        var result = await new StatisticsHttpService(http).GetHallOfFameAsync();
        EqualResults(expected, result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var row = json.RootElement.GetProperty("sections")[0].GetProperty("entries")[0];
        Assert.Equal(new[] { "rank", "profileName", "bestTime", "speedOfBestResult", "passingCount",
            "facilityOfBestResult", "dateOfBestResult" }, row.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task ServerAdministration_RoundTripsLongVersionDisabledSectionsAndColumnOrder()
    {
        const long version = (long)int.MaxValue + 100;
        var sections = new C.HallOfFameSectionSettingsDTO[]
        {
            new(Item, C.HallOfFameRankingMode.MostPassings, false, 100,
                [C.HallOfFameColumn.DateOfBestResult, C.HallOfFameColumn.PassingCount]),
            new(Item, C.HallOfFameRankingMode.BestTime, true, 1, [])
        };
        using var cancellation = new CancellationTokenSource();
        var client = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((method, args) =>
        {
            var saving = method.Name == "ReplaceHallOfFameSettingsAsync";
            Assert.Equal(cancellation.Token, args[^1]);
            if (saving)
            {
                var body = Assert.IsType<SaveHallOfFameSettingsRequest>(args[0]);
                Assert.Equal(version, body.ExpectedVersion);
                Assert.Equal(new[] { 1, 0 }, body.Sections.Select(section => section.RankingMode));
                Assert.False(body.Sections.First().IsEnabled);
                Assert.Equal(new[] { 4, 2 }, body.Sections.First().Columns);
            }
            else Assert.Equal("GetHallOfFameSettingsAsync", method.Name);
            return new HallOfFameSettingsDTO
            {
                Version = version + (saving ? 1 : 0),
                Sections = sections.Select(section => new HallOfFameSectionSettingsDTO
                {
                    StatisticsItemId = section.StatisticsItemId, RankingMode = (int)section.RankingMode,
                    RowCount = section.RowCount, IsEnabled = section.IsEnabled,
                    Columns = section.Columns.Select(column => (int)column).ToArray()
                }).ToArray()
            };
        });
        using var cache = new WebUiCacheTestHost(client);
        var service = new StatisticsAdministrationService(client, cache.Cache);
        Assert.Equal(version, (await service.GetHallOfFameSettingsAsync(cancellation.Token)).Version);
        var saved = await service.SaveHallOfFameSettingsAsync(new(version, sections), cancellation.Token);
        Assert.Equal(version + 1, saved.Version);
        Assert.Equal(sections[0].Columns, saved.Sections[0].Columns);
        Assert.False(saved.Sections[0].IsEnabled);
    }

    [Fact]
    public async Task ServerAdministration_PreservesStaleConflictWithoutLeakingUpstreamPayload()
    {
        var client = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((_, _) =>
            throw new ApiException("private upstream", 409, "secret body", null!, null!));
        using var cache = new WebUiCacheTestHost(client);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new StatisticsAdministrationService(client, cache.Cache).SaveHallOfFameSettingsAsync(new(0, [])));
        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.DoesNotContain("secret", error.Message);
    }

    internal static void EqualResults(C.HallOfFameDTO expected, C.HallOfFameDTO actual)
    {
        Assert.Equal(expected.Sections.Count, actual.Sections.Count);
        for (var index = 0; index < expected.Sections.Count; index++)
        {
            var left = expected.Sections[index]; var right = actual.Sections[index];
            Assert.Equal(left.StatisticsItemId, right.StatisticsItemId);
            Assert.Equal(left.StatisticsItemName, right.StatisticsItemName);
            Assert.Equal(left.RankingMode, right.RankingMode);
            Assert.Equal(left.RowCount, right.RowCount);
            Assert.Equal(left.Columns, right.Columns);
            Assert.Equal(left.Entries, right.Entries);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request);
    }
}
