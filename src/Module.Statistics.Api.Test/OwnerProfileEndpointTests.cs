using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class OwnerProfileEndpointTests(StatisticsApiFactory factory) : IClassFixture<StatisticsApiFactory>, IAsyncLifetime
{
    private const string AdminRoot = "/api/statistics/admin/owner-profiles";
    private const string Self = "/api/statistics/owner-profiles/me";
    private static readonly Guid OwnerId = Guid.Parse("da04e68b-d0e2-4590-9e83-689eab2bbf83");

    public Task InitializeAsync() => factory.InitializeDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Self_ReturnsOnlyOwnPrivateNameAndIgnoresForgedSubjectQuery()
    {
        await Seed(new StatsProfile { UserId = OwnerId, ProfileName = "Private rider", IsPublic = false },
            new StatsProfile { UserId = Guid.NewGuid(), ProfileName = "Different rider", IsPublic = false });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Subject", OwnerId.ToString());
        var profile = await client.GetFromJsonAsync<OwnerProfileDTO>($"{Self}?userId={Guid.NewGuid()}");
        Assert.Equal(new OwnerProfileDTO(OwnerId, "Private rider"), profile);
    }

    [Fact]
    public async Task Self_DistinguishesMissingProfileFromInvalidSubject()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Self)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Subject", OwnerId.ToString());
        Assert.Equal(new OwnerProfileDTO(OwnerId, null), await client.GetFromJsonAsync<OwnerProfileDTO>(Self));
    }

    [Theory]
    [InlineData("user", false)]
    [InlineData("statistics-admin", false)]
    [InlineData("facilities-admin", false)]
    [InlineData("timing-admin", true)]
    [InlineData("velotime-admin", true)]
    public async Task Admin_SearchRequiresTimingButResolutionAllowsAllAdministrators(string role, bool allowed)
    {
        await Seed(new StatsProfile { UserId = OwnerId, ProfileName = "Private racer", IsPublic = false });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        var response = await client.PostAsJsonAsync($"{AdminRoot}/resolve", new ResolveOwnerProfilesRequest([OwnerId, OwnerId, Guid.NewGuid()]));
        var search = await client.GetAsync($"{AdminRoot}/search?query=racer&offset=0&pageSize=10");
        var canResolve = role != "user";
        Assert.Equal(canResolve ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, search.StatusCode);
        if (canResolve)
            Assert.Equal(new OwnerProfileDTO(OwnerId, "Private racer"), Assert.Single((await response.Content.ReadFromJsonAsync<List<OwnerProfileDTO>>())!));
        if (allowed)
        {
            Assert.Equal(OwnerId, Assert.Single((await search.Content.ReadFromJsonAsync<OwnerProfilePage>())!.Profiles).UserId);
        }
    }

    [Fact]
    public async Task Search_IsCaseInsensitiveBoundedAndDeterministicallyPaged()
    {
        await Seed(Enumerable.Range(0, 23).Select(index => new StatsProfile
        {
            UserId = Guid.NewGuid(), ProfileName = $"Racer {index:D2}", IsPublic = index % 2 == 0
        }).ToArray());
        using var client = AdminClient();
        var first = (await client.GetFromJsonAsync<OwnerProfilePage>($"{AdminRoot}/search?query=%20rAcEr%20&offset=0&pageSize=20"))!;
        Assert.Equal(20, first.Profiles.Count);
        Assert.Equal(20, first.NextOffset);
        var last = (await client.GetFromJsonAsync<OwnerProfilePage>($"{AdminRoot}/search?query=racer&offset={first.NextOffset}&pageSize=20"))!;
        Assert.Equal(3, last.Profiles.Count);
        Assert.Null(last.NextOffset);
        Assert.Equal(23, first.Profiles.Concat(last.Profiles).Select(profile => profile.UserId).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, 23).Select(index => $"Racer {index:D2}"), first.Profiles.Concat(last.Profiles).Select(profile => profile.ProfileName));
    }

    [Theory]
    [InlineData("a", 0, 20)]
    [InlineData("ra", -1, 20)]
    [InlineData("ra", 0, 0)]
    [InlineData("ra", 0, 21)]
    public async Task Search_RejectsInvalidBounds(string query, int offset, int pageSize)
    {
        using var client = AdminClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{AdminRoot}/search?query={query}&offset={offset}&pageSize={pageSize}")).StatusCode);
    }

    [Fact]
    public async Task Resolve_RejectsInvalidAndOversizedBatches()
    {
        using var client = AdminClient();
        foreach (var ids in new[] { new[] { Guid.Empty }, Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray() })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{AdminRoot}/resolve", new ResolveOwnerProfilesRequest(ids))).StatusCode);
        Assert.Empty((await (await client.PostAsJsonAsync($"{AdminRoot}/resolve", new ResolveOwnerProfilesRequest([]))).Content.ReadFromJsonAsync<List<OwnerProfileDTO>>())!);
    }

    [Fact]
    public async Task OwnerLookups_RejectAnonymousAccess()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Self)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{AdminRoot}/search?query=racer&offset=0&pageSize=10")).StatusCode);
    }

    private HttpClient AdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "timing-admin");
        return client;
    }

    private async Task Seed(params StatsProfile[] profiles)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
        storage.AddRange(profiles);
        await storage.SaveChangesAsync();
    }
}
