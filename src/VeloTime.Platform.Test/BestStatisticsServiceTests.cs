using System.Net;
using Microsoft.Extensions.Caching.Memory;
using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Statistics.Client;
using VeloTime.WebUI.Mud.Services;

namespace VeloTime.Platform.Test;

public sealed class BestStatisticsServiceTests
{
    [Fact]
    public async Task BestResults_ForwardsFiltersAndResolvesEachFacilityOnce()
    {
        var facilityId = Guid.NewGuid();
        var from = DateTimeOffset.UtcNow.AddDays(-30);
        var to = from.AddDays(30);
        using var cancellation = new CancellationTokenSource();
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((method, args) =>
        {
            Assert.Equal(nameof(IStatisticsClient.GetBestStatisticsResultsAsync), method.Name);
            Assert.Equal(facilityId, args[0]);
            Assert.Equal(from, args[1]);
            Assert.Equal(to, args[2]);
            Assert.Equal(cancellation.Token, args[3]);
            return new List<BestStatisticsResultDTO>
            {
                new() { Id = Guid.NewGuid(), StatisticsItemName = "Lap", ProfileName = "Public rider",
                    FacilityId = facilityId, Duration = "00:00:20.1234567", Speed = 45 },
                new() { Id = Guid.NewGuid(), StatisticsItemName = "Sprint", ProfileName = "Another rider",
                    FacilityId = facilityId, Duration = "00:00:15", Speed = 48 }
            };
        });
        var facilities = PlatformServiceTests.TestProxy.Create<IFacilitiesClient>((method, args) =>
        {
            Assert.Equal(nameof(IFacilitiesClient.FacilityAsync), method.Name);
            Assert.Equal(facilityId, args[0]);
            return new FacilityDTO { Id = facilityId, Name = "Arena" };
        });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new StatisticsService(statistics, facilities, cache);

        var rows = await service.GetBestResultsAsync(facilityId, from, to, cancellation.Token);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("Arena", row.FacilityName));
        Assert.Equal("Public rider", rows[0].ProfileName);
        Assert.Equal(TimeSpan.Parse("00:00:20.1234567"), rows[0].Duration);
        Assert.Equal(45, rows[0].Speed);
        Assert.Single(((PlatformServiceTests.TestProxy)(object)facilities).Calls);
    }

    [Fact]
    public async Task BestResults_EmptyDataDoesNotRequestFacilities()
    {
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((_, _) => new List<BestStatisticsResultDTO>());
        var facilities = PlatformServiceTests.TestProxy.Create<IFacilitiesClient>((_, _) => throw new InvalidOperationException());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        Assert.Empty(await new StatisticsService(statistics, facilities, cache).GetBestResultsAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BestResults_SurfacesFacilityFailures(bool missing)
    {
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((_, _) =>
            new List<BestStatisticsResultDTO> { new() { FacilityId = Guid.NewGuid(), Duration = "00:00:20" } });
        var facilities = PlatformServiceTests.TestProxy.Create<IFacilitiesClient>((_, _) => missing
            ? new FacilityDTO { Name = "" }
            : throw new VeloTime.Module.Facilities.Client.ApiException("secret", 404, "secret body", null!, null!));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new StatisticsService(statistics, facilities, cache).GetBestResultsAsync());
        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Fact]
    public async Task BestResults_SurfacesStatisticsFailures()
    {
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((_, _) =>
            throw new VeloTime.Module.Statistics.Client.ApiException("secret", 503, "secret body", null!, null!));
        var facilities = PlatformServiceTests.TestProxy.Create<IFacilitiesClient>((_, _) => throw new InvalidOperationException());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new StatisticsService(statistics, facilities, cache).GetBestResultsAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.DoesNotContain("secret", error.Message);
    }
}
