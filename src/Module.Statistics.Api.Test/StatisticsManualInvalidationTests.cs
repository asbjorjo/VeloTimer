using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed partial class StatisticsManualInvalidationTests
{
    private const string Root = "/api/statistics/admin/entries";
    private static readonly Guid Actor = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly DateTime Completed = new(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    [InlineData("timing-admin", HttpStatusCode.Forbidden)]
    [InlineData("facilities-admin", HttpStatusCode.Forbidden)]
    public async Task ListAndMutationRequireStatisticsAdministration(string? role, HttpStatusCode expected)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        using var client = host.App.CreateClient();
        if (role is null) client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        else client.DefaultRequestHeaders.Add("X-Test-Role", role);
        Assert.Equal(expected, (await client.GetAsync(Root)).StatusCode);
        Assert.Equal(expected, (await client.PutAsJsonAsync(Url(Guid.NewGuid()), new StatisticsEntryManualInvalidationRequest(true))).StatusCode);
    }

    [Theory]
    [InlineData("unsupported=1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("pageSize=abc")]
    [InlineData("pageSize=1&pageSize=2")]
    [InlineData("facilityId=00000000-0000-0000-0000-000000000000")]
    [InlineData("statisticsItemId=not-a-uuid")]
    [InlineData("statisticsItemConfigId=")]
    [InlineData("transponderId=invalid")]
    [InlineData("isValid=maybe")]
    [InlineData("isValid=true&isValid=false")]
    [InlineData("cursor=invalid&direction=next")]
    [InlineData("direction=previous")]
    [InlineData("direction=NEXT")]
    public async Task ListRejectsMalformedDuplicateAndUnsupportedQueries(string query)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        using var client = Admin(host);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Root + "?" + query)).StatusCode);
    }

    [Fact]
    public async Task MutationRejectsOversizeNoteMissingBodyAndNonUuidActor()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        using var client = Admin(host);
        var response = await client.PutAsJsonAsync(Url(Guid.NewGuid()), new StatisticsEntryManualInvalidationRequest(true, new string('x', 501)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("note", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync(Url(Guid.NewGuid()), null)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-Subject");
        client.DefaultRequestHeaders.Add("X-Test-Subject", "not-a-uuid");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(Url(Guid.NewGuid()), new StatisticsEntryManualInvalidationRequest(true))).StatusCode);
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task MarkRestorePersistsLatestAuditUtcSiAndRecomputesDependentMulti()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var config = Config();
        var entry = Entry(config, 1);
        var multiItem = new StatisticsItem { Id = Guid.NewGuid(), Name = "Double", Distance = 500 };
        var multi = new MultiStatisticsItemConfig { Id = Guid.NewGuid(), StatisticsItem = multiItem,
            StatisticsItemId = multiItem.Id, ParentConfig = config, ParentConfigId = config.Id, Repetitions = 2 };
        await host.SeedAsync(entry, multi);
        using var client = Admin(host);
        var original = Assert.Single((await Page(client)).Items);
        Assert.Null(original.ManualInvalidationChangedByUserId);
        Assert.Null(original.ManualInvalidationChangedAtUtc);
        Assert.Null(original.ManualInvalidationNote);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var note = new string('n', 500);
        var marked = await Set(client, entry.Id, true, note);
        Assert.False(marked.IsValid);
        Assert.True(marked.IsManuallyInvalidated);
        Assert.Equal(StatisticsEntryInvalidReason.ManuallyInvalidated, marked.InvalidReason);
        Assert.Equal(Actor, marked.ManualInvalidationChangedByUserId);
        Assert.Equal(note, marked.ManualInvalidationNote);
        Assert.InRange(marked.ManualInvalidationChangedAtUtc!.Value, before, DateTimeOffset.UtcNow);
        Assert.Equal(TimeSpan.Zero, marked.ManualInvalidationChangedAtUtc.Value.Offset);
        Assert.Equal(TimeSpan.Zero, marked.TimeStart.Offset);
        Assert.Equal(TimeSpan.Zero, marked.TimeEnd.Offset);
        Assert.Equal(TimeSpan.FromSeconds(10), marked.Duration);
        Assert.Equal(25d, marked.SpeedMetresPerSecond);
        // A validity change marks the entry's range dirty so the worker re-derives Multi contributor validity.
        var firstRequests = await Requests(host, entry.TransponderId);
        Assert.Equal(1, firstRequests);
        AssertPersisted(marked, Assert.Single((await Page(client)).Items));
        AssertMeasurements(original, marked);
        var otherActor = Guid.NewGuid();
        client.DefaultRequestHeaders.Remove("X-Test-Subject");
        client.DefaultRequestHeaders.Add("X-Test-Subject", otherActor.ToString());
        var remarked = await Set(client, entry.Id, true, "Reviewed again");
        Assert.Equal(otherActor, remarked.ManualInvalidationChangedByUserId);
        Assert.Equal("Reviewed again", remarked.ManualInvalidationNote);
        Assert.True(remarked.ManualInvalidationChangedAtUtc >= marked.ManualInvalidationChangedAtUtc);
        Assert.Equal(firstRequests, await Requests(host, entry.TransponderId));
        var restored = await Set(client, entry.Id, false, "Restored after review");
        Assert.True(restored.IsValid);
        Assert.False(restored.IsManuallyInvalidated);
        Assert.Null(restored.InvalidReason);
        Assert.Equal(otherActor, restored.ManualInvalidationChangedByUserId);
        Assert.Equal("Restored after review", restored.ManualInvalidationNote);
        Assert.True(restored.ManualInvalidationChangedAtUtc >= remarked.ManualInvalidationChangedAtUtc);
        AssertPersisted(restored, Assert.Single((await Page(client)).Items));
        AssertMeasurements(original, restored);
        Assert.Equal(2, await Requests(host, entry.TransponderId));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(Url(entry.Id), new StatisticsEntryManualInvalidationRequest(false, "Must not overwrite audit"))).StatusCode);
        AssertPersisted(restored, Assert.Single((await Page(client)).Items));
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task AnyAutomaticStateCanBeMarkedAndRestoreUsesCurrentBoundsAndCheckpointEvidence()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var config = Config();
        var entries = new List<StatisticsEntry>();
        foreach (var reason in new StatisticsEntryInvalidReason?[] { null, StatisticsEntryInvalidReason.NonPositiveDuration,
            StatisticsEntryInvalidReason.BelowMinimumDuration, StatisticsEntryInvalidReason.AboveMaximumDuration,
            StatisticsEntryInvalidReason.RequiredCoursePointsNotSatisfied })
        {
            var entry = Entry(config, entries.Count + 1, reason,
                checkpoints: reason != StatisticsEntryInvalidReason.RequiredCoursePointsNotSatisfied);
            entries.Add(entry);
        }
        await host.SeedAsync(entries.Cast<object>().ToArray());
        using var client = Admin(host);
        foreach (var entry in entries)
        {
            var before = (await Page(client)).Items.Single(row => row.Id == entry.Id);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(Url(entry.Id), new StatisticsEntryManualInvalidationRequest(false, "Rejected"))).StatusCode);
            Assert.Equal(before, (await Page(client)).Items.Single(row => row.Id == entry.Id));
            await Set(client, entry.Id, true, null);
        }
        await host.MutateAsync(async db => (await db.Set<StatisticsItem>().SingleAsync()).MinDuration = TimeSpan.FromSeconds(20));
        foreach (var entry in entries)
        {
            var restored = await Set(client, entry.Id, false, null);
            Assert.False(restored.IsValid);
            Assert.False(restored.IsManuallyInvalidated);
            Assert.Equal(entry.Duration <= TimeSpan.Zero ? StatisticsEntryInvalidReason.NonPositiveDuration
                : !entry.RequiredCoursePointsSatisfied ? StatisticsEntryInvalidReason.RequiredCoursePointsNotSatisfied
                : StatisticsEntryInvalidReason.BelowMinimumDuration, restored.InvalidReason);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(Url(Guid.NewGuid()), new StatisticsEntryManualInvalidationRequest(true))).StatusCode);
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task ListFiltersAndBidirectionalKeysetsIncludeInvalidPrivateAndUnlinkedEntries()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var config = Config();
        var entries = Enumerable.Range(1, 5).Select(i => Entry(config, i)).ToArray();
        entries[1].IsValid = false;
        entries[1].InvalidReason = StatisticsEntryInvalidReason.AboveMaximumDuration;
        entries[2].StatsProfile!.IsPublic = false;
        entries[3].StatsProfile = null;
        await host.SeedAsync(entries.Cast<object>().ToArray());
        using var client = Admin(host);
        // All rows have the same timestamp: native UUID ordering is the required tiebreaker.
        var first = await Page(client, "pageSize=2");
        Assert.Equal(new[] { entries[4].Id, entries[3].Id }, first.Items.Select(row => row.Id));
        Assert.Null(first.Previous);
        Assert.NotNull(first.Next);
        var second = await Page(client, Cursor(first.Next!, "next"));
        Assert.Equal(new[] { entries[2].Id, entries[1].Id }, second.Items.Select(row => row.Id));
        Assert.NotNull(second.Previous);
        Assert.NotNull(second.Next);
        var last = await Page(client, Cursor(second.Next!, "next"));
        Assert.Equal(entries[0].Id, Assert.Single(last.Items).Id);
        Assert.Null(last.Next);
        Assert.NotNull(last.Previous);
        var back = await Page(client, Cursor(last.Previous!, "previous"));
        Assert.Equal(second.Items, back.Items);
        var backToFirst = await Page(client, Cursor(back.Previous!, "previous"));
        Assert.Equal(first.Items, backToFirst.Items);
        Assert.Null(backToFirst.Previous);
        Assert.Equal(entries[1].Id, Assert.Single((await Page(client, "isValid=false")).Items).Id);
        Assert.Equal(4, (await Page(client, "isValid=true")).Items.Count);
        var query = $"facilityId={entries[0].FacilityId}&statisticsItemId={config.StatisticsItemId}&statisticsItemConfigId={config.Id}&transponderId={entries[0].TransponderId}&isValid=true";
        Assert.Equal(entries[0].Id, Assert.Single((await Page(client, query)).Items).Id);
        foreach (var filter in new[] { "facilityId", "statisticsItemId", "statisticsItemConfigId", "transponderId" })
            Assert.Empty((await Page(client, $"{filter}={Guid.NewGuid()}")).Items);
        var raw = await client.GetStringAsync(Root);
        using var json = JsonDocument.Parse(raw);
        Assert.Equal(new[] { "items", "next", "previous" }, json.RootElement.EnumerateObject().Select(p => p.Name).Order());
        Assert.DoesNotContain("totalCount", raw);
        Assert.Contains(host.Sql.Commands, command => command.Text.Contains("LIMIT"));
        Assert.DoesNotContain(host.Sql.Commands, command => command.Text.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task InvalidatingLastMatchingPageRecoversToFirstFilteredPage()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var config = Config();
        var newest = Entry(config, 1);
        var older = Entry(config, 2, completed: Completed.AddMinutes(-1));
        await host.SeedAsync(newest, older);
        using var client = Admin(host);

        var first = await Page(client, "isValid=true&pageSize=1");
        Assert.Equal(newest.Id, Assert.Single(first.Items).Id);
        Assert.Null(first.Previous);
        var anchor = Assert.IsType<AdminStatisticsEntryCursorDTO>(first.Next);
        Assert.Equal(newest.Id, anchor.Id);
        Assert.Equal(new DateTimeOffset(newest.TimeEnd), anchor.TimeEnd);
        var pageTwoQuery = "isValid=true&" + Cursor(anchor, "next", pageSize: 1);
        var second = await Page(client, pageTwoQuery);
        Assert.Equal(older.Id, Assert.Single(second.Items).Id);
        Assert.NotNull(second.Previous);
        Assert.Null(second.Next);

        var invalidated = await Set(client, older.Id, true, "Review");
        Assert.False(invalidated.IsValid);
        Assert.True(invalidated.IsManuallyInvalidated);
        var recovered = await Page(client, pageTwoQuery);
        var survivor = Assert.Single(recovered.Items);
        Assert.Equal(newest.Id, survivor.Id);
        Assert.True(survivor.IsValid);
        Assert.Null(recovered.Previous);
        Assert.Null(recovered.Next);
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task ManualInvalidationIsImmediatelyExcludedFromPublicResultsAndBestResults()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var config = Config();
        var entry = Entry(config, 1);
        await host.SeedAsync(entry);
        using var admin = Admin(host);
        using var publicClient = PublicStatisticsDiscoveryTests.Anonymous(host);
        var results = $"/api/statistics/public/results?facilityId={entry.FacilityId}&itemId={config.StatisticsItemId}&configurationId={config.Id}";
        Assert.Single((await publicClient.GetFromJsonAsync<PublicStatisticsResultsPageDTO>(results))!.Items);
        Assert.Single((await publicClient.GetFromJsonAsync<List<BestStatisticsResultDTO>>("/api/statistics/best-results"))!);
        Assert.Single((await publicClient.GetFromJsonAsync<HallOfFameDTO>("/api/statistics/hall-of-fame?category=lap"))!.Times);
        await Set(admin, entry.Id, true, "Exclude");
        Assert.Empty((await publicClient.GetFromJsonAsync<PublicStatisticsResultsPageDTO>(results))!.Items);
        Assert.Empty((await publicClient.GetFromJsonAsync<List<BestStatisticsResultDTO>>("/api/statistics/best-results"))!);
        Assert.Empty((await publicClient.GetFromJsonAsync<HallOfFameDTO>("/api/statistics/hall-of-fame?category=lap"))!.Times);
        await Set(admin, entry.Id, false, "Include again");
        Assert.Equal(entry.Id, Assert.Single((await publicClient.GetFromJsonAsync<PublicStatisticsResultsPageDTO>(results))!.Items).Id);
        Assert.Equal(entry.Id, Assert.Single((await publicClient.GetFromJsonAsync<List<BestStatisticsResultDTO>>("/api/statistics/best-results"))!).Id);
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task MultiCanBeMarkedAndRestoresToCurrentInvalidContributorState()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        var parent = Config();
        var contributor = Entry(parent, 1, StatisticsEntryInvalidReason.AboveMaximumDuration);
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Double", Distance = 250 };
        var config = new MultiStatisticsItemConfig { Id = Guid.NewGuid(), StatisticsItem = item,
            StatisticsItemId = item.Id, ParentConfig = parent, ParentConfigId = parent.Id, Repetitions = 1 };
        var aggregate = new StatisticsEntry
        {
            Id = Guid.NewGuid(), StatisticsItem = item, StatisticsItemId = item.Id,
            StatisticsItemConfig = config, StatisticsItemConfigId = config.Id,
            TransponderId = contributor.TransponderId, FacilityId = contributor.FacilityId,
            TimeStart = contributor.TimeStart, TimeEnd = contributor.TimeEnd,
            Duration = contributor.Duration, Speed = contributor.Speed,
            IsValid = false, InvalidReason = StatisticsEntryInvalidReason.InvalidContributor
        };
        await host.SeedAsync(contributor, aggregate);
        using var client = Admin(host);
        var marked = await Set(client, aggregate.Id, true, "Review Multi");
        Assert.True(marked.IsManuallyInvalidated);
        Assert.Equal(StatisticsConfigurationType.Multi, marked.StatisticsItemConfigType);
        var restored = await Set(client, aggregate.Id, false, "Re-evaluate contributors");
        Assert.False(restored.IsManuallyInvalidated);
        Assert.False(restored.IsValid);
        Assert.Equal(StatisticsEntryInvalidReason.InvalidContributor, restored.InvalidReason);
        AssertMeasurements(marked, restored);
        var persisted = (await Page(client)).Items.Single(row => row.Id == aggregate.Id);
        AssertPersisted(restored, persisted);
    }

    private static SimpleStatisticsItemConfig Config()
    {
        var item = new StatisticsItem { Id = Guid.NewGuid(), Name = "Lap", Distance = 250 };
        return new() { Id = Guid.NewGuid(), StatisticsItemId = item.Id, StatisticsItem = item,
            CoursePointStart = PublicStatisticsTestHost.Start, CoursePointEnd = PublicStatisticsTestHost.End };
    }

    private static StatisticsEntry Entry(SimpleStatisticsItemConfig config, int index,
        StatisticsEntryInvalidReason? reason = null, bool checkpoints = true, DateTime? completed = null, TimeSpan? duration = null, double? speed = null) => new()
    {
        Id = Guid.Parse($"80000000-0000-0000-0000-{index:D12}"), StatisticsItem = config.StatisticsItem,
        StatisticsItemId = config.StatisticsItemId, StatisticsItemConfig = config, StatisticsItemConfigId = config.Id,
        FacilityId = PublicStatisticsTestHost.Facility, TransponderId = Guid.NewGuid(),
        StatsProfile = new() { UserId = Guid.NewGuid(), ProfileName = "Rider", IsPublic = true },
        TimeEnd = completed ?? Completed, TimeStart = reason == StatisticsEntryInvalidReason.NonPositiveDuration
            ? completed ?? Completed : (completed ?? Completed).AddSeconds(-10),
        Duration = duration ?? (reason == StatisticsEntryInvalidReason.NonPositiveDuration ? TimeSpan.Zero : TimeSpan.FromSeconds(10)),
        Speed = speed ?? (reason == StatisticsEntryInvalidReason.NonPositiveDuration ? double.PositiveInfinity : 25),
        IsValid = reason is null, InvalidReason = reason, RequiredCoursePointsSatisfied = checkpoints
    };

    private static HttpClient Admin(PublicStatisticsTestHost host)
    {
        var client = host.App.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "statistics-admin");
        client.DefaultRequestHeaders.Add("X-Test-Subject", Actor.ToString());
        return client;
    }
    private static string Url(Guid id) => $"{Root}/{id}/manual-invalidation";
    private static async Task<AdminStatisticsEntryDTO> Set(HttpClient client, Guid id, bool manual, string? note)
    {
        var response = await client.PutAsJsonAsync(Url(id), new StatisticsEntryManualInvalidationRequest(manual, note));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<AdminStatisticsEntryDTO>())!;
    }
    private static async Task<AdminStatisticsEntriesPageDTO> Page(HttpClient client, string query = "")
    {
        var response = await client.GetAsync(Root + "?" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<AdminStatisticsEntriesPageDTO>())!;
    }
    private static string Cursor(AdminStatisticsEntryCursorDTO cursor, string direction, int pageSize = 2) =>
        $"pageSize={pageSize}&cursor={Uri.EscapeDataString(cursor.TimeEnd.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + "|" + cursor.Id)}&direction={direction}";
    private static async Task<int> Requests(PublicStatisticsTestHost host, Guid transponderId)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<StatisticsDbContext>().Set<RecomputeRequest>()
            .CountAsync(row => row.TransponderId == transponderId && row.Reason == RecomputeReason.Manual);
    }
    private static void AssertPersisted(AdminStatisticsEntryDTO response, AdminStatisticsEntryDTO persisted)
    {
        // Npgsql persists timestamps at whole microseconds; the response clock has 100ns ticks.
        var time = response.ManualInvalidationChangedAtUtc!.Value;
        var storedTime = new DateTimeOffset(time.Ticks / 10 * 10, TimeSpan.Zero);
        Assert.Equal(response with { ManualInvalidationChangedAtUtc = storedTime }, persisted);
    }
    private static void AssertMeasurements(AdminStatisticsEntryDTO expected, AdminStatisticsEntryDTO actual) =>
        Assert.Equal((expected.Id, expected.TimeStart, expected.TimeEnd, expected.Duration, expected.SpeedMetresPerSecond,
            expected.FacilityId, expected.TransponderId, expected.StatsProfileUserId, expected.StatisticsItemId, expected.StatisticsItemConfigId),
            (actual.Id, actual.TimeStart, actual.TimeEnd, actual.Duration, actual.SpeedMetresPerSecond,
            actual.FacilityId, actual.TransponderId, actual.StatsProfileUserId, actual.StatisticsItemId, actual.StatisticsItemConfigId));
}
