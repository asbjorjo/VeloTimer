using System.Globalization;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.Platform.Test;

public sealed partial class WebUiBffTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("nb-NO")]
    [InlineData("ar-EG")]
    public async Task BrowserBffRoundtripsEverySortOrderAndJsonSafeCursor(string locale)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale);
            GetAdminStatisticsEntriesRequest? expected = null;
            var entry = StatisticsManualInvalidationTransportTests.Entry();
            var administration = PlatformServiceTests.TestProxy.Create<IStatisticsAdministrationService>((method, args) =>
            {
                Assert.Equal(nameof(IStatisticsAdministrationService.ListEntriesAsync), method.Name);
                Assert.Equal(expected, args[0]);
                return new AdminStatisticsEntriesPageDTO([entry], expected!.Cursor, expected.Cursor);
            });
            await using var app = await CreateAppAsync(administration: administration);
            using var client = CreateClient(app);
            await client.GetAsync("signin?role=statistics-admin");
            var browser = new StatisticsAdministrationHttpService(client);
            foreach (var sort in Enum.GetValues<AdminStatisticsEntriesSort>())
            foreach (var order in Enum.GetValues<AdminStatisticsEntriesSortOrder>())
            foreach (var speed in sort == AdminStatisticsEntriesSort.Speed ? new[] { "-Infinity", "2.5", "Infinity", "NaN" } : new[] { "unused" })
            foreach (var direction in Enum.GetValues<AdminStatisticsEntriesPageDirection>())
            {
                var cursor = sort switch
                {
                    AdminStatisticsEntriesSort.Duration => new AdminStatisticsEntryCursorDTO(default, entry.Id, sort, order, TimeSpan.FromTicks(-20)),
                    AdminStatisticsEntriesSort.Speed => new(default, entry.Id, sort, order, Speed: speed),
                    _ => new(entry.TimeEnd, entry.Id, sort, order)
                };
                expected = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false, cursor, direction, 7,
                    TimeSpan.FromTicks(-19), TimeSpan.Zero, 0.125, 27.5, sort, order);
                var query = StatisticsEntriesQuery.Encode(expected);
                Assert.Contains("minSpeed=0.125", query);
                Assert.Contains("maxSpeed=27.5", query);
                var text = StatisticsEntriesQuery.EncodeCursor(cursor);
                Assert.Equal(sort == AdminStatisticsEntriesSort.TimeEnd && order == AdminStatisticsEntriesSortOrder.Descending,
                    !text.StartsWith("v1|", StringComparison.Ordinal));
                var page = await browser.ListEntriesAsync(expected);
                Assert.Equal(cursor, page.Next);
                Assert.Equal(cursor, page.Previous);
                Assert.Equal(entry, Assert.Single(page.Items));
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
