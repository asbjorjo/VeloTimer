using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Api.Test;

public sealed class PassingReplayEndpointTests(TimingApiFactory factory) : IClassFixture<TimingApiFactory>, IAsyncLifetime
{
    private static readonly DateTime Oldest = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync()
    {
        factory.Clock.UtcNow = new DateTimeOffset(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);
        return factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Installation> AddInstallationAsync(bool withReport = true, bool loopFilter = true)
    {
        var installation = new Installation { Id = Guid.NewGuid(), AgentId = $"replay-{Guid.NewGuid():N}" };
        if (withReport)
        {
            installation.CapabilitiesReportedAt = factory.Clock.UtcNow.UtcDateTime;
            installation.AgentType = "Dummy";
            installation.ControlProtocolVersion = 1;
            installation.AgentCapabilities =
            [
                new AgentCapabilityEntry
                {
                    Name = "passing-replay",
                    PassingReplay = new PassingReplayCapabilityEntry
                    {
                        Mode = "AgentStore", OldestAvailableUtc = Oldest,
                        MaxRangeSeconds = TimeSpan.FromDays(30).TotalSeconds, SupportsLoopFilter = loopFilter
                    }
                }
            ];
        }
        await using var scope = factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
        storage.Add(installation);
        await storage.SaveChangesAsync();
        return installation;
    }

    private static CreatePassingReplayRequest Request(DateTime? from = null, DateTime? to = null, string mode = "AgentStore",
        List<string>? loops = null) => new()
    {
        Mode = mode,
        FromUtc = from ?? new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc),
        ToUtc = to ?? new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc),
        LoopIds = loops,
        Note = "Decoder lost network"
    };

    [Fact]
    public async Task Create_StoresRequestAndEnqueuesCommandForTheAgent()
    {
        var installation = await AddInstallationAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/timing/admin/installations/{installation.Id}/passing-replays",
            Request(loops: ["L1", " L1 ", "L2"]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<PassingReplayRequestDTO>())!;
        Assert.Equal(("Pending", "AgentStore", installation.AgentId), (created.State, created.Mode, created.AgentId));
        Assert.Equal(["L1", "L2"], created.LoopIds);
        Assert.Equal(factory.Clock.UtcNow.UtcDateTime.AddMinutes(15), created.ExpiresAt.ToUniversalTime());
        var command = Assert.Single(factory.Messages.ReplayCommands);
        Assert.Equal(created.RequestId, command.RequestId);
        Assert.Equal(installation.AgentId, command.AgentId);
        Assert.Equal(created.FromUtc.ToUniversalTime(), command.FromUtc);
        Assert.Equal(["L1", "L2"], command.LoopIds!);

        var listed = await client.GetFromJsonAsync<List<PassingReplayRequestDTO>>(
            $"/api/timing/admin/installations/{installation.Id}/passing-replays");
        Assert.Equal(created.RequestId, Assert.Single(listed!).RequestId);
        var fetched = await client.GetFromJsonAsync<PassingReplayRequestDTO>(response.Headers.Location!.ToString());
        Assert.Equal("Decoder lost network", fetched!.Note);
    }

    [Fact]
    public async Task Create_RejectsSecondReplayWhileOneIsActive()
    {
        var installation = await AddInstallationAsync();
        using var client = factory.CreateClient();
        var url = $"/api/timing/admin/installations/{installation.Id}/passing-replays";

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(url, Request())).StatusCode);
        var second = await client.PostAsJsonAsync(url, Request());

        await AssertRejectedAsync(second, "Busy");
        Assert.Single(factory.Messages.ReplayCommands);
    }

    public static TheoryData<string, CreatePassingReplayRequest> InvalidRequests => new()
    {
        { "UnsupportedMode", Request(mode: "Source") },
        { "InvalidRange", Request(from: new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc), to: new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc)) },
        { "RangeTooLong", Request(from: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), to: new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc)) },
        { "OutOfRetention", Request(from: new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc), to: new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc)) },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Create_ReturnsReasonCodeForRequestsTheCapabilityDoesNotAllow(string reason, CreatePassingReplayRequest request)
    {
        var installation = await AddInstallationAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/timing/admin/installations/{installation.Id}/passing-replays", request);

        await AssertRejectedAsync(response, reason);
        Assert.Empty(factory.Messages.ReplayCommands);
    }

    [Fact]
    public async Task Create_RejectsWithoutCapabilityReportOrLoopFilterSupport()
    {
        var silent = await AddInstallationAsync(withReport: false);
        var noFilter = await AddInstallationAsync(loopFilter: false);
        using var client = factory.CreateClient();

        await AssertRejectedAsync(await client.PostAsJsonAsync(
            $"/api/timing/admin/installations/{silent.Id}/passing-replays", Request()), "NoCapabilityReport");
        await AssertRejectedAsync(await client.PostAsJsonAsync(
            $"/api/timing/admin/installations/{noFilter.Id}/passing-replays", Request(loops: ["L1"])), "LoopFilterUnsupported");
        Assert.Empty(factory.Messages.ReplayCommands);
    }

    [Fact]
    public async Task UnknownInstallationOrRequest_ReturnsNotFound()
    {
        var installation = await AddInstallationAsync();
        using var client = factory.CreateClient();
        var unknown = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(
            $"/api/timing/admin/installations/{unknown}/passing-replays", Request())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/timing/admin/installations/{unknown}/passing-replays")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/timing/admin/installations/{installation.Id}/passing-replays/{unknown}")).StatusCode);
        Assert.Empty(factory.Messages.ReplayCommands);
    }

    private static async Task AssertRejectedAsync(HttpResponseMessage response, string reason)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(reason, problem!.Detail);
    }
}
