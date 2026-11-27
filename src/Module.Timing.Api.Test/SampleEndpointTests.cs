using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Api.Test;

public sealed class SampleEndpointTests(TimingApiFactory factory) : IClassFixture<TimingApiFactory>, IAsyncLifetime
{
    private static readonly DateTime Start = new(2026, 11, 27, 18, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Samples_PairEachPassingWithThePreviousPassingAtTheSameInstallation()
    {
        var transponder = Guid.NewGuid();
        var other = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
            var trackA = Installation("track-a", out var a1, out var a2);
            var trackB = Installation("track-b", out var b1, out _);
            storage.AddRange(trackA, trackB,
                new MylapsX2Transponder { Id = transponder, SystemId = "106187718" },
                new MylapsX2Transponder { Id = other, SystemId = "106187719" });
            // Inserted out of time order: pairing must depend on event time only.
            storage.AddRange(
                Passing(transponder, a1, 30),
                Passing(transponder, a2, 0),
                Passing(transponder, b1, 15), // Other installation: never paired with track A.
                Passing(transponder, a1, 10),
                Passing(other, a2, 12));
            await storage.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        var samples = await client.GetFromJsonAsync<List<SampleDTO>>("/api/timing/sample?count=10");

        Assert.NotNull(samples);
        Assert.Collection(samples,
            sample =>
            {
                Assert.Equal(transponder, sample.TransponderId);
                Assert.Equal(("A1", "A1", Start.AddSeconds(30), TimeSpan.FromSeconds(20)),
                    (sample.TimingPointStart, sample.TimingPointEnd, sample.Time.ToUniversalTime(), sample.Duration));
            },
            sample =>
            {
                Assert.Equal(transponder, sample.TransponderId);
                Assert.Equal(("A2", "A1", Start.AddSeconds(10), TimeSpan.FromSeconds(10)),
                    (sample.TimingPointStart, sample.TimingPointEnd, sample.Time.ToUniversalTime(), sample.Duration));
            });
    }

    [Fact]
    public async Task Samples_HonourTheRequestedCount()
    {
        var transponder = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
            storage.AddRange(Installation("count", out var point, out _), new MylapsX2Transponder { Id = transponder, SystemId = "106187720" });
            for (var i = 0; i < 5; i++) storage.Add(Passing(transponder, point, i * 10));
            await storage.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        var samples = await client.GetFromJsonAsync<List<SampleDTO>>("/api/timing/sample?count=2");

        Assert.Equal([Start.AddSeconds(40), Start.AddSeconds(30)], samples!.Select(sample => sample.Time.ToUniversalTime()));
    }

    private static Installation Installation(string agentId, out TimingPoint first, out TimingPoint second)
    {
        var installation = new Installation { Id = Guid.NewGuid(), AgentId = agentId, TimingSystem = TimingSystem.MyLaps_X2 };
        var prefix = agentId == "track-b" ? "B" : "A";
        first = new TimingPoint { Id = Guid.NewGuid(), SystemId = "1", Description = prefix + "1", Installation = installation, InstallationId = installation.Id };
        second = new TimingPoint { Id = Guid.NewGuid(), SystemId = "2", Description = prefix + "2", Installation = installation, InstallationId = installation.Id };
        installation.TimingPoints.AddRange([first, second]);
        return installation;
    }

    private static Passing Passing(Guid transponder, TimingPoint point, int seconds) => new()
    {
        Id = Guid.NewGuid(),
        SourcePassingId = Guid.NewGuid().ToString(),
        InstallationId = point.InstallationId,
        TransponderId = transponder,
        TimingPointId = point.Id,
        Time = Start.AddSeconds(seconds)
    };
}
