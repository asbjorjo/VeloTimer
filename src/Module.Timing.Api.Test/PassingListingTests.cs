using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Api.Test;

public sealed class PassingListingTests(TimingApiFactory factory) : IClassFixture<TimingApiFactory>, IAsyncLifetime
{
    private static readonly ConcurrentDictionary<Guid, MylapsX2Transponder> Transponders = new();
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    [InlineData("timing-admin", HttpStatusCode.OK)]
    [InlineData("facilities-admin", HttpStatusCode.OK)]
    [InlineData("statistics-admin", HttpStatusCode.OK)]
    [InlineData("velotime-admin", HttpStatusCode.OK)]
    public async Task ListingUsesCrossDomainAdministratorPolicy(string? role, HttpStatusCode expected)
    {
        using var client = Client(role);
        using var response = await client.GetAsync(Url(Guid.NewGuid(), Start, Start));
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK) Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("")]
    [InlineData("transponderId=")]
    [InlineData("transponderId=00000000-0000-0000-0000-000000000000")]
    [InlineData("transponderId=not-a-guid")]
    [InlineData("transponderId=00000000-0000-0000-0000-000000000001&from=2026-11-01T00:00:00Z")]
    [InlineData("transponderId=00000000-0000-0000-0000-000000000001&to=2026-11-01T00:00:00Z")]
    [InlineData("transponderId=00000000-0000-0000-0000-000000000001&from=2026-11-01&to=2026-11-02T00:00:00Z")]
    [InlineData("transponderId=00000000-0000-0000-0000-000000000001&from=2026-11-01T00:00:00&to=2026-11-02T00:00:00Z")]
    [InlineData("transponderId=00000000-0000-0000-0000-000000000001&from=2026-11-01T00:00:00%2B01:00&to=2026-11-02T00:00:00Z")]
    [InlineData("transponderId=00000000-0000-0000-0000-000000000001&from=2026-11-02T00:00:00Z&to=2026-11-01T00:00:00Z")]
    [InlineData("transponderId=00000000-0000-0000-0000-000000000001&from=2026-11-01T00:00:00Z&to=2026-11-08T00:00:00.0000001Z")]
    [InlineData("transponderId=00000000-0000-0000-0000-000000000001&transponderId=00000000-0000-0000-0000-000000000002&from=2026-11-01T00:00:00Z&to=2026-11-01T00:00:00Z")]
    [InlineData("transponderId=00000000-0000-0000-0000-000000000001&from=2026-11-01T00:00:00Z&from=2026-11-01T00:00:00Z&to=2026-11-01T00:00:00Z")]
    [InlineData("transponderId=00000000-0000-0000-0000-000000000001&from=2026-11-01T00:00:00Z&to=2026-11-01T00:00:00Z&extra=1")]
    [InlineData("TransponderId=00000000-0000-0000-0000-000000000001&from=2026-11-01T00:00:00Z&to=2026-11-01T00:00:00Z")]
    public async Task ListingRejectsMissingMalformedDuplicateAndUnsupportedQueryValues(string query)
    {
        using var client = Client("timing-admin");
        using var response = await client.GetAsync("/api/timing/passings?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("errors", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ListingUsesInclusiveTransponderWindowOrderingAndPopulatesDescriptionsFlags()
    {
        var transponder = Guid.NewGuid();
        var otherTransponder = Guid.NewGuid();
        var end = Start.AddDays(7);
        var rows = new[]
        {
            Row(Guid.Parse("20000000-0000-0000-0000-000000000004"), transponder, Start, "Tie four", true, false, true),
            Row(Guid.Parse("20000000-0000-0000-0000-000000000003"), transponder, Start, "Tie three", false, true, false),
            Row(Guid.NewGuid(), transponder, end, "Inclusive end", true, true, true),
            Row(Guid.NewGuid(), transponder, Start.AddMilliseconds(-1), "Too early"),
            Row(Guid.NewGuid(), transponder, end.AddMilliseconds(1), "Too late"),
            Row(Guid.NewGuid(), otherTransponder, Start, "Other transponder")
        };
        await Seed(rows);
        using var client = Client("statistics-admin");
        using var response = await client.GetAsync(Url(transponder, Start, end));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var result = (await response.Content.ReadFromJsonAsync<TransponderPassingsDTO>())!;
        Assert.Equal(transponder, result.TransponderId);
        Assert.Equal(Start, result.From);
        Assert.Equal(end, result.To);
        Assert.Equal(TimeSpan.Zero, result.From.Offset);
        Assert.Equal(TimeSpan.Zero, result.To.Offset);
        Assert.False(result.Truncated);
        Assert.Equal(new[] { rows[1].Passing.Id, rows[0].Passing.Id, rows[2].Passing.Id }, result.Passings.Select(p => p.Id));
        var first = result.Passings[0];
        Assert.Equal(Start, first.Time);
        Assert.Equal("Tie three", first.TimingPointDescription);
        Assert.Equal("Installation Tie three", first.InstallationDescription);
        Assert.Equal(rows[1].Facility, first.FacilityId);
        Assert.False(first.LowBattery);
        Assert.True(first.LowStrength);
        Assert.False(first.LowHits);
        Assert.True(result.Passings[1].LowBattery);
        Assert.True(result.Passings[1].LowHits);
    }

    [Fact]
    public async Task ListingSupportsEqualAndExactlySevenDayWindowsAndDistinguishes500From501AndEmpty()
    {
        var transponder = Guid.NewGuid();
        var pointInTime = Start.AddDays(2);
        await Seed(Row(Guid.NewGuid(), transponder, pointInTime, "Equal bound"));
        using var client = Client("timing-admin");
        var equal = await List(client, transponder, pointInTime, pointInTime);
        Assert.Equal(pointInTime, Assert.Single(equal.Passings).Time);
        var sevenDays = await List(client, transponder, Start, Start.AddDays(7));
        Assert.Single(sevenDays.Passings);
        var empty = await List(client, Guid.NewGuid(), Start, Start.AddDays(7));
        Assert.Empty(empty.Passings);
        Assert.False(empty.Truncated);

        var capped = Guid.NewGuid();
        await Seed(Enumerable.Range(0, 501).Select(i => Row(Guid.NewGuid(), capped, Start.AddMilliseconds(i), "Cap " + i)).ToArray());
        var result501 = await List(client, capped, Start, Start.AddDays(7));
        Assert.Equal(500, result501.Passings.Count);
        Assert.True(result501.Truncated);
        var exactly500Id = Guid.NewGuid();
        await Seed(Enumerable.Range(0, 500).Select(i => Row(Guid.NewGuid(), exactly500Id, Start.AddMilliseconds(i), "500 " + i)).ToArray());
        var result500 = await List(client, exactly500Id, Start, Start.AddDays(7));
        Assert.Equal(500, result500.Passings.Count);
        Assert.False(result500.Truncated);
    }

    private HttpClient Client(string? role)
    {
        var client = factory.CreateClient();
        if (role is null) client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        else client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }
    private static string Url(Guid transponder, DateTimeOffset from, DateTimeOffset to) =>
        $"/api/timing/passings?transponderId={transponder:D}&from={Uri.EscapeDataString(from.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}&to={Uri.EscapeDataString(to.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}";
    private static async Task<TransponderPassingsDTO> List(HttpClient client, Guid transponder, DateTimeOffset from, DateTimeOffset to)
    {
        using var response = await client.GetAsync(Url(transponder, from, to));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<TransponderPassingsDTO>())!;
    }
    private async Task Seed(params RowSeed[] rows)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
        storage.AddRange(rows.SelectMany(row => new object[] { row.Transponder, row.Installation, row.Point, row.Passing }));
        await storage.SaveChangesAsync();
    }
    private static RowSeed Row(Guid id, Guid transponderId, DateTimeOffset time, string description,
        bool lowBattery = false, bool lowStrength = false, bool lowHits = false)
    {
        var transponder = Transponders.GetOrAdd(transponderId, key => new MylapsX2Transponder
        {
            Id = key, SystemId = Random.Shared.NextInt64(100000000, 999999999).ToString(CultureInfo.InvariantCulture)
        });
        var installation = new Installation { Id = Guid.NewGuid(), AgentId = Guid.NewGuid().ToString("N"),
            Description = "Installation " + description, Facility = Guid.NewGuid() };
        var point = new TimingPoint { Id = Guid.NewGuid(), SystemId = Guid.NewGuid().ToString("N"),
            Description = description, InstallationId = installation.Id, Installation = installation };
        var passing = new Passing { Id = id, Time = time.UtcDateTime, Transponder = transponder,
            TransponderId = transponderId, TimingPoint = point, Installation = point.Installation, SourcePassingId = Guid.NewGuid(), TimingPointId = point.Id,
            LowBattery = lowBattery, LowStrength = lowStrength, LowHits = lowHits };
        return new(transponder, installation, point, passing, installation.Facility);
    }
    private sealed record RowSeed(MylapsX2Transponder Transponder, Installation Installation, TimingPoint Point, Passing Passing, Guid Facility);
}
