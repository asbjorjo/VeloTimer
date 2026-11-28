using System.Globalization;
using System.Net;
using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed partial class StatisticsManualInvalidationTests
{
    [PublicStatisticsPostgreSqlFact]
    public async Task EverySortWalksTiedPagesBidirectionallyAndRecoversWithCriteria()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var config = Config();
        var entries = Enumerable.Range(1, 11).Select(i => Entry(config, i,
            completed: Completed.AddSeconds((i - 1) / 4), duration: TimeSpan.FromSeconds(((i - 1) / 4 + 1) % 3 - 1),
            speed: (2 - (i - 1) / 4) * 2.5)).ToArray();
        await host.SeedAsync(entries.Cast<object>().ToArray());
        using var client = Admin(host);
        foreach (var sort in Enum.GetValues<AdminStatisticsEntriesSort>())
        foreach (var order in Enum.GetValues<AdminStatisticsEntriesSortOrder>())
        {
            var criteria = new GetAdminStatisticsEntriesRequest(StatisticsItemId: config.StatisticsItemId,
                PageSize: 3, MinDuration: TimeSpan.FromSeconds(-1), MaxSpeedMetresPerSecond: 5, Sort: sort, Order: order);
            var sorted = entries.OrderBy(row => sort switch
            {
                AdminStatisticsEntriesSort.Duration => row.Duration.Ticks,
                AdminStatisticsEntriesSort.Speed => row.Speed,
                _ => row.TimeEnd.Ticks
            }).ThenBy(row => row.Id).Select(row => row.Id).ToArray();
            if (order == AdminStatisticsEntriesSortOrder.Descending) Array.Reverse(sorted);
            var pages = new List<AdminStatisticsEntriesPageDTO>();
            var request = criteria;
            while (true)
            {
                var page = await Page(client, AdminStatisticsEntriesQuery.Encode(request));
                pages.Add(page);
                Assert.All(new[] { page.Previous, page.Next }.OfType<AdminStatisticsEntryCursorDTO>(), cursor =>
                { Assert.Equal(sort, cursor.Sort); Assert.Equal(order, cursor.Order); });
                if (page.Next is null) break;
                request = criteria with { Cursor = page.Next, Direction = AdminStatisticsEntriesPageDirection.Next };
                Assert.True(pages.Count < 6, "Keyset walk must terminate without repeating pages.");
            }
            Assert.Equal(sorted, pages.SelectMany(page => page.Items).Select(row => row.Id));
            for (var i = pages.Count - 1; i > 0; i--)
            {
                var back = await Page(client, AdminStatisticsEntriesQuery.Encode(criteria with
                { Cursor = pages[i].Previous, Direction = AdminStatisticsEntriesPageDirection.Previous }));
                Assert.Equal(pages[i - 1].Items, back.Items);
            }
            var terminal = pages[^1].Items[^1];
            var cursor = sort switch
            {
                AdminStatisticsEntriesSort.Duration => new AdminStatisticsEntryCursorDTO(default, terminal.Id, sort, order, terminal.Duration),
                AdminStatisticsEntriesSort.Speed => new(default, terminal.Id, sort, order, Speed: terminal.SpeedMetresPerSecond!.Value.ToString("R", CultureInfo.InvariantCulture)),
                _ => new(terminal.TimeEnd, terminal.Id, sort, order)
            };
            var recovered = await Page(client, AdminStatisticsEntriesQuery.Encode(criteria with
            { Cursor = cursor, Direction = AdminStatisticsEntriesPageDirection.Next }));
            Assert.Equal(pages[0].Items, recovered.Items);
        }
        Assert.Equal((await Page(client)).Items, (await Page(client, "sort=timeEnd&order=desc")).Items);
        Assert.DoesNotContain(host.Sql.Commands, command => command.Text.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task SignedSubmicrosecondInclusiveBoundsAndNonfiniteCursorsStayJsonSafe()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var config = Config();
        var speeds = new[] { double.NegativeInfinity, 0, 2.5, 2.5, double.PositiveInfinity, double.NaN, double.NaN };
        var rows = speeds.Select((speed, i) => Entry(config, i + 1,
            speed: speed, duration: TimeSpan.FromTicks((i - 3) * 10))).ToArray();
        await host.SeedAsync(rows.Cast<object>().ToArray());
        using var client = Admin(host);
        var bounded = await Page(client, "minDuration=-00:00:00.0000019&maxDuration=00:00:00.0000009");
        Assert.Equal(new[] { rows[2].Id, rows[3].Id }.Order(), bounded.Items.Select(row => row.Id).Order());
        Assert.Equal(rows[3].Id, Assert.Single((await Page(client, "minDuration=00:00:00&maxDuration=00:00:00")).Items).Id);
        foreach (var bound in new[] { "minSpeed=0", "maxSpeed=2.5", "minSpeed=2.5&maxSpeed=2.5" })
        {
            var page = await Page(client, bound);
            Assert.All(page.Items, row => Assert.True(double.IsFinite(row.SpeedMetresPerSecond!.Value)));
            Assert.Equal(bound.Contains("minSpeed=2.5") ? 2 : 3, page.Items.Count);
        }
        foreach (var order in Enum.GetValues<AdminStatisticsEntriesSortOrder>())
        {
            var request = new GetAdminStatisticsEntriesRequest(PageSize: 1, Sort: AdminStatisticsEntriesSort.Speed, Order: order);
            var seen = new List<Guid>();
            AdminStatisticsEntriesPageDTO? previous = null;
            while (true)
            {
                var page = await Page(client, AdminStatisticsEntriesQuery.Encode(request));
                seen.Add(Assert.Single(page.Items).Id);
                if (previous is not null)
                {
                    var back = await Page(client, AdminStatisticsEntriesQuery.Encode(request with
                    { Cursor = page.Previous, Direction = AdminStatisticsEntriesPageDirection.Previous }));
                    Assert.Equal(previous.Items, back.Items);
                }
                previous = page;
                if (page.Next is null) break;
                Assert.NotNull(page.Next.Speed);
                Assert.Equal(page.Next, System.Text.Json.JsonSerializer.Deserialize<AdminStatisticsEntryCursorDTO>(System.Text.Json.JsonSerializer.Serialize(page.Next)));
                request = request with { Cursor = page.Next, Direction = AdminStatisticsEntriesPageDirection.Next };
                Assert.True(seen.Count < 8);
            }
            var expected = rows.Select(row => row.Id).ToArray();
            if (order == AdminStatisticsEntriesSortOrder.Descending) Array.Reverse(expected);
            Assert.Equal(expected, seen);
        }
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task InvalidatingEntireLastPageRecoversWithinAppliedBoundsAndSort()
    {
        foreach (var sort in new[] { AdminStatisticsEntriesSort.Duration, AdminStatisticsEntriesSort.Speed })
        foreach (var order in Enum.GetValues<AdminStatisticsEntriesSortOrder>())
        {
            await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
            var config = Config();
            var entries = Enumerable.Range(1, 5).Select(i => Entry(config, i)).ToArray();
            // A newer out-of-range entry must not leak into recovery.
            var outside = Entry(config, 6, duration: TimeSpan.FromSeconds(30), speed: 50);
            await host.SeedAsync(entries.Cast<object>().Append(outside).ToArray());
            using var client = Admin(host);
            var criteria = new GetAdminStatisticsEntriesRequest(IsValid: true, PageSize: 3,
                MinDuration: TimeSpan.FromSeconds(10), MaxDuration: TimeSpan.FromSeconds(10),
                MinSpeedMetresPerSecond: 25, MaxSpeedMetresPerSecond: 25, Sort: sort, Order: order);
            var first = await Page(client, AdminStatisticsEntriesQuery.Encode(criteria));
            var query = AdminStatisticsEntriesQuery.Encode(criteria with
            { Cursor = first.Next, Direction = AdminStatisticsEntriesPageDirection.Next });
            var last = await Page(client, query);
            Assert.Equal(2, last.Items.Count);
            foreach (var row in last.Items) await Set(client, row.Id, true, "Recovery regression");
            var recovered = await Page(client, query);
            Assert.Equal(first.Items, recovered.Items);
            Assert.Null(recovered.Previous);
            Assert.Null(recovered.Next);
        }
    }

    [Theory]
    [InlineData("sort=Duration")]
    [InlineData("order=ascending")]
    [InlineData("sort=speed&sort=duration")]
    [InlineData("minDuration=oops")]
    [InlineData("maxDuration=")]
    [InlineData("minDuration=00:00:02&maxDuration=-00:00:01")]
    [InlineData("minSpeed=NaN")]
    [InlineData("maxSpeed=Infinity")]
    [InlineData("minSpeed=-0.1")]
    [InlineData("minSpeed=3&maxSpeed=2")]
    [InlineData("minSpeed=1&minSpeed=2")]
    [InlineData("sort=duration&order=asc&cursor=v1%7Cspeed%7Casc%7C2.5%7C80000000-0000-0000-0000-000000000001&direction=next")]
    [InlineData("sort=duration&order=asc&cursor=v1%7Cduration%7Cdesc%7C00:00:01%7C80000000-0000-0000-0000-000000000001&direction=next")]
    [InlineData("sort=duration&order=asc&cursor=v1%7Cduration%7Casc%7C00:00:00.0000001%7C80000000-0000-0000-0000-000000000001&direction=next")]
    public async Task NewBoundsAndCursorMismatchesReturn400(string query)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        using var client = Admin(host);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Root + "?" + query)).StatusCode);
    }
}
