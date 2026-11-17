using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Services;
using Api = VeloTime.Module.Statistics.Client;

namespace VeloTime.Platform.Test;

public sealed partial class WebUiBffTests
{
    [Theory]
    [InlineData(AdminStatisticsEntriesPageDirection.Next)]
    [InlineData(AdminStatisticsEntriesPageDirection.Previous)]
    public async Task ManualInvalidationBrowserBffRoundtripsFiltersPagingNoteAndAudit(AdminStatisticsEntriesPageDirection direction)
    {
        var entry = StatisticsManualInvalidationTransportTests.Entry();
        var cursor = new AdminStatisticsEntryCursorDTO(entry.TimeEnd, entry.Id);
        var request = new GetAdminStatisticsEntriesRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false, cursor, direction, 17);
        var page = new AdminStatisticsEntriesPageDTO([entry], cursor, cursor);
        var change = new StatisticsEntryManualInvalidationRequest(false, "Restore — reviewed ø");
        var administration = PlatformServiceTests.TestProxy.Create<IStatisticsAdministrationService>((method, args) =>
        {
            if (method.Name == nameof(IStatisticsAdministrationService.ListEntriesAsync))
            {
                Assert.Equal(request, args[0]);
                Assert.IsType<CancellationToken>(args[1]);
                return page;
            }
            Assert.Equal(nameof(IStatisticsAdministrationService.SetManualInvalidationAsync), method.Name);
            Assert.Equal(entry.Id, args[0]);
            Assert.Equal(change, args[1]);
            Assert.IsType<CancellationToken>(args[2]);
            return entry;
        });
        await using var app = await CreateAppAsync(administration: administration);
        using var client = CreateClient(app);
        await client.GetAsync("signin?role=statistics-admin");
        var browser = new StatisticsAdministrationHttpService(client);
        var result = await browser.ListEntriesAsync(request);
        Assert.Equal(page.Items, result.Items);
        Assert.Equal(page.Previous, result.Previous);
        Assert.Equal(page.Next, result.Next);
        Assert.Equal(entry, await browser.SetManualInvalidationAsync(entry.Id, change));
    }

    [Theory]
    [InlineData("user")]
    [InlineData("timing-admin")]
    [InlineData("facilities-admin")]
    public async Task ManualInvalidationBffRejectsOtherAdministratorRoles(string role)
    {
        var administration = PlatformServiceTests.TestProxy.Create<IStatisticsAdministrationService>((_, _) => throw new InvalidOperationException("must not call"));
        await using var app = await CreateAppAsync(administration: administration);
        using var client = CreateClient(app);
        await client.GetAsync($"signin?role={role}");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("api/admin/statistics/entries")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"api/admin/statistics/entries/{Guid.NewGuid()}/manual-invalidation", new StatisticsEntryManualInvalidationRequest(true))).StatusCode);
        Assert.Empty(((PlatformServiceTests.TestProxy)(object)administration).Calls);
    }

    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("isValid=no")]
    [InlineData("facilityId=invalid")]
    [InlineData("cursor=invalid&direction=next")]
    [InlineData("direction=previous")]
    [InlineData("pageSize=2&pageSize=3")]
    [InlineData("offset=0")]
    [InlineData("minSpeed=NaN")]
    [InlineData("maxSpeed=-1")]
    [InlineData("minDuration=00:00:02&maxDuration=-00:00:01")]
    [InlineData("minDuration=bad")]
    [InlineData("sort=speed&sort=duration")]
    [InlineData("maxSpeed=1&maxSpeed=2")]
    [InlineData("order=ASC")]
    public async Task ManualInvalidationBffRejectsInvalidQueriesBeforeService(string query)
    {
        var administration = PlatformServiceTests.TestProxy.Create<IStatisticsAdministrationService>((_, _) => throw new InvalidOperationException("must not call"));
        await using var app = await CreateAppAsync(administration: administration);
        using var client = CreateClient(app);
        await client.GetAsync("signin?role=statistics-admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("api/admin/statistics/entries?" + query)).StatusCode);
        Assert.Empty(((PlatformServiceTests.TestProxy)(object)administration).Calls);
    }

    [Fact]
    public async Task ManualInvalidationBffRequiresAntiforgeryAndPreservesRestoreConflict()
    {
        var administration = PlatformServiceTests.TestProxy.Create<IStatisticsAdministrationService>((_, _) =>
            throw new Api.ApiException("secret backend detail", 409, "secret backend payload", null!, null!));
        await using var app = await CreateAppAsync(administration: administration);
        using var unprotected = CreateClient(app, antiforgery: false);
        await unprotected.GetAsync("signin?role=statistics-admin");
        var url = $"api/admin/statistics/entries/{Guid.NewGuid()}/manual-invalidation";
        Assert.Equal(HttpStatusCode.BadRequest, (await unprotected.PutAsJsonAsync(url, new StatisticsEntryManualInvalidationRequest(false))).StatusCode);
        Assert.Empty(((PlatformServiceTests.TestProxy)(object)administration).Calls);
        using var client = CreateClient(app);
        await client.GetAsync("signin?role=statistics-admin");
        var response = await client.PutAsJsonAsync(url, new StatisticsEntryManualInvalidationRequest(false));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new StatisticsAdministrationHttpService(client)
            .SetManualInvalidationAsync(Guid.NewGuid(), new(false)));
        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
    }
}

public sealed class StatisticsManualInvalidationTransportTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("nb-NO")]
    [InlineData("ar-EG")]
    public void QueryUsesInvariantUtcCursorAndRoundtripsEverySelection(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var entry = Entry();
            var request = new GetAdminStatisticsEntriesRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false,
                new(entry.TimeEnd, entry.Id), AdminStatisticsEntriesPageDirection.Previous, 100);
            var encoded = StatisticsEntriesQuery.Encode(request);
            var values = encoded.Split('&').Select(part => part.Split('='))
                .ToDictionary(parts => parts[0], parts => (string?)Uri.UnescapeDataString(parts[1]));
            Assert.True(StatisticsEntriesQuery.TryParse(values, out var actual, out var errors));
            Assert.Empty(errors);
            Assert.Equal(request, actual);
            Assert.Equal("false", values["isValid"]);
            Assert.Equal("100", values["pageSize"]);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task ServerAdapterPreservesNegativeDurationSiAuditAndCancellationWithoutCaching()
    {
        var expected = Entry();
        var request = new GetAdminStatisticsEntriesRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false,
            new(default, expected.Id, AdminStatisticsEntriesSort.Speed, AdminStatisticsEntriesSortOrder.Ascending, Speed: "NaN"),
            AdminStatisticsEntriesPageDirection.Next, 17, TimeSpan.FromTicks(-19), TimeSpan.FromSeconds(2), 0.125, 25.75,
            AdminStatisticsEntriesSort.Speed, AdminStatisticsEntriesSortOrder.Ascending);
        var generated = new Api.AdminStatisticsEntryDTO
        {
            Id = expected.Id, TimeStart = expected.TimeStart, TimeEnd = expected.TimeEnd,
            StatisticsItemId = expected.StatisticsItemId, StatisticsItemName = expected.StatisticsItemName,
            StatisticsItemConfigId = expected.StatisticsItemConfigId,
            StatisticsItemConfigType = (int)StatisticsConfigurationType.Multi,
            FacilityId = expected.FacilityId, TransponderId = expected.TransponderId,
            StatsProfileUserId = expected.StatsProfileUserId, ProfileName = expected.ProfileName,
            Duration = expected.Duration.ToString("c", CultureInfo.InvariantCulture), SpeedMetresPerSecond = expected.SpeedMetresPerSecond,
            IsValid = false, InvalidReason = (int)StatisticsEntryInvalidReason.ManuallyInvalidated, IsManuallyInvalidated = true,
            ManualInvalidationChangedByUserId = expected.ManualInvalidationChangedByUserId,
            ManualInvalidationChangedAtUtc = expected.ManualInvalidationChangedAtUtc, ManualInvalidationNote = expected.ManualInvalidationNote
        };
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var client = PlatformServiceTests.TestProxy.Create<Api.IStatisticsClient>((method, args) =>
        {
            calls++;
            Assert.Equal(cancellation.Token, args[^1]);
            if (method.Name == "ListAdminStatisticsEntriesAsync")
            {
                Assert.Equal(request.FacilityId, args[0]);
                Assert.Equal(request.StatisticsItemId, args[1]);
                Assert.Equal(request.StatisticsItemConfigId, args[2]);
                Assert.Equal(request.TransponderId, args[3]);
                Assert.Equal(false, args[4]);
                Assert.Equal(StatisticsEntriesQuery.EncodeCursor(request.Cursor!), args[5]);
                Assert.Equal("next", args[6]);
                Assert.Equal(17, args[7]);
                Assert.Equal("-00:00:00.0000019", args[8]);
                Assert.Equal("00:00:02", args[9]);
                Assert.Equal("0.125", args[10]);
                Assert.Equal("25.75", args[11]);
                Assert.Equal("speed", args[12]);
                Assert.Equal("asc", args[13]);
                return new Api.AdminStatisticsEntriesPageDTO { Items = [generated],
                    Previous = new() { Id = expected.Id, Sort = 2, Order = 1, Speed = "NaN" }, Next = null };
            }
            Assert.Equal("SetStatisticsEntryManualInvalidationAsync", method.Name);
            Assert.Equal(expected.Id, args[0]);
            var body = Assert.IsType<Api.StatisticsEntryManualInvalidationRequest>(args[1]);
            Assert.False(body.IsManuallyInvalidated);
            Assert.Equal("Restore note", body.Note);
            return generated;
        });
        using var cache = new WebUiCacheTestHost(client);
        var service = cache.StatisticsAdministration(client);
        for (var i = 0; i < 2; i++)
        {
            var page = await service.ListEntriesAsync(request, cancellation.Token);
            Assert.Equal(expected, Assert.Single(page.Items));
            Assert.Equal(request.Cursor, page.Previous);
            Assert.Null(page.Next);
        }
        Assert.Equal(expected, await service.SetManualInvalidationAsync(expected.Id, new(false, "Restore note"), cancellation.Token));
        Assert.Equal(3, calls);
        cache.Authentication.User = WebUiCacheTestHost.Principal(admin: false);
        await Assert.ThrowsAsync<HttpRequestException>(() => service.ListEntriesAsync(request));
        await Assert.ThrowsAsync<HttpRequestException>(() => service.SetManualInvalidationAsync(expected.Id, new(true)));
        Assert.Equal(3, calls);
    }

    internal static AdminStatisticsEntryDTO Entry() => new(Guid.NewGuid(),
        new(2026, 11, 17, 8, 0, 1, TimeSpan.Zero), new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero),
        Guid.NewGuid(), "Double", Guid.NewGuid(), StatisticsConfigurationType.Multi,
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Rider", TimeSpan.FromSeconds(-1), -25,
        false, StatisticsEntryInvalidReason.ManuallyInvalidated, true, Guid.NewGuid(),
        new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero), "Review note");
}
