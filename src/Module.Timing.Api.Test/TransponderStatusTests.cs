using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Api.Test;

public sealed class TransponderStatusTests(TimingApiFactory factory) : IClassFixture<TimingApiFactory>, IAsyncLifetime
{
    private static readonly Guid OwnerId = TestAuthenticationHandler.DefaultOwnerId;
    private static readonly Guid OtherOwnerId = Guid.Parse("00000000-0000-0000-0000-000000000456");
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("velotime-admin")]
    [InlineData("timing-admin")]
    [InlineData("facilities-admin")]
    [InlineData("statistics-admin")]
    public async Task Status_AllAdministratorsSeeFullHistoryAndLocations(string role)
    {
        var id = await Seed();
        using var client = Client(role);
        var status = await Get(client, id);
        Assert.True(status.Administration);
        Assert.Equal("Training label", status.Label);
        Assert.Equal(MylapsX2Transponder.IdToCode("106187718"), status.Code);
        Assert.NotEqual(status.Label, status.Code);
        Assert.Equal("106187718", status.SystemId);
        Assert.Equal(3, status.OwnershipCount);
        Assert.Contains(status.Ownerships, owner => owner.OwnerId == OtherOwnerId);
        Assert.Equal(Now, status.LastSeen!.Time);
        Assert.Equal("Finish", status.LastSeen.TimingPoint);
        Assert.Equal("Track installation", status.LastSeen.Installation);
        Assert.NotEqual(Guid.Empty, status.LastSeen.FacilityId);
        Assert.True(status.LowBattery);
        Assert.True(status.LowStrength);
        Assert.True(status.LowHits);
        Assert.Equal(6, status.WarningCount);
        Assert.Equal(Now.AddDays(-7), status.WarningWindowStart);
        Assert.Equal(Now, status.WarningWindowEnd);
        Assert.DoesNotContain(status.Warnings, warning => warning.Time < status.WarningWindowStart || warning.Time > Now);
        using var response = await client.GetAsync(Path(id));
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal(role is "facilities-admin" or "statistics-admin" ? HttpStatusCode.Forbidden : HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"/api/timing/transponders/{id}", new SaveTransponderRequest("Changed"))).StatusCode);
    }

    [Fact]
    public async Task Status_CurrentOwnerSeesOnlyOwnPeriodsAndSightingsIncludingEarlierOwnedPeriod()
    {
        var id = await Seed();
        using var client = Client("user");
        var status = await Get(client, id);
        Assert.False(status.Administration);
        Assert.Equal(2, status.OwnershipCount);
        Assert.All(status.Ownerships, owner => Assert.Equal(OwnerId, owner.OwnerId));
        Assert.Equal(Now, status.LastSeen!.Time);
        Assert.Equal(3, status.WarningCount);
        Assert.True(status.LowBattery);
        Assert.False(status.LowStrength);
        Assert.True(status.LowHits);
        Assert.Contains(status.Warnings, passing => passing.Time == Now.AddDays(-6));
        Assert.Contains(status.Warnings, passing => passing.Time == Now.AddDays(-2));
        Assert.DoesNotContain(status.Warnings, passing => passing.Time == Now.AddDays(-5));
        var json = await client.GetStringAsync(Path(id));
        Assert.DoesNotContain(OtherOwnerId.ToString(), json);

        await using var scope = factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
        var active = await storage.Set<TransponderOwner>().SingleAsync(owner => owner.OwnerId == OwnerId && owner.OwnedTo == null);
        active.OwnedTo = Now.UtcDateTime;
        await storage.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Path(id) + "?warningPage=1")).StatusCode);
    }

    [Theory]
    [InlineData(-2, -1, HttpStatusCode.NotFound)]
    [InlineData(1, 2, HttpStatusCode.NotFound)]
    [InlineData(-1, 0, HttpStatusCode.NotFound)]
    [InlineData(0, 1, HttpStatusCode.OK)]
    public async Task Status_CurrentOwnershipUsesInclusiveStartAndExclusiveEnd(int fromDays, int toDays, HttpStatusCode expected)
    {
        var id = await SeedSingleOwner(Now.AddDays(fromDays), Now.AddDays(toDays));
        using var client = Client("user");
        Assert.Equal(expected, (await client.GetAsync(Path(id))).StatusCode);
    }

    [Fact]
    public async Task Status_DeniesAnonymousInvalidSubjectUnrelatedOwnerAndMissingTransponder()
    {
        var id = await Seed();
        using var anonymous = Client("user");
        anonymous.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path(id))).StatusCode);
        using var invalid = Client("user");
        invalid.DefaultRequestHeaders.Add("X-Test-Subject", "invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await invalid.GetAsync(Path(id))).StatusCode);
        using var unrelated = Client("user");
        unrelated.DefaultRequestHeaders.Add("X-Test-Subject", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await unrelated.GetAsync(Path(id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await unrelated.GetAsync(Path(Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task Status_PaginatesIndependentlyAndSummarizesBeyondTheWarningPage()
    {
        var id = await Seed();
        using var client = Client("timing-admin");
        var first = await Get(client, id, "?pageSize=1");
        var second = await Get(client, id, "?pageSize=1&warningPage=1&ownershipPage=1");
        Assert.Single(first.Ownerships);
        Assert.Single(first.Warnings);
        Assert.NotEqual(first.Ownerships[0].Id, second.Ownerships[0].Id);
        Assert.NotEqual(first.Warnings[0].Id, second.Warnings[0].Id);
        Assert.True(first.LowHits);
        Assert.False(first.Warnings[0].LowHits);
        Assert.Equal(first.WarningCount, second.WarningCount);
        var repeated = await Get(client, id, "?pageSize=1");
        Assert.Equal(first.Warnings[0].Id, repeated.Warnings[0].Id);
        var empty = await Get(client, id, "?ownershipPage=50&warningPage=50");
        Assert.Empty(empty.Ownerships);
        Assert.Empty(empty.Warnings);
        Assert.Equal(first.WarningCount, empty.WarningCount);
        foreach (var query in new[] { "?pageSize=0", "?pageSize=101", "?ownershipPage=-1", "?warningPage=-1", "?warningPage=2147483647" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Path(id) + query)).StatusCode);
    }

    [Fact]
    public async Task Status_TiedTimesHaveStablePagesAndOverlappingOwnershipsDoNotDuplicateWarnings()
    {
        var id = await Seed();
        var passingId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var facilityId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
            var point = new TimingPoint { Id = Guid.NewGuid(), SystemId = "second",
                Description = "Second finish", Installation = new Installation { AgentId = "second-agent", Facility = facilityId, Description = "Second installation" } };
            storage.Add(new Passing { Id = passingId, Time = Now.UtcDateTime, TransponderId = id,
                TimingPoint = point, LowHits = true });
            storage.Add(new TransponderOwner { Id = Guid.NewGuid(), OwnerId = OwnerId, TransponderId = id,
                OwnedFrom = Now.UtcDateTime.AddDays(-1) });
            await storage.SaveChangesAsync();
        }
        using var client = Client("user");
        var status = await Get(client, id);
        Assert.Equal(4, status.WarningCount);
        Assert.Equal(4, status.Warnings.Select(warning => warning.Id).Distinct().Count());
        var first = await Get(client, id, "?pageSize=1");
        var second = await Get(client, id, "?pageSize=1&warningPage=1");
        Assert.Equal(passingId, first.Warnings[0].Id);
        Assert.NotEqual(first.Warnings[0].Id, second.Warnings[0].Id);
        Assert.Equal(Now, first.Warnings[0].Time);
        Assert.Equal(Now, second.Warnings[0].Time);
        Assert.Equal(facilityId, first.Warnings[0].FacilityId);
        Assert.Equal("Second finish", first.Warnings[0].TimingPoint);
        Assert.Equal(first.Warnings[0].Id, (await Get(client, id, "?pageSize=1")).Warnings[0].Id);
    }

    [Fact]
    public async Task Status_ListLinkEligibilityUsesCurrentOwnershipNotTheDisplayedHistoricalRow()
    {
        var id = await Seed();
        using var client = Client("user");
        var page = (await client.GetFromJsonAsync<TransponderPage>("/api/timing/my/transponders"))!;
        Assert.All(page.Items, row => Assert.True(row.Transponder.CanViewStatus));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
            var current = await storage.Set<TransponderOwner>().SingleAsync(owner => owner.OwnerId == OwnerId && owner.OwnedTo == null);
            current.OwnedTo = DateTime.UtcNow.AddSeconds(-1);
            await storage.SaveChangesAsync();
        }
        var ended = (await client.GetFromJsonAsync<TransponderPage>("/api/timing/my/transponders"))!;
        Assert.All(ended.Items, row => Assert.False(row.Transponder.CanViewStatus));
        client.DefaultRequestHeaders.Remove("X-Test-Role");
        client.DefaultRequestHeaders.Add("X-Test-Role", "facilities-admin");
        var administration = (await client.GetFromJsonAsync<TransponderPage>("/api/timing/my/transponders"))!;
        Assert.All(administration.Items, row => Assert.True(row.Transponder.CanViewStatus));
        Assert.All(administration.Items, row => Assert.Equal(id, row.Transponder.Id));
    }

    [Fact]
    public async Task Status_LastSeenIsFilteredBeforeSelectingLatestAndCanBeEmpty()
    {
        var id = await Seed();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
            var active = await storage.Set<TransponderOwner>().SingleAsync(owner => owner.OwnerId == OwnerId && owner.OwnedTo == null);
            active.OwnedFrom = Now.UtcDateTime.AddHours(1);
            storage.Add(new TransponderOwner { Id = Guid.NewGuid(), OwnerId = OwnerId, TransponderId = id,
                OwnedFrom = Now.UtcDateTime.AddMinutes(-1), OwnedTo = Now.UtcDateTime.AddMinutes(1) });
            storage.Remove(await storage.Set<Passing>().SingleAsync(passing => passing.Time == Now.UtcDateTime));
            await storage.SaveChangesAsync();
        }
        using var client = Client("user");
        var status = await Get(client, id);
        Assert.Equal(Now.AddDays(-6), status.LastSeen!.Time);
        Assert.Single(status.Warnings);

        var unseen = await SeedSingleOwner(Now, null, "106187719");
        var empty = await Get(client, unseen);
        Assert.Null(empty.LastSeen);
        Assert.Empty(empty.Warnings);
        Assert.False(empty.LowBattery || empty.LowStrength || empty.LowHits);
    }

    private HttpClient Client(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        factory.Clock.UtcNow = Now;
        return client;
    }

    private static string Path(Guid id) => $"/api/timing/transponder-status/{id}";
    private static async Task<TransponderStatus> Get(HttpClient client, Guid id, string query = "") =>
        (await client.GetFromJsonAsync<TransponderStatus>(Path(id) + query))!;

    private async Task<Guid> SeedSingleOwner(DateTimeOffset from, DateTimeOffset? to, string systemId = "106187718")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
        var transponder = new MylapsX2Transponder { Id = Guid.NewGuid(), SystemId = systemId };
        transponder.Owners.Add(new TransponderOwner { OwnerId = OwnerId, OwnedFrom = from.UtcDateTime, OwnedTo = to?.UtcDateTime });
        storage.Add(transponder);
        await storage.SaveChangesAsync();
        return transponder.Id;
    }

    private async Task<Guid> Seed()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
        var transponder = new MylapsX2Transponder { Id = Guid.NewGuid(), SystemId = "106187718", Label = "Training label" };
        transponder.Owners.Add(new() { OwnerId = OwnerId, OwnedFrom = Now.UtcDateTime.AddDays(-6), OwnedTo = Now.UtcDateTime.AddDays(-5) });
        transponder.Owners.Add(new() { OwnerId = OtherOwnerId, OwnedFrom = Now.UtcDateTime.AddDays(-5), OwnedTo = Now.UtcDateTime.AddDays(-2) });
        transponder.Owners.Add(new() { OwnerId = OwnerId, OwnedFrom = Now.UtcDateTime.AddDays(-2) });
        var installation = new Installation { Id = Guid.NewGuid(), AgentId = "status-test", Description = "Track installation", Facility = Guid.NewGuid() };
        var point = new TimingPoint { Id = Guid.NewGuid(), Installation = installation, Description = "Finish", SystemId = "finish" };
        storage.AddRange(transponder, point);
        foreach (var (days, battery, strength, hits) in new[]
        {
            (-8, true, true, true), (-7, false, true, false), (-6, false, false, true),
            (-5, false, true, false), (-3, false, true, false), (-2, true, false, false),
            (0, true, false, false), (1, false, false, true)
        })
            storage.Add(new Passing { Id = Guid.NewGuid(), Time = Now.UtcDateTime.AddDays(days),
                Transponder = transponder, TimingPoint = point, LowBattery = battery, LowStrength = strength, LowHits = hits });
        await storage.SaveChangesAsync();
        return transponder.Id;
    }
}
