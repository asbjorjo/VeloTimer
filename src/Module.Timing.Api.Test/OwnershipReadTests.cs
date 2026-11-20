using System.Net;
using System.Net.Http.Json;
using VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.Module.Timing.Api.Test;

public sealed partial class TimingEndpointTests
{
    [Theory]
    [InlineData("statistics-processor", HttpStatusCode.OK)]
    [InlineData("timing-admin", HttpStatusCode.OK)]
    [InlineData("velotime-admin", HttpStatusCode.OK)]
    [InlineData("statistics-admin", HttpStatusCode.Forbidden)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    public async Task OwnershipRead_RequiresNarrowPermission(string role, HttpStatusCode expected)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        var response = await client.GetAsync($"/api/timing/ownerships/{Guid.NewGuid()}");
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
            Assert.Empty((await response.Content.ReadFromJsonAsync<List<TransponderOwnershipDTO>>())!);
    }

    [Fact]
    public async Task OwnershipRead_RequiresAuthentication()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/timing/ownerships/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task OwnershipRead_ReturnsAllPeriodsWithoutGrantingWriteAccess()
    {
        using var client = factory.CreateClient();
        var id = await SeedUnownedTransponder();
        var requests = new[]
        {
            new SaveTransponderOwnershipRequest(id, Guid.NewGuid(), new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new SaveTransponderOwnershipRequest(id, Guid.NewGuid(), new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), null)
        };
        foreach (var request in requests)
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/timing/admin/ownerships", request)).StatusCode);

        client.DefaultRequestHeaders.Add("X-Test-Role", "statistics-processor");
        var owners = await client.GetFromJsonAsync<List<TransponderOwnershipDTO>>($"/api/timing/ownerships/{id}");
        Assert.NotNull(owners);
        Assert.Equal(requests.Select(request => request.OwnerId), owners.Select(owner => owner.OwnerId));
        Assert.Equal(requests.Select(request => request.OwnedFrom), owners.Select(owner => owner.OwnedFrom));
        Assert.Equal(requests.Select(request => request.OwnedTo), owners.Select(owner => owner.OwnedTo));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/timing/admin/ownerships", requests[0])).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/timing/transponders/{id}")).StatusCode);
    }
}
