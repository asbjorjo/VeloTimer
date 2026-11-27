using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;
using Assert = Xunit.Assert;

namespace VeloTime.Module.Statistics.Api.Test;

/// <summary>
/// Replay-safe plan Phase 4 (C3, decision §6.1): definition edits are dated. Each edit applies from its effective
/// instant, keeps earlier versions, and marks results from that instant dirty for the recompute worker.
/// </summary>
public sealed class StatisticsDefinitionVersioningPostgreSqlTests
{
    private static readonly DateTime X = new(2026, 11, 3, 11, 0, 0, DateTimeKind.Utc);

    [PublicStatisticsPostgreSqlFact]
    public async Task BackdatedItemThresholdSplitsEveryConfigurationAtXAndRecomputesFromX()
    {
        await using var h = await Harness.Create();
        var item = await h.CreateItem();
        var first = await h.CreateConfig(item.Id);
        var second = await h.CreateConfig(item.Id, PublicStatisticsTestHost.End, PublicStatisticsTestHost.End);
        await h.ClearRequests();

        var edit = new SaveStatisticsItemRequest("Lap", "", 250, null, TimeSpan.FromSeconds(17.5)) { EffectiveFromUtc = X };
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsJsonAsync(ItemUrl(item.Id), edit)).StatusCode);

        foreach (var config in new[] { first, second })
        {
            var versions = await h.Versions(config.Id);
            Assert.Equal(2, versions.Count);
            Assert.Equal((null, X), (versions[0].ValidFromUtc, versions[0].ValidToUtc));
            Assert.Null(versions[0].ItemMaxDuration);
            Assert.Equal((X, null), (versions[1].ValidFromUtc, versions[1].ValidToUtc));
            Assert.Equal(TimeSpan.FromSeconds(17.5), versions[1].ItemMaxDuration);
        }
        var requests = await h.Requests();
        Assert.Equal(2, requests.Count);
        Assert.All(requests, r => Assert.Equal((null, X, (DateTime?)null, RecomputeReason.Definition), (r.TransponderId, r.FromUtc!.Value, r.ToUtc, r.Reason)));

        // The same values again change nothing and schedule nothing.
        await h.ClearRequests();
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsJsonAsync(ItemUrl(item.Id), edit)).StatusCode);
        Assert.Equal(2, (await h.Versions(first.Id)).Count);
        Assert.Empty(await h.Requests());
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task LaterEditsKeepValuesTheyDoNotChangeAndEditsWithoutADateApplyFromNow()
    {
        await using var h = await Harness.Create();
        var item = await h.CreateItem();
        var config = await h.CreateConfig(item.Id);
        var later = X.AddHours(1);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsJsonAsync(ConfigUrl(config.Id),
            ConfigRequest(item.Id) with { MaxDuration = TimeSpan.FromSeconds(30), EffectiveFromUtc = later })).StatusCode);
        // A distance change backdated before the threshold change reaches both later versions; the threshold stays dated.
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsJsonAsync(ItemUrl(item.Id),
            new SaveStatisticsItemRequest("Lap", "", 333) { EffectiveFromUtc = X })).StatusCode);
        var versions = await h.Versions(config.Id);
        Assert.Equal([(null, X, 250f, (TimeSpan?)null), (X, later, 333f, null), (later, null, 333f, TimeSpan.FromSeconds(30))],
            versions.Select(v => (v.ValidFromUtc, v.ValidToUtc, v.Distance, v.MaxDuration)).ToList());

        var before = DateTime.UtcNow;
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsJsonAsync(ConfigUrl(config.Id),
            ConfigRequest(item.Id) with { MaxDuration = TimeSpan.FromSeconds(40) })).StatusCode);
        var open = (await h.Versions(config.Id))[^1];
        Assert.InRange(open.ValidFromUtc!.Value, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(40), open.MaxDuration);
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task RecordedResultsNoLongerBlockStructuralEditsButStillGuardMovesAndDeletes()
    {
        await using var h = await Harness.Create();
        var item = await h.CreateItem();
        var other = await h.CreateItem("Other");
        var simple = await h.CreateConfig(item.Id);
        var multiRequest = new SaveStatisticsConfigurationRequest(item.Id, StatisticsConfigurationType.Multi, null, null, simple.Id, 2);
        var response = await h.Client.PostAsJsonAsync("/api/statistics/admin/configurations", multiRequest);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var multi = (await response.Content.ReadFromJsonAsync<StatisticsConfigurationDTO>())!;
        await h.Host.SeedAsync(Entry(simple), Entry(multi));

        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsJsonAsync(ConfigUrl(simple.Id),
            ConfigRequest(item.Id) with { CoursePointEnd = PublicStatisticsTestHost.Start, EffectiveFromUtc = X })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsJsonAsync(ConfigUrl(multi.Id),
            multiRequest with { Repetitions = 3, EffectiveFromUtc = X })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsJsonAsync(ItemUrl(item.Id),
            new SaveStatisticsItemRequest("Lap", "", 500) { EffectiveFromUtc = X })).StatusCode);
        Assert.Equal([2, 3], (await h.Versions(multi.Id)).Select(v => v.Repetitions!.Value).ToList());
        Assert.Equal(PublicStatisticsTestHost.Start, (await h.Versions(simple.Id))[^1].CoursePointEnd);

        Assert.Equal(HttpStatusCode.Conflict, (await h.Client.PutAsJsonAsync(ConfigUrl(simple.Id),
            ConfigRequest(other.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await h.Client.DeleteAsync(ConfigUrl(simple.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await h.Client.DeleteAsync(ItemUrl(item.Id))).StatusCode);
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task PositiveWholeMicrosecondValidationAndWriteAuthorization()
    {
        await using var h = await Harness.Create();
        var item = await h.CreateItem();
        foreach (var ticks in new long[] { -10, 0, 1, 9, 11 })
            Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PutAsJsonAsync(ItemUrl(item.Id),
                new SaveStatisticsItemRequest("Lap", "", 250, TimeSpan.FromTicks(ticks)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsJsonAsync(ItemUrl(item.Id),
            new SaveStatisticsItemRequest("Lap", "", 250, TimeSpan.FromTicks(10), TimeSpan.FromTicks(10)))).StatusCode);
        foreach (var role in new[] { "user", "timing-admin", "facilities-admin" })
        {
            using var client = h.Host.App.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(ItemUrl(item.Id),
                new SaveStatisticsItemRequest("No", "", 250))).StatusCode);
        }
        using var anonymous = PublicStatisticsDiscoveryTests.Anonymous(h.Host);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(ItemUrl(item.Id),
            new SaveStatisticsItemRequest("No", "", 250))).StatusCode);
    }

    private static string ItemUrl(Guid id) => $"/api/statistics/admin/items/{id}";
    private static string ConfigUrl(Guid id) => $"/api/statistics/admin/configurations/{id}";
    private static SaveStatisticsConfigurationRequest ConfigRequest(Guid item) => new(item,
        StatisticsConfigurationType.Simple, PublicStatisticsTestHost.Start, PublicStatisticsTestHost.End, null, null);
    private static StatisticsEntry Entry(StatisticsConfigurationDTO config) => new()
    {
        Id = Guid.NewGuid(), StatisticsItemId = config.StatisticsItemId, StatisticsItemConfigId = config.Id,
        TransponderId = Guid.NewGuid(), FacilityId = PublicStatisticsTestHost.Facility,
        TimeStart = X.AddMinutes(-10), TimeEnd = X.AddMinutes(-10).AddSeconds(18),
        Duration = TimeSpan.FromSeconds(18), Speed = 250d / 18
    };

    private sealed class Harness : IAsyncDisposable
    {
        public PublicStatisticsTestHost Host = null!;
        public HttpClient Client = null!;

        public static async Task<Harness> Create()
        {
            var h = new Harness { Host = await PublicStatisticsTestHost.CreateAsync(true) };
            h.Client = h.Host.App.CreateClient();
            h.Client.DefaultRequestHeaders.Add("X-Test-Role", "statistics-admin");
            return h;
        }

        public async Task<StatisticsItemDTO> CreateItem(string name = "Lap")
        {
            var response = await Client.PostAsJsonAsync("/api/statistics/admin/items", new SaveStatisticsItemRequest(name, "", 250));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<StatisticsItemDTO>())!;
        }

        public async Task<StatisticsConfigurationDTO> CreateConfig(Guid item, Guid? start = null, Guid? end = null)
        {
            var response = await Client.PostAsJsonAsync("/api/statistics/admin/configurations",
                ConfigRequest(item) with { CoursePointStart = start ?? PublicStatisticsTestHost.Start, CoursePointEnd = end ?? PublicStatisticsTestHost.End });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<StatisticsConfigurationDTO>())!;
        }

        public async Task<List<StatisticsConfigurationVersionDTO>> Versions(Guid config) =>
            (await Client.GetFromJsonAsync<List<StatisticsConfigurationVersionDTO>>($"{ConfigUrl(config)}/versions"))!;

        public async Task<List<RecomputeRequest>> Requests()
        {
            await using var scope = Host.App.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<StatisticsDbContext>().Set<RecomputeRequest>().AsNoTracking().ToListAsync();
        }

        public Task ClearRequests() => Host.MutateAsync(db => db.Set<RecomputeRequest>().ExecuteDeleteAsync());

        public async ValueTask DisposeAsync()
        {
            Client?.Dispose();
            if (Host is not null) await Host.DisposeAsync();
        }
    }
}
