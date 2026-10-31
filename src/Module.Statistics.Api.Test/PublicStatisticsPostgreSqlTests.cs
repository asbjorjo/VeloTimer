using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class PublicStatisticsPostgreSqlTests
{
    private static readonly DateTime Completed = new DateTime(2026, 10, 31, 8, 47, 54, DateTimeKind.Utc)
        .AddTicks(1_234_560);

    [PublicStatisticsPostgreSqlFact]
    public async Task Results_AreIndividualPublicEntriesAndObserveVisibilityOnEveryRequest()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var config = Config();
        var publicProfile = Profile("Visible");
        var first = Entry(config, 1, publicProfile);
        var second = Entry(config, 2, publicProfile);
        var hidden = Entry(config, 3, Profile("Private secret", false));
        var unlinked = Entry(config, 4, null);
        await host.SeedAsync(first, second, hidden, unlinked);
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        using var response = await client.GetAsync(await Url(Request(config)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var page = (await response.Content.ReadFromJsonAsync<PublicStatisticsResultsPageDTO>())!;
        Assert.Equal(new[] { second.Id, first.Id }, page.Items.Select(row => row.Id));
        Assert.All(page.Items, row => Assert.Equal("Visible", row.ProfileName));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "items", "next", "previous" },
            json.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(new[] { "duration", "facilityId", "id", "profileName", "speedKmh",
                "statisticsItemConfigId", "statisticsItemId", "timeEnd" }.Order(),
            json.RootElement.GetProperty("items")[0].EnumerateObject().Select(property => property.Name).Order());
        await host.MutateAsync(async storage =>
        {
            var profile = await storage.Set<StatsProfile>().SingleAsync(p => p.UserId == publicProfile.UserId);
            profile.IsPublic = false;
        });
        var nowPrivate = await Get(client, Request(config));
        Assert.Empty(nowPrivate.Items);
        Assert.Null(nowPrivate.Previous);
        Assert.Null(nowPrivate.Next);
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task Results_ApplyAllInclusiveBoundsSelectionAndLiteralCaseInsensitiveSearchInSql()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var config = Config();
        var name = @"O'Brien 50%_A+B & C\D";
        var low = Entry(config, 1, Profile(name), duration: TimeSpan.FromSeconds(1), speedKmh: 36);
        var high = Entry(config, 2, Profile(name.ToUpperInvariant()), duration: TimeSpan.FromSeconds(2), speedKmh: 72);
        var otherConfig = PublicStatisticsDiscoveryTests.Simple(config.StatisticsItem);
        var otherItem = Config();
        await host.SeedAsync(low, high,
            Entry(config, 3, Profile(name), duration: TimeSpan.FromSeconds(1).Add(TimeSpan.FromTicks(-10))),
            Entry(config, 4, Profile(name), duration: TimeSpan.FromSeconds(2).Add(TimeSpan.FromTicks(10))),
            Entry(config, 5, Profile(name), speedKmh: 35.99),
            Entry(config, 6, Profile(name), speedKmh: 72.01),
            Entry(config, 7, Profile(name.Replace("%_", "XX"))),
            Entry(config, 8, Profile(name), facility: PublicStatisticsTestHost.OtherFacility),
            Entry(otherConfig, 9, Profile(name)),
            Entry(otherItem, 10, Profile(name)));
        host.Sql.Commands.Clear();
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        var request = Request(config) with
        {
            Name = "  " + name.ToLowerInvariant() + "  ",
            MinDuration = TimeSpan.FromSeconds(1), MaxDuration = TimeSpan.FromSeconds(2),
            MinSpeedKmh = 36, MaxSpeedKmh = 72
        };
        var page = await Get(client, request);
        Assert.Equal(new[] { high.Id, low.Id }, page.Items.Select(row => row.Id));
        Assert.Equal(72, page.Items[0].SpeedKmh, 9);
        Assert.Equal(36, page.Items[1].SpeedKmh, 9);
        Assert.Equal(TimeSpan.FromSeconds(2), page.Items[0].Duration);
        Assert.Equal(new DateTimeOffset(Completed.AddSeconds(2)), page.Items[0].TimeEnd);

        var commands = EntryCommands(host);
        Assert.NotEmpty(commands);
        var select = Assert.Single(commands, command => !command.Text.Contains("EXISTS", StringComparison.OrdinalIgnoreCase));
        foreach (var fragment in new[] { "JOIN", "is_public", "facility_id", "statistics_item_id",
            "statistics_item_config_id", "ILIKE", "ESCAPE", "duration >=", "duration <=", "speed >=", "speed <=",
            "ORDER BY", "time_end DESC", "id DESC", "LIMIT" })
            Assert.Contains(fragment, select.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(select.Parameters, value => value is int limit && limit == 26);
        Assert.Contains(select.Parameters, value => value is string pattern && pattern.Contains(@"\%") &&
            pattern.Contains(@"\_") && pattern.Contains(@"\\"));
        Assert.All(commands, command =>
        {
            Assert.DoesNotContain("OFFSET", command.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("COUNT(", command.Text, StringComparison.OrdinalIgnoreCase);
            foreach (var predicate in new[] { "is_public", "facility_id", "statistics_item_id",
                "statistics_item_config_id", "ILIKE", "duration >=", "duration <=", "speed >=", "speed <=" })
                Assert.Contains(predicate, command.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("transponder_id", command.Text, StringComparison.OrdinalIgnoreCase);
        });
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task Results_EqualZeroAndDayDurationBoundsAreInclusiveInPostgreSql()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var config = Config();
        var day = TimeSpan.FromDays(2) + new TimeSpan(3, 4, 5) + TimeSpan.FromMilliseconds(6);
        var zero = Entry(config, 1, Profile("Zero"), duration: TimeSpan.Zero, speedKmh: 0);
        var exact = Entry(config, 2, Profile("Day"), duration: day, speedKmh: 12.5);
        await host.SeedAsync(zero, exact,
            Entry(config, 3, Profile("Below"), duration: day.Subtract(TimeSpan.FromTicks(10)), speedKmh: 12.5),
            Entry(config, 4, Profile("Above"), duration: day.Add(TimeSpan.FromTicks(10)), speedKmh: 12.5),
            Entry(config, 5, Profile("Slow"), duration: day, speedKmh: 12.499),
            Entry(config, 6, Profile("Fast"), duration: day, speedKmh: 12.501));
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        var zeroPage = await Get(client, Request(config) with
        {
            MinDuration = TimeSpan.Zero, MaxDuration = TimeSpan.Zero, MinSpeedKmh = 0, MaxSpeedKmh = 0
        });
        Assert.Equal(zero.Id, Assert.Single(zeroPage.Items).Id);
        var dayPage = await Get(client, Request(config) with
        {
            MinDuration = day, MaxDuration = day, MinSpeedKmh = 12.5, MaxSpeedKmh = 12.5
        });
        Assert.Equal(exact.Id, Assert.Single(dayPage.Items).Id);
        Assert.Equal(day, dayPage.Items[0].Duration);
        Assert.Equal(12.5, dayPage.Items[0].SpeedKmh, 9);
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task Results_OrderCompositeCursorAcrossAdjacentMicrosecondsWithoutPrecisionLoss()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var config = Config();
        var rows = Enumerable.Range(1, 57).Select(index =>
            Entry(config, index, Profile("Rider"), completion: Completed.AddTicks(index / 3 * 10))).ToArray();
        await host.SeedAsync(rows.Cast<object>().ToArray());
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        var first = await Get(client, Request(config));
        var second = await Get(client, Request(config) with
        {
            Cursor = first.Next, Direction = PublicStatisticsPageDirection.Next
        });
        var last = await Get(client, Request(config) with
        {
            Cursor = second.Next, Direction = PublicStatisticsPageDirection.Next
        });
        var actual = first.Items.Concat(second.Items).Concat(last.Items).ToArray();
        var expected = rows.OrderByDescending(row => row.TimeEnd)
            .ThenByDescending(row => row.Id.ToString("D"), StringComparer.Ordinal).ToArray();
        Assert.Equal(expected.Select(row => row.Id), actual.Select(row => row.Id));
        Assert.Equal(expected.Select(row => row.TimeEnd.Ticks), actual.Select(row => row.TimeEnd.Ticks));
        var previous = await Get(client, Request(config) with
        {
            Cursor = last.Previous, Direction = PublicStatisticsPageDirection.Previous
        });
        Assert.Equal(second.Items, previous.Items);
        Assert.All(actual, row => Assert.Equal(0, row.TimeEnd.Ticks % 10));
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task Results_KeysetPagesUseNativeUuidTiesAndPreviousNextWithoutDuplicatesOrOmissions()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var config = Config();
        var rows = Enumerable.Range(1, 60).Select(index =>
            Entry(config, index, Profile("Rider"), completion: Completed)).ToArray();
        await host.SeedAsync(rows.Cast<object>().ToArray());
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        host.Sql.Commands.Clear();
        var first = await Get(client, Request(config));
        Assert.Equal(25, first.Items.Count);
        Assert.Null(first.Previous);
        Assert.NotNull(first.Next);
        Assert.Equal(Cursor(first.Items[^1]), first.Next);
        var second = await Get(client, Request(config) with { Cursor = first.Next, Direction = PublicStatisticsPageDirection.Next });
        Assert.Equal(25, second.Items.Count);
        Assert.NotNull(second.Previous);
        Assert.NotNull(second.Next);
        var third = await Get(client, Request(config) with { Cursor = second.Next, Direction = PublicStatisticsPageDirection.Next });
        Assert.Equal(10, third.Items.Count);
        Assert.NotNull(third.Previous);
        Assert.Null(third.Next);
        var actual = first.Items.Concat(second.Items).Concat(third.Items).Select(row => row.Id).ToArray();
        var expected = rows.Select(row => row.Id).OrderByDescending(id => id.ToString("D"), StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);
        Assert.Equal(60, actual.Distinct().Count());
        var back = await Get(client, Request(config) with { Cursor = third.Previous, Direction = PublicStatisticsPageDirection.Previous });
        Assert.Equal(second.Items, back.Items);
        var backFirst = await Get(client, Request(config) with { Cursor = back.Previous, Direction = PublicStatisticsPageDirection.Previous });
        Assert.Equal(first.Items, backFirst.Items);
        Assert.Null(backFirst.Previous);
        var commands = EntryCommands(host);
        Assert.Contains(commands, command => command.Text.Contains(") < (", StringComparison.Ordinal));
        Assert.Contains(commands, command => command.Text.Contains(") > (", StringComparison.Ordinal));
        Assert.All(commands, command =>
        {
            Assert.DoesNotContain("OFFSET", command.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("COUNT(", command.Text, StringComparison.OrdinalIgnoreCase);
            Assert.True(command.Text.Contains("LIMIT", StringComparison.OrdinalIgnoreCase) ||
                command.Text.Contains("EXISTS", StringComparison.OrdinalIgnoreCase), command.Text);
        });
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task Results_Exactly25HasNoNextBut26HasLookaheadAndLosslessCursor()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var config = Config();
        await host.SeedAsync(Enumerable.Range(1, 25).Select(index =>
            (object)Entry(config, index, Profile("Rider"), completion: Completed)).ToArray());
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        var exact = await Get(client, Request(config));
        Assert.Equal(25, exact.Items.Count);
        Assert.Null(exact.Next);
        await host.MutateAsync(async storage =>
        {
            var existing = await storage.Set<SimpleStatisticsItemConfig>().Include(item => item.StatisticsItem)
                .SingleAsync(item => item.Id == config.Id);
            storage.Add(Entry(existing, 26, Profile("Rider"), completion: Completed));
        });
        var over = await Get(client, Request(config));
        Assert.Equal(25, over.Items.Count);
        Assert.NotNull(over.Next);
        Assert.Equal(Completed.Ticks, over.Next.TimeEnd.Ticks);
        var final = await Get(client, Request(config) with { Cursor = over.Next, Direction = PublicStatisticsPageDirection.Next });
        Assert.Single(final.Items);
        Assert.Null(final.Next);
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task Results_CursorsRemainValidWhenAnchorDeletedOrMadePrivate()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var config = Config();
        var rows = Enumerable.Range(1, 51).Select(index => Entry(config, index, Profile("Rider"))).ToArray();
        await host.SeedAsync(rows.Cast<object>().ToArray());
        using var client = PublicStatisticsDiscoveryTests.Anonymous(host);
        var first = await Get(client, Request(config));
        var nextRequest = Request(config) with { Cursor = first.Next, Direction = PublicStatisticsPageDirection.Next };
        var before = await Get(client, nextRequest);
        await host.MutateAsync(async storage =>
        {
            var anchor = await storage.Set<StatisticsEntry>().Include(entry => entry.StatsProfile)
                .SingleAsync(entry => entry.Id == first.Next!.Id);
            anchor.StatsProfile!.IsPublic = false;
        });
        var privateAnchor = await Get(client, nextRequest);
        Assert.Equal(before.Items, privateAnchor.Items);
        await host.MutateAsync(async storage =>
            storage.Remove(await storage.Set<StatisticsEntry>().SingleAsync(entry => entry.Id == first.Next!.Id)));
        var deletedAnchor = await Get(client, nextRequest);
        Assert.Equal(before.Items, deletedAnchor.Items);
        var back = await Get(client, Request(config) with { Cursor = deletedAnchor.Previous, Direction = PublicStatisticsPageDirection.Previous });
        Assert.DoesNotContain(back.Items, row => row.Id == first.Next!.Id);
        Assert.Equal(first.Items.Take(24), back.Items);
    }

    private static SimpleStatisticsItemConfig Config() =>
        PublicStatisticsDiscoveryTests.Simple(new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap", Distance = 250 });
    private static StatsProfile Profile(string name, bool isPublic = true) =>
        new() { UserId = Guid.NewGuid(), ProfileName = name, IsPublic = isPublic };
    private static StatisticsEntry Entry(SimpleStatisticsItemConfig config, int index, StatsProfile? profile,
        TimeSpan? duration = null, double speedKmh = 54, Guid? facility = null, DateTime? completion = null) => new()
    {
        Id = Guid.Parse($"{index:x8}-0000-0000-0000-000000000001"),
        StatisticsItem = config.StatisticsItem, StatisticsItemConfig = config,
        FacilityId = facility ?? PublicStatisticsTestHost.Facility,
        StatsProfile = profile, TransponderId = Guid.NewGuid(),
        Duration = duration ?? TimeSpan.FromMilliseconds(1500),
        Speed = speedKmh / 3.6, TimeEnd = completion ?? Completed.AddSeconds(index),
        TimeStart = (completion ?? Completed.AddSeconds(index)).AddSeconds(-2)
    };
    private static GetPublicStatisticsResultsRequest Request(SimpleStatisticsItemConfig config) =>
        new(PublicStatisticsTestHost.Facility, config.StatisticsItem.Id, config.Id);
    private static PublicStatisticsCursorDTO Cursor(PublicStatisticsResultDTO row) => new(row.TimeEnd, row.Id);
    private static async Task<string> Url(GetPublicStatisticsResultsRequest request)
    {
        using var form = new FormUrlEncodedContent(PublicStatisticsQuery.ToQuery(request));
        return PublicStatisticsQuery.ResultsPath + "?" + await form.ReadAsStringAsync();
    }
    private static async Task<PublicStatisticsResultsPageDTO> Get(HttpClient client, GetPublicStatisticsResultsRequest request)
    {
        using var response = await client.GetAsync(await Url(request));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<PublicStatisticsResultsPageDTO>())!;
    }
    private static (string Text, object?[] Parameters)[] EntryCommands(PublicStatisticsTestHost host) =>
        host.Sql.Commands.Where(command => command.Text.Contains("statistics_entry", StringComparison.OrdinalIgnoreCase)).ToArray();
}
