using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Api.Test;

public sealed class AgentCapabilityEndpointTests(TimingApiFactory factory) : IClassFixture<TimingApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Installation_ExposesLatestCapabilityReport_OrNullWithoutOne()
    {
        var reportedAt = new DateTime(2026, 11, 28, 10, 0, 0, DateTimeKind.Utc);
        var oldest = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var reported = new Installation
        {
            Id = Guid.NewGuid(), AgentId = $"caps-{Guid.NewGuid():N}", CapabilitiesReportedAt = reportedAt,
            AgentType = "P3", AgentVersion = "2.1.0", ControlProtocolVersion = 1,
            AgentCapabilities =
            [
                new AgentCapabilityEntry
                {
                    Name = "passing-replay",
                    PassingReplay = new PassingReplayCapabilityEntry
                    {
                        Mode = "AgentStore", OldestAvailableUtc = oldest, MaxRangeSeconds = TimeSpan.FromDays(30).TotalSeconds
                    }
                },
                new AgentCapabilityEntry { Name = "future.capability" }
            ]
        };
        var silent = new Installation { Id = Guid.NewGuid(), AgentId = $"silent-{Guid.NewGuid():N}" };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
            storage.AddRange(reported, silent);
            await storage.SaveChangesAsync();
        }
        using var client = factory.CreateClient();

        var dto = await client.GetFromJsonAsync<InstallationDTO>($"/api/timing/installations/{reported.Id}");
        var report = Assert.IsType<AgentCapabilitiesDTO>(dto!.Capabilities);
        Assert.Equal(reportedAt, report.ReportedAtUtc.ToUniversalTime());
        Assert.Equal(("P3", "2.1.0", 1), (report.AgentType, report.AgentVersion, report.ControlProtocolVersion));
        Assert.Equal(["passing-replay", "future.capability"], report.Capabilities.Select(capability => capability.Name));
        var replay = Assert.IsType<PassingReplayCapabilityDTO>(report.Capabilities[0].PassingReplay);
        Assert.Equal("AgentStore", replay.Mode);
        Assert.Equal(oldest, replay.OldestAvailableUtc!.Value.ToUniversalTime());
        Assert.Equal(30 * 86400d, replay.MaxRangeSeconds);
        Assert.False(replay.SupportsLoopFilter);
        Assert.Null(report.Capabilities[1].PassingReplay);

        var none = await client.GetFromJsonAsync<InstallationDTO>($"/api/timing/installations/{silent.Id}");
        Assert.Null(none!.Capabilities);
    }

    [Fact]
    public async Task RefreshCapabilities_EnqueuesCommandForTheInstallationsAgent()
    {
        var installation = new Installation { Id = Guid.NewGuid(), AgentId = $"refresh-{Guid.NewGuid():N}" };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
            storage.Add(installation);
            await storage.SaveChangesAsync();
        }
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/timing/admin/installations/{installation.Id}/capabilities/refresh", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CapabilityReportRequestedDTO>();
        var command = Assert.Single(factory.Messages.AgentCommands);
        Assert.Equal(installation.AgentId, command.AgentId);
        Assert.Equal(body!.RequestId, command.RequestId);
        Assert.NotEqual(Guid.Empty, command.RequestId);
    }

    [Fact]
    public async Task RefreshCapabilities_ReturnsNotFoundForUnknownInstallation()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/timing/admin/installations/{Guid.NewGuid()}/capabilities/refresh", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.Messages.AgentCommands);
    }
}
