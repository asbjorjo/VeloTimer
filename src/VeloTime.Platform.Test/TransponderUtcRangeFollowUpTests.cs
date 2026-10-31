using System.Globalization;
using System.Net;
using VeloTime.Module.Timing.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Services;
using TimingContracts = VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.Platform.Test;

public sealed partial class WebUiBffTests
{
    private static readonly DateTimeOffset LastSeenStart = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LastSeenEnd = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OwnedFromStart = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OwnedFromEnd = new(2026, 4, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OwnedToStart = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OwnedToEnd = new(2026, 5, 2, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("api/transponders", "user", false)]
    [InlineData("api/admin/timing/transponders", "timing-admin", true)]
    public async Task TransponderListingBff_ForwardsAllThreeUtcRanges(string path, string role, bool administration)
    {
        var backend = PlatformServiceTests.TestProxy.Create<ITransponderManagementService>((method, arguments) =>
        {
            Assert.Equal(nameof(ITransponderManagementService.GetTranspondersAsync), method.Name);
            Assert.Equal(administration, arguments[0]);
            var filters = Assert.IsType<TimingContracts.TransponderColumnFilters>(arguments[6]);
            Assert.Equal(new TimingContracts.UtcInstantRange(LastSeenStart, LastSeenEnd), filters.LastSeenUtcRange);
            Assert.Equal(new TimingContracts.UtcInstantRange(OwnedFromStart, OwnedFromEnd), filters.OwnedFromUtcRange);
            Assert.Equal(new TimingContracts.UtcInstantRange(OwnedToStart, OwnedToEnd), filters.OwnedToUtcRange);
            Assert.Null(filters.LastSeen);
            Assert.Null(filters.OwnedFrom);
            Assert.Null(filters.OwnedTo);
            return new TimingContracts.TransponderPage([], 0);
        });
        await using var app = await CreateAppAsync(transponders: backend);
        using var client = CreateClient(app);
        await client.GetAsync($"signin?role={role}");

        var query = string.Join("&", new[]
        {
            Range("lastSeen", LastSeenStart, LastSeenEnd),
            Range("ownedFrom", OwnedFromStart, OwnedFromEnd),
            Range("ownedTo", OwnedToStart, OwnedToEnd)
        });
        using var response = await client.GetAsync($"{path}?{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(((PlatformServiceTests.TestProxy)(object)backend).Calls);
    }

    [Theory]
    [InlineData("api/transponders", "user", false)]
    [InlineData("api/admin/timing/transponders", "timing-admin", true)]
    public async Task TransponderListingBff_PreservesLegacyDateFilters(string path, string role, bool administration)
    {
        var backend = PlatformServiceTests.TestProxy.Create<ITransponderManagementService>((method, arguments) =>
        {
            Assert.Equal(nameof(ITransponderManagementService.GetTranspondersAsync), method.Name);
            Assert.Equal(administration, arguments[0]);
            var filters = Assert.IsType<TimingContracts.TransponderColumnFilters>(arguments[6]);
            Assert.Equal(new DateOnly(2026, 3, 1), filters.LastSeen);
            Assert.Equal(new DateOnly(2026, 4, 2), filters.OwnedFrom);
            Assert.Equal(new DateOnly(2026, 5, 3), filters.OwnedTo);
            Assert.Null(filters.LastSeenUtcRange);
            Assert.Null(filters.OwnedFromUtcRange);
            Assert.Null(filters.OwnedToUtcRange);
            return new TimingContracts.TransponderPage([], 0);
        });
        await using var app = await CreateAppAsync(transponders: backend);
        using var client = CreateClient(app);
        await client.GetAsync($"signin?role={role}");

        using var response = await client.GetAsync($"{path}?lastSeenFilter=2026-03-01&ownedFromFilter=2026-04-02&ownedToFilter=2026-05-03");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(((PlatformServiceTests.TestProxy)(object)backend).Calls);
    }

    [Theory]
    [InlineData("api/transponders", "user")]
    [InlineData("api/admin/timing/transponders", "timing-admin")]
    public async Task TransponderListingBff_RejectsInvalidUtcRangesWithoutCallingService(string path, string role)
    {
        var invalidQueries = new[]
        {
            "lastSeenUtcStart=2026-03-01T10%3A00%3A00Z", // unpaired
            "ownedFromUtcEnd=2026-04-02T00%3A00%3A00Z", // unpaired
            "ownedToUtcStart=2026-05-01T00%3A00%3A00%2B02%3A00&ownedToUtcEnd=2026-05-02T00%3A00%3A00%2B02%3A00", // non-UTC offset
            "lastSeenUtcStart=2026-03-02T10%3A00%3A00Z&lastSeenUtcEnd=2026-03-01T10%3A00%3A00Z", // reversed
            "ownedFromUtcStart=2026-04-01T00%3A00%3A00Z&ownedFromUtcEnd=2026-04-01T00%3A00%3A00Z", // equal
            "ownedToUtcStart=2026-05-01T00%3A00%3A00Z&ownedToUtcEnd=2026-05-02T00%3A00%3A00Z&ownedToFilter=2026-05-01" // conflict
        };
        var backend = PlatformServiceTests.TestProxy.Create<ITransponderManagementService>((_, _) =>
            throw new InvalidOperationException("Invalid UTC ranges must be rejected before reaching the service."));
        await using var app = await CreateAppAsync(transponders: backend);
        using var client = CreateClient(app);
        await client.GetAsync($"signin?role={role}");

        foreach (var query in invalidQueries)
        {
            using var response = await client.GetAsync($"{path}?{query}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Empty(((PlatformServiceTests.TestProxy)(object)backend).Calls);
    }

    private static string Range(string name, DateTimeOffset start, DateTimeOffset end) =>
        $"{name}UtcStart={Uri.EscapeDataString(start.ToString("O", CultureInfo.InvariantCulture))}&" +
        $"{name}UtcEnd={Uri.EscapeDataString(end.ToString("O", CultureInfo.InvariantCulture))}";
}

public sealed class TransponderManagementUtcRangeServiceTests
{
    [Fact]
    public async Task ServerAdapterMapsEachUtcRangeToTheGeneratedTimingFilter()
    {
        var lastSeen = new TimingContracts.UtcInstantRange(
            new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero), new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero));
        var ownedFrom = new TimingContracts.UtcInstantRange(
            new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 4, 2, 0, 0, 0, TimeSpan.Zero));
        var ownedTo = new TimingContracts.UtcInstantRange(
            new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));
        using var source = new CancellationTokenSource();
        var token = source.Token;
        var timing = PlatformServiceTests.TestProxy.Create<ITimingClient>((method, arguments) =>
        {
            Assert.Equal(nameof(ITimingClient.QueryTranspondersAsync), method.Name);
            Assert.Equal(token, arguments[1]);
            var body = Assert.IsType<TransponderPageRequest>(arguments[0]);
            var converted = Assert.IsType<VeloTime.Module.Timing.Client.TransponderColumnFilters>(body.Filters);
            Assert.Equal(lastSeen.StartInclusive, converted.LastSeenUtcRange.StartInclusive);
            Assert.Equal(lastSeen.EndExclusive, converted.LastSeenUtcRange.EndExclusive);
            Assert.Equal(ownedFrom.StartInclusive, converted.OwnedFromUtcRange.StartInclusive);
            Assert.Equal(ownedFrom.EndExclusive, converted.OwnedFromUtcRange.EndExclusive);
            Assert.Equal(ownedTo.StartInclusive, converted.OwnedToUtcRange.StartInclusive);
            Assert.Equal(ownedTo.EndExclusive, converted.OwnedToUtcRange.EndExclusive);
            return new TransponderPage { Items = [], TotalCount = 0 };
        });
        var filters = new TimingContracts.TransponderColumnFilters
        {
            LastSeenUtcRange = lastSeen,
            OwnedFromUtcRange = ownedFrom,
            OwnedToUtcRange = ownedTo
        };

        await new TransponderManagementService(timing, UnusedOwners())
            .GetTranspondersAsync(true, cancellationToken: token, filters: filters);

        Assert.Single(((PlatformServiceTests.TestProxy)(object)timing).Calls);
    }

    private static ITransponderOwnerService UnusedOwners() =>
        PlatformServiceTests.TestProxy.Create<ITransponderOwnerService>((_, _) =>
            throw new InvalidOperationException("Owner service is not used when listing without search filters."));
}
