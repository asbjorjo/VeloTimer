using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Api.Test;

public sealed class RejectedPassingEndpointTests(TimingApiFactory factory) : IClassFixture<TimingApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RejectedPassings_ListReprocessAndDismiss()
    {
        var time = new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
        var fixable = Row("reprocess-agent", "106187718", "ProChip", PassingRejectionReason.UnsupportedTransponderType, time);
        var broken = Row("reprocess-agent", "42", "ProChip", PassingRejectionReason.InvalidTransponderId, time.AddSeconds(1));
        var other = Row("other-agent", "106187718", "X2", PassingRejectionReason.UnsupportedTransponderType, time);
        using (var scope = factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
            storage.AddRange(fixable, broken, other);
            await storage.SaveChangesAsync();
        }
        using var client = factory.CreateClient();

        var page = await client.GetFromJsonAsync<RejectedPassingPage>("/api/timing/rejected-passings?agentId=reprocess-agent");
        Assert.Equal(2, page!.TotalCount);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/timing/rejected-passings?reason=Nope")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/timing/rejected-passings?pageSize=0")).StatusCode);

        var response = await client.PostAsJsonAsync("/api/timing/rejected-passings/reprocess", new ReprocessRejectedPassingsRequest("reprocess-agent"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new ReprocessRejectedPassingsResult(1, 0, 0, 1), await response.Content.ReadFromJsonAsync<ReprocessRejectedPassingsResult>());
        var recorded = Assert.Single(factory.Messages.Passings, passing => passing.Time == time);

        using (var scope = factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
            Assert.Equal(recorded.PassingId, (await storage.Set<Passing>().SingleAsync(p => p.SourcePassingId == fixable.SourcePassingId)).Id);
            Assert.Equal([broken.Id, other.Id], (await storage.Set<RejectedPassing>().Where(r => r.Id == broken.Id || r.Id == other.Id)
                .OrderBy(r => r.AgentId == "other-agent").Select(r => r.Id).ToListAsync()));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/timing/rejected-passings/{broken.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/timing/rejected-passings/{broken.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("statistics-admin")]
    public async Task RejectedPassings_RequireTimingRole(string role)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/timing/rejected-passings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/timing/rejected-passings/reprocess", new ReprocessRejectedPassingsRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/timing/rejected-passings/{Guid.NewGuid()}")).StatusCode);
    }

    private static RejectedPassing Row(string agent, string transponderId, string type, PassingRejectionReason reason, DateTime time) => new()
    {
        Id = Guid.NewGuid(), AgentId = agent, SourcePassingId = Guid.NewGuid(), Time = time, TransponderType = type,
        TransponderId = transponderId, LoopId = "finish", Reason = reason, FirstSeenAt = time, LastSeenAt = time
    };
}
