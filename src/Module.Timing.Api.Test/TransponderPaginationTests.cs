using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Api.Test;

public sealed partial class TimingEndpointTests
{
    [Fact]
    public async Task TransponderPages_EnforceMaximumAndStablyPageDuplicateLabels()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
            for (var index = 0; index < 125; index++)
                storage.Add(new MylapsX2Transponder
                {
                    Id = Guid.NewGuid(), SystemId = (106187718 + index).ToString(), Label = "Duplicate"
                });
            await storage.SaveChangesAsync();
        }
        using var client = factory.CreateClient();
        var first = (await client.GetFromJsonAsync<TransponderPage>("/api/timing/transponders?pageSize=100"))!;
        var second = (await client.GetFromJsonAsync<TransponderPage>("/api/timing/transponders?pageSize=100&page=1"))!;
        var repeat = (await client.GetFromJsonAsync<TransponderPage>("/api/timing/transponders?pageSize=100"))!;
        Assert.Equal(125, first.TotalCount);
        Assert.Equal(100, first.Items.Count);
        Assert.Equal(25, second.Items.Count);
        Assert.Equal(first.Items.Select(row => row.Transponder.Id), repeat.Items.Select(row => row.Transponder.Id));
        Assert.Empty(first.Items.Select(row => row.Transponder.Id).Intersect(second.Items.Select(row => row.Transponder.Id)));
    }

    [Fact]
    public async Task TransponderPages_SliceOwnershipRowsAndCountBeforePaging()
    {
        var transponderId = await SeedPagedTransponders();
        using var client = factory.CreateClient();
        var first = (await client.GetFromJsonAsync<TransponderPage>("/api/timing/transponders?pageSize=10"))!;
        var next = (await client.GetFromJsonAsync<TransponderPage>("/api/timing/transponders?page=1&pageSize=10"))!;
        Assert.Equal(24, first.TotalCount);
        Assert.Equal(24, next.TotalCount);
        Assert.Equal(10, first.Items.Count);
        Assert.Equal(10, next.Items.Count);
        Assert.Equal(3, first.Items.Count(row => row.Transponder.Id == transponderId));
        Assert.Empty(first.Items.Select(row => row.Transponder.Id).Intersect(next.Items.Select(row => row.Transponder.Id)));
        Assert.All(first.Items, row => Assert.Empty(row.Transponder.Ownerships));
        Assert.Equal(first.Items.Select(row => row.Transponder.Label).Order(), first.Items.Select(row => row.Transponder.Label));
        var repeated = (await client.GetFromJsonAsync<TransponderPage>("/api/timing/transponders?pageSize=10"))!;
        Assert.Equal(first.Items.Select(row => (row.Transponder.Id, row.Ownership?.Id)),
            repeated.Items.Select(row => (row.Transponder.Id, row.Ownership?.Id)));
        var empty = (await client.GetFromJsonAsync<TransponderPage>("/api/timing/transponders?page=20"))!;
        Assert.Equal(24, empty.TotalCount);
        Assert.Empty(empty.Items);

        var split = (await client.GetFromJsonAsync<TransponderPage>("/api/timing/transponders?page=1&pageSize=1"))!;
        Assert.Equal(transponderId, Assert.Single(split.Items).Transponder.Id);
        Assert.NotEqual(first.Items[0].Ownership!.Id, split.Items[0].Ownership!.Id);
    }

    [Fact]
    public async Task TransponderPages_FilterHistoryAndSearchBeforeCountAndRespectPersonalScope()
    {
        var transponderId = await SeedPagedTransponders();
        using var client = factory.CreateClient();
        var active = (await client.GetFromJsonAsync<TransponderPage>("/api/timing/transponders?includeHistory=false"))!;
        Assert.Equal(22, active.TotalCount);
        Assert.Single(active.Items, row => row.Transponder.Id == transponderId);
        Assert.Equal(9, active.Items.Count(row => row.Ownership is null));
        var matching = (await client.GetFromJsonAsync<TransponderPage>("/api/timing/transponders?search=shared&pageSize=1"))!;
        Assert.Equal(3, matching.TotalCount);
        Assert.Single(matching.Items);
        var system = (await client.GetFromJsonAsync<TransponderPage>("/api/timing/transponders?search=106187718"))!;
        Assert.Equal(3, system.TotalCount);
        using var response = await client.PostAsJsonAsync("/api/timing/transponders/query",
            new TransponderPageRequest(0, 10, "not a label", false, [transponderId]));
        var selected = (await response.Content.ReadFromJsonAsync<TransponderPage>())!;
        Assert.Equal(transponderId, Assert.Single(selected.Items).Transponder.Id);

        client.DefaultRequestHeaders.Add("X-Test-Role", "user");
        var personal = (await client.GetFromJsonAsync<TransponderPage>("/api/timing/my/transponders"))!;
        Assert.Equal(2, personal.TotalCount);
        Assert.All(personal.Items, row => Assert.Equal(TestAuthenticationHandler.DefaultOwnerId, row.Ownership!.OwnerId));
        using var filteredResponse = await client.PostAsJsonAsync("/api/timing/my/transponders/query",
            new TransponderPageRequest(0, 10, "", false, [transponderId, Guid.NewGuid()]));
        var filtered = (await filteredResponse.Content.ReadFromJsonAsync<TransponderPage>())!;
        Assert.Equal(TestAuthenticationHandler.DefaultOwnerId, Assert.Single(filtered.Items).Ownership!.OwnerId);
        var lookup = (await client.GetFromJsonAsync<TransponderLookupPage>("/api/timing/my/transponders/lookup?includeOwners=true"))!;
        Assert.All(Assert.Single(lookup.Items).OwnerIds, id => Assert.Equal(TestAuthenticationHandler.DefaultOwnerId, id));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/timing/transponders/lookup")).StatusCode);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task TransponderPages_RejectInvalidOffsetsAndSizes(int page, int pageSize)
    {
        using var client = factory.CreateClient();
        foreach (var root in new[] { "/api/timing/transponders", "/api/timing/my/transponders" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{root}?page={page}&pageSize={pageSize}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{root}/lookup?page={page}&pageSize={pageSize}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{root}/query",
                new TransponderPageRequest(page, pageSize, "", true))).StatusCode);
        }
    }

    [Fact]
    public async Task TransponderLookup_IsBoundedAndIndependentOfOwnershipRows()
    {
        await SeedPagedTransponders();
        using var client = factory.CreateClient();
        var first = (await client.GetFromJsonAsync<TransponderLookupPage>("/api/timing/transponders/lookup"))!;
        var next = (await client.GetFromJsonAsync<TransponderLookupPage>("/api/timing/transponders/lookup?page=1"))!;
        Assert.Equal(22, first.TotalCount);
        Assert.Equal(10, first.Items.Count);
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(next.Items.Select(item => item.Id)));
        Assert.All(first.Items, item => Assert.Empty(item.OwnerIds));
        var candidates = (await client.GetFromJsonAsync<TransponderLookupPage>("/api/timing/transponders/lookup?includeOwners=true&search=SHARED"))!;
        Assert.Equal(3, Assert.Single(candidates.Items).OwnerIds.Count);
    }

    [Fact]
    public async Task TransponderQuery_AcceptsLargeMatchingSetsWithoutUrlOrParameterTruncation()
    {
        var id = await SeedPagedTransponders();
        using var client = factory.CreateClient();
        var ids = Enumerable.Range(0, 3000).Select(_ => Guid.NewGuid()).Append(id).ToList();
        using var response = await client.PostAsJsonAsync("/api/timing/transponders/query", new TransponderPageRequest(0, 10, "", true, ids));
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<TransponderPage>())!;
        Assert.Equal(3, result.TotalCount);
        Assert.All(result.Items, row => Assert.Equal(id, row.Transponder.Id));
    }

    private async Task<Guid> SeedPagedTransponders()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
        var shared = new MylapsX2Transponder { Id = Guid.NewGuid(), SystemId = "106187718", Label = "00 Shared" };
        var now = DateTime.UtcNow;
        shared.Owners.Add(new() { Id = Guid.NewGuid(), TransponderId = shared.Id, OwnerId = TestAuthenticationHandler.DefaultOwnerId, OwnedFrom = now.AddDays(-4), OwnedTo = now.AddDays(-3) });
        shared.Owners.Add(new() { Id = Guid.NewGuid(), TransponderId = shared.Id, OwnerId = TestAuthenticationHandler.DefaultOwnerId, OwnedFrom = now.AddDays(-1) });
        shared.Owners.Add(new() { Id = Guid.NewGuid(), TransponderId = shared.Id, OwnerId = Guid.NewGuid(), OwnedFrom = now.AddDays(1) });
        storage.Add(shared);
        for (var index = 1; index <= 21; index++)
            storage.Add(new MylapsX2Transponder
            {
                Id = Guid.NewGuid(), SystemId = (106187718 + index).ToString(), Label = $"{index:00} Transponder"
            });
        await storage.SaveChangesAsync();
        return shared.Id;
    }
}
