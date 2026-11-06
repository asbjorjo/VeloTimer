using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;
namespace VeloTime.Module.Timing.Api.Test;
public sealed partial class TimingEndpointTests
{
    private static readonly DateTimeOffset RangeStart = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
    private static TransponderColumnFilters RangeFilters(string field, UtcInstantRange range, bool legacy = false) => field switch
    {
        "lastSeen" => new(LastSeen: legacy ? new DateOnly(2026, 10, 25) : null) { LastSeenUtcRange = range },
        "ownedFrom" => new(OwnedFrom: legacy ? new DateOnly(2026, 10, 25) : null) { OwnedFromUtcRange = range },
        "ownedTo" => new(OwnedTo: legacy ? new DateOnly(2026, 10, 25) : null) { OwnedToUtcRange = range },
        _ => throw new ArgumentException(field)
    };
    [Theory] [InlineData("lastSeen")] [InlineData("ownedFrom")] [InlineData("ownedTo")]
    public async Task UtcRangeValidation_RejectsMissingReversedEqualOffsetsAndLegacyConflicts(string field)
    {
        using var client = factory.CreateClient();
        var ranges = new[] { new UtcInstantRange(default, RangeStart), new(RangeStart, default), new(RangeStart, RangeStart), new(RangeStart.AddHours(1), RangeStart), new(RangeStart.ToOffset(TimeSpan.FromHours(1)), RangeStart.AddDays(1)), new(RangeStart, RangeStart.AddDays(1).ToOffset(TimeSpan.FromHours(-1))) };
        foreach (var range in ranges)
        {
            using var response = await client.PostAsJsonAsync("/api/timing/transponders/query", new TransponderPageRequest(0, 10, "", true, Filters: RangeFilters(field, range)));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var conflict = await client.PostAsJsonAsync("/api/timing/transponders/query", new TransponderPageRequest(0, 10, "", true, Filters: RangeFilters(field, new(RangeStart, RangeStart.AddDays(1)), true)));
        Assert.Equal(HttpStatusCode.BadRequest, conflict.StatusCode);
    }
    [Theory] [InlineData("lastSeen")] [InlineData("ownedFrom")] [InlineData("ownedTo")]
    public async Task UtcRanges_AreHalfOpen_FilterBeforeCountAndPage_ExcludeNulls_RespectOwnerScope(string field)
    {
        var start = RangeStart.UtcDateTime; var end = RangeStart.AddHours(25).UtcDateTime;
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
            var installation = new Installation { Id = Guid.NewGuid(), AgentId = "range-test", TimingSystem = TimingSystem.MyLaps_X2 };
            var point = new TimingPoint { Id = Guid.NewGuid(), SystemId = "range-loop", Description = "Range", InstallationId = installation.Id, Installation = installation };
            var boundaries = new[] { start.AddTicks(-10), start, end.AddTicks(-10), end };
            for (var i = 0; i < ids.Length; i++)
            {
                db.Add(new MylapsX2Transponder { Id = ids[i], SystemId = (106187718 + i).ToString(), Label = "range-" + i });
                if (i == 4) continue; // unassigned transponder
                if (i == 5)
                {
                    db.Add(new TransponderOwner { Id = Guid.NewGuid(), TransponderId = ids[i], OwnerId = TestAuthenticationHandler.DefaultOwnerId, OwnedFrom = start.AddDays(-2), OwnedTo = null });
                    continue; // open ownership and null last-seen must not match.
                }
                db.Add(new TransponderOwner { Id = Guid.NewGuid(), TransponderId = ids[i], OwnerId = i == 2 ? Guid.NewGuid() : TestAuthenticationHandler.DefaultOwnerId, OwnedFrom = field == "ownedFrom" ? boundaries[i] : start.AddDays(-2), OwnedTo = field == "ownedTo" ? boundaries[i] : null });
                db.Add(new Passing { Id = Guid.NewGuid(), TransponderId = ids[i], Time = boundaries[i], TimingPointId = point.Id, TimingPoint = point });
                // MAX(last-seen), not any historical passing, decides matching.
                if (i == 3) db.Add(new Passing { Id = Guid.NewGuid(), TransponderId = ids[i], Time = start.AddHours(1), TimingPointId = point.Id, TimingPoint = point });
            }
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateClient();
        var filters = RangeFilters(field, new(RangeStart, RangeStart.AddHours(25)));
        using var first = await client.PostAsJsonAsync("/api/timing/transponders/query", new TransponderPageRequest(0, 1, "", true, Filters: filters));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); var page = (await first.Content.ReadFromJsonAsync<TransponderPage>())!;
        Assert.Equal(2, page.TotalCount); Assert.Equal(ids[1], Assert.Single(page.Items).Transponder.Id);
        using var next = await client.PostAsJsonAsync("/api/timing/transponders/query", new TransponderPageRequest(1, 1, "", true, Filters: filters));
        var second = (await next.Content.ReadFromJsonAsync<TransponderPage>())!; Assert.Equal(2, second.TotalCount); Assert.Equal(ids[2], Assert.Single(second.Items).Transponder.Id);
        var legacy = field switch
        {
            "lastSeen" => new TransponderColumnFilters(LastSeen: new DateOnly(2026, 10, 25)),
            "ownedFrom" => new TransponderColumnFilters(OwnedFrom: new DateOnly(2026, 10, 25)),
            _ => new TransponderColumnFilters(OwnedTo: new DateOnly(2026, 10, 25))
        };
        using var legacyResponse = await client.PostAsJsonAsync("/api/timing/transponders/query", new TransponderPageRequest(0, 10, "", true, Filters: legacy));
        var legacyPage = (await legacyResponse.Content.ReadFromJsonAsync<TransponderPage>())!;
        Assert.Equal(1, legacyPage.TotalCount); Assert.Equal(ids[1], Assert.Single(legacyPage.Items).Transponder.Id);
        client.DefaultRequestHeaders.Add("X-Test-Role", "user");
        using var personal = await client.PostAsJsonAsync("/api/timing/my/transponders/query", new TransponderPageRequest(0, 10, "", true, Filters: filters));
        var mine = (await personal.Content.ReadFromJsonAsync<TransponderPage>())!; Assert.Equal(1, mine.TotalCount); Assert.Equal(ids[1], Assert.Single(mine.Items).Transponder.Id);
        using var forbidden = await client.PostAsJsonAsync("/api/timing/transponders/query", new TransponderPageRequest(0, 10, "", true, Filters: filters)); Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
