using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Timing.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;
using VeloTime.WebUI.Mud.Services;
using Contracts = VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.Platform.Test;

public sealed partial class WebUiBffTests
{
    [Theory]
    [InlineData("user")]
    [InlineData("timing-admin")]
    [InlineData("facilities-admin")]
    [InlineData("statistics-admin")]
    [InlineData("velotime-admin")]
    public async Task StatusBff_ForwardsScopeToTimingAndPagesWithoutCaching(string role)
    {
        var expected = TransponderStatusTestData.View();
        var backend = PlatformServiceTests.TestProxy.Create<ITransponderStatusService>((method, args) =>
        {
            Assert.Equal(nameof(ITransponderStatusService.GetAsync), method.Name);
            Assert.Equal(expected.Status.Id, args[0]);
            Assert.Equal(2, args[1]);
            Assert.Equal(3, args[2]);
            Assert.Equal(25, args[3]);
            return expected;
        });
        await using var app = await CreateAppAsync(status: backend);
        using var client = CreateClient(app);
        await client.GetAsync($"signin?role={role}");
        var result = await new TransponderStatusHttpService(client).GetAsync(expected.Status.Id, 2, 3, 25);
        Assert.Equal(expected.Status.Code, result.Status.Code);
        Assert.Equal(expected.Status.Warnings[0], result.Status.Warnings[0]);
        using var response = await client.GetAsync($"api/transponder-status/{expected.Status.Id}?ownershipPage=2&warningPage=3&pageSize=25");
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task StatusBff_PreservesAuthorizationAndFailureStatuses(HttpStatusCode status)
    {
        var backend = PlatformServiceTests.TestProxy.Create<ITransponderStatusService>((_, _) =>
            throw new HttpRequestException("Status unavailable.", null, status));
        await using var app = await CreateAppAsync(status: backend);
        using var client = CreateClient(app);
        await client.GetAsync("signin?role=user");
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new TransponderStatusHttpService(client).GetAsync(ResourceId));
        Assert.Equal(status, exception.StatusCode);
    }
}

public sealed class TransponderStatusServiceTests
{
    [Fact]
    public async Task SampleMapping_RetainsTransponderIdForStatusNavigation()
    {
        var id = Guid.NewGuid();
        var statistics = PlatformServiceTests.TestProxy.Create<VeloTime.Module.Statistics.Client.IStatisticsClient>((_, _) =>
            new VeloTime.Module.Statistics.Client.GetSamplesResponse
            {
                Samples = [new VeloTime.Module.Statistics.Client.SampleDTO { TransponderId = id,
                    CoursePointStartId = Guid.NewGuid(), CoursePointEndId = Guid.NewGuid(), Duration = "00:00:20" }]
            });
        var facilities = PlatformServiceTests.TestProxy.Create<IFacilitiesClient>((_, args) => new CoursePointDTO { Id = (Guid)args[0]!, Name = "Finish" });
        using var cache = new WebUiCacheTestHost(facilities);
        var result = Assert.Single(await new StatisticsService(statistics, cache.Cache).GetSamplesAsync(null, false, 10));
        Assert.Equal(id, result.TransponderId);
        Assert.Equal(id.ToString(), result.TransponderLabel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StatusService_MapsWarningsAndRetainsStatusOnFacilityFailure(bool failFacility)
    {
        var expected = TransponderStatusTestData.View();
        var sighting = expected.Status.Warnings[0];
        var timing = PlatformServiceTests.TestProxy.Create<ITimingClient>((method, args) =>
        {
            Assert.Equal(nameof(ITimingClient.GetTransponderStatusAsync), method.Name);
            Assert.Equal(expected.Status.Id, args[0]);
            Assert.Equal(2, args[1]);
            Assert.Equal(3, args[2]);
            Assert.Equal(10, args[3]);
            return new TransponderStatus
            {
                Id = expected.Status.Id, Label = expected.Status.Label, Code = expected.Status.Code,
                System = "MyLaps_X2", SystemId = expected.Status.SystemId, Administration = true,
                WarningWindowStart = expected.Status.WarningWindowStart, WarningWindowEnd = expected.Status.WarningWindowEnd,
                LowBattery = true, LowStrength = true, LowHits = true, OwnershipCount = 1, WarningCount = 1,
                Ownerships = [new TransponderOwnershipDTO { Id = Guid.NewGuid(), OwnerId = TransponderStatusTestData.OwnerId,
                    TransponderId = expected.Status.Id, OwnedFrom = sighting.Time.AddDays(-1) }],
                Warnings = [new TransponderSighting { Id = sighting.Id, Time = sighting.Time,
                    FacilityId = sighting.FacilityId, InstallationId = sighting.InstallationId, Installation = sighting.Installation,
                    TimingPointId = sighting.TimingPointId, TimingPoint = sighting.TimingPoint,
                    LowBattery = true, LowStrength = true, LowHits = true }]
            };
        });
        var facilities = PlatformServiceTests.TestProxy.Create<IFacilitiesClient>((method, args) =>
        {
            Assert.Equal(nameof(IFacilitiesClient.Facility2Async), method.Name);
            Assert.Equal(sighting.FacilityId, args[0]);
            if (failFacility) throw new HttpRequestException("offline");
            return new FacilityDTO { Id = sighting.FacilityId, Name = "Arena" };
        });
        using var cacheHost = new WebUiCacheTestHost(facilities);
        var service = new TransponderStatusService(timing, NullLogger<TransponderStatusService>.Instance, cacheHost.Cache);
        var result = await service.GetAsync(expected.Status.Id, 2, 3);
        Assert.Equal(sighting, Assert.Single(result.Status.Warnings));
        Assert.Equal(TransponderStatusTestData.OwnerId, Assert.Single(result.Status.Ownerships).OwnerId);
        if (failFacility)
        {
            Assert.NotNull(result.FacilityError);
            Assert.Empty(result.FacilityNames);
        }
        else
        {
            Assert.Null(result.FacilityError);
            Assert.Equal("Arena", result.FacilityNames[sighting.FacilityId]);
        }
    }

    [Fact]
    public async Task StatusService_DoesNotResolvePrivateNamesOrLocationsAfterTimingDeniesAccess()
    {
        var timing = PlatformServiceTests.TestProxy.Create<ITimingClient>((_, _) =>
            throw new VeloTime.Module.Timing.Client.ApiException("denied", 404, "", null!, null!));
        var facilities = PlatformServiceTests.TestProxy.Create<IFacilitiesClient>((_, _) => throw new InvalidOperationException("Must not resolve"));
        using var cacheHost = new WebUiCacheTestHost(facilities);
        var service = new TransponderStatusService(timing, NullLogger<TransponderStatusService>.Instance, cacheHost.Cache);
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetAsync(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Empty(((PlatformServiceTests.TestProxy)(object)facilities).Calls);
    }
}

internal static class TransponderStatusTestData
{
    internal static readonly Guid OwnerId = Guid.Parse("da04e68b-d0e2-4590-9e83-689eab2bbf83");
    internal static TransponderStatusView View(bool administration = true)
    {
        var id = Guid.NewGuid();
        var time = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        var sighting = new Contracts.TransponderSighting(Guid.NewGuid(), time, Guid.NewGuid(), Guid.NewGuid(),
            "Track installation", Guid.NewGuid(), "Finish", true, true, true);
        return new(new(id, "Training label", "HT-24422", "106187718", "MyLaps_X2", administration,
            sighting, time.AddDays(-7), time, true, true, true,
            [new(Guid.NewGuid(), id, OwnerId, time.AddDays(-1), null)], 1, [sighting], 1),
            new Dictionary<Guid, string> { [sighting.FacilityId] = "Arena" }, null);
    }
}
