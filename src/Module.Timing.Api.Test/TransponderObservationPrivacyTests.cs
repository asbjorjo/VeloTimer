using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Api.Test;

// The PostgreSQL fixture resets its database; do not run this new class alongside other reset fixtures.
[CollectionDefinition(nameof(TransponderObservationPrivacyTests), DisableParallelization = true)]
public sealed class ObservationPrivacyCollection { }

[Collection(nameof(TransponderObservationPrivacyTests))]
public sealed class TransponderObservationPrivacyTests(TimingApiFactory factory) : IClassFixture<TimingApiFactory>, IAsyncLifetime
{
    private static readonly Guid Caller = TestAuthenticationHandler.DefaultOwnerId;
    private static readonly Guid Other = Guid.Parse("00000000-0000-0000-0000-000000000456");
    // Listing uses the system UTC clock. Keep fixtures comfortably away from its current boundary.
    private readonly DateTime day = DateTime.UtcNow.Date.AddDays(-1);
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("lastSeen", false)]
    [InlineData("lastSeen", true)]
    [InlineData("ownedLastSeen", false)]
    [InlineData("ownedLastSeen", true)]
    public async Task HistoricalOwner_CannotInferOtherRiderViaCountOrSort(string sort, bool descending)
    {
        var ids = await Seed(current: false);
        using var client = Client("user");
        var before = await Query(client, sort, descending);
        Assert.Equal(2, before.TotalCount);
        Assert.Equal(ids, before.Items.Select(row => row.Transponder.Id)); // Null sightings use stable ID fallback.
        Assert.All(before.Items, row => { Assert.Null(row.Transponder.LastSeen); Assert.False(row.Transponder.CanViewStatus); });
        foreach (var date in new[] { day.AddDays(-6), day })
        {
            var legacy = await Query(client, sort, descending, new(LastSeen: DateOnly.FromDateTime(date)));
            Assert.Equal(0, legacy.TotalCount);
            Assert.Empty(legacy.Items);
            var range = await Query(client, sort, descending, new() { LastSeenUtcRange = new(new(date, TimeSpan.Zero), new(date.AddDays(1), TimeSpan.Zero)) });
            Assert.Equal(0, range.TotalCount);
            Assert.Empty(range.Items);
        }
        await AddPassing(descending || sort == "ownedLastSeen" ? ids[1] : ids[0], day.AddHours(23)); // Another rider changes a formerly owned transponder.
        var after = await Query(client, sort, descending);
        Assert.Equal(before.TotalCount, after.TotalCount);
        Assert.Equal(before.Items.Select(row => (row.Transponder.Id, row.Ownership!.Id)), after.Items.Select(row => (row.Transponder.Id, row.Ownership!.Id)));
        Assert.All(after.Items, row => Assert.Null(row.Transponder.LastSeen));
        var filtered = await Query(client, sort, descending, new(LastSeen: DateOnly.FromDateTime(day)));
        Assert.Equal(0, filtered.TotalCount);
        Assert.Empty(filtered.Items);
        using var denied = await client.GetAsync($"/api/timing/transponder-status/{ids[0]}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [Theory]
    [InlineData("lastSeen")]
    [InlineData("ownedLastSeen")]
    public async Task CurrentOwner_LastSeenUsesOnlyCallerPeriodsAndExcludesFuture(string sort)
    {
        var ids = await Seed(current: true);
        using var client = Client("user");
        var page = await Query(client, sort, true);
        Assert.Equal(4, page.TotalCount); // Each of two transponders has two caller ownership rows.
        Assert.Equal(ids.Reverse(), page.Items.Select(row => row.Transponder.Id).Distinct());
        Assert.All(page.Items, row =>
        {
            Assert.True(row.Transponder.CanViewStatus);
            var expected = row.Transponder.Id == ids[0] ? day.AddDays(-6) : day.AddDays(-1);
            Assert.Equal(new DateTimeOffset(expected, TimeSpan.Zero), row.Transponder.LastSeen);
            Assert.Equal(TimeSpan.Zero, row.Transponder.LastSeen!.Value.Offset);
        });
        var excluded = await Query(client, sort, true, new(LastSeen: DateOnly.FromDateTime(day)));
        Assert.Equal(0, excluded.TotalCount);
        Assert.Empty(excluded.Items);
        var earlierPeriod = await Query(client, sort, true, new() { LastSeenUtcRange = new(new(day.AddDays(-6), TimeSpan.Zero), new(day.AddDays(-5), TimeSpan.Zero)) });
        Assert.Equal(2, earlierPeriod.TotalCount);
        Assert.All(earlierPeriod.Items, row => Assert.Equal(ids[0], row.Transponder.Id));
    }

    [Theory]
    [InlineData("velotime-admin")]
    [InlineData("timing-admin")]
    [InlineData("facilities-admin")]
    [InlineData("statistics-admin")]
    public async Task Administrator_PersonalListRetainsFullObservationVisibilityButExcludesFuture(string role)
    {
        var ids = await Seed(current: false);
        using var client = Client(role);
        var page = await Query(client, "lastSeen", true, new(LastSeen: DateOnly.FromDateTime(day)));
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(ids, page.Items.Select(row => row.Transponder.Id));
        Assert.All(page.Items, row => { Assert.True(row.Transponder.CanViewStatus); Assert.Equal(new DateTimeOffset(day.AddHours(12), TimeSpan.Zero), row.Transponder.LastSeen); Assert.Equal(Caller, row.Ownership!.OwnerId); });
        var range = await Query(client, "ownedLastSeen", true, new() { LastSeenUtcRange = new(new(day, TimeSpan.Zero), new(day.AddDays(1), TimeSpan.Zero)) });
        Assert.Equal(2, range.TotalCount);
    }

    private HttpClient Client(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }
    private static async Task<TransponderPage> Query(HttpClient client, string sort, bool descending, TransponderColumnFilters? filters = null)
    {
        using var response = await client.PostAsJsonAsync("/api/timing/my/transponders/query", new TransponderPageRequest(0, 10, "", true, Filters: filters, SortBy: sort, SortDescending: descending));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransponderPage>())!;
    }
    private async Task<Guid[]> Seed(bool current)
    {
        var ids = new[] { Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000002") };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
        var point = new TimingPoint { Id = Guid.NewGuid(), SystemId = "privacy", Description = "Finish", Installation = new Installation { Id = Guid.NewGuid(), AgentId = "privacy-test" } };
        for (var index = 0; index < ids.Length; index++)
        {
            var transponder = new MylapsX2Transponder { Id = ids[index], SystemId = (106187718 + index).ToString(), Label = "Same label" };
            transponder.Owners.Add(new() { OwnerId = Caller, OwnedFrom = day.AddDays(-6), OwnedTo = day.AddDays(-5) });
            transponder.Owners.Add(new() { OwnerId = Other, OwnedFrom = day.AddDays(-5), OwnedTo = current ? day.AddDays(-2) : null });
            if (current) transponder.Owners.Add(new() { OwnerId = Caller, OwnedFrom = day.AddDays(-2) });
            db.Add(transponder);
            // Inclusive caller start; exclusive caller end; other rider; future (even if owned).
            foreach (var time in new[] { day.AddDays(-7), day.AddDays(-6), day.AddDays(-5), day.AddDays(-3), day.AddHours(12), day.AddDays(3) })
                if (!(current && index == 1 && time == day.AddHours(12))) db.Add(new Passing { Id = Guid.NewGuid(), Transponder = transponder, TimingPoint = point, Time = time });
            if (current && index == 1) db.Add(new Passing { Id = Guid.NewGuid(), Transponder = transponder, TimingPoint = point, Time = day.AddDays(-1) });
            if (current && index == 0) transponder.Owners.Last().OwnedFrom = day.AddHours(18); // Current period with no sightings: retain earlier owned sighting.
        }
        await db.SaveChangesAsync();
        return ids;
    }
    private async Task AddPassing(Guid id, DateTime time)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
        db.Add(new Passing { Id = Guid.NewGuid(), TransponderId = id, TimingPointId = await db.Set<TimingPoint>().Select(point => point.Id).SingleAsync(), Time = time });
        await db.SaveChangesAsync();
    }
}
