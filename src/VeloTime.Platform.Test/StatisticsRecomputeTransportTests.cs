using System.Net;
using System.Net.Http.Json;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.WebUI.Mud.Client.Services;
using Api = VeloTime.Module.Statistics.Client;

namespace VeloTime.Platform.Test;

public sealed partial class WebUiBffTests
{
    [Fact]
    public async Task RecomputeBrowserBffRoundtripsOverviewAndJobs()
    {
        var job = StatisticsRecomputeTransportTests.Job();
        var overview = new RecomputeOverviewDTO(new RecomputeBacklogDTO(4, 1, 1, job.CreatedAtUtc, "boom",
            [new("Passing", 3), new("Manual", 2)], 7, 2, job.CreatedAtUtc), [job]);
        var request = new CreateRecomputeJobRequest(job.FromUtc, job.ToUtc, null, job.InstallationId, "after layout fix");
        var administration = PlatformServiceTests.TestProxy.Create<IStatisticsAdministrationService>((method, args) =>
        {
            if (method.Name == nameof(IStatisticsAdministrationService.GetRecomputeOverviewAsync)) return overview;
            Assert.Equal(nameof(IStatisticsAdministrationService.CreateRecomputeJobAsync), method.Name);
            Assert.Equal(request, args[0]);
            return job;
        });
        await using var app = await CreateAppAsync(administration: administration);
        using var client = CreateClient(app);
        await client.GetAsync("signin?role=statistics-admin");
        var browser = new StatisticsAdministrationHttpService(client);

        var result = await browser.GetRecomputeOverviewAsync();
        Assert.Equal(overview.Backlog with { ByReason = [] }, result.Backlog with { ByReason = [] });
        Assert.Equal(overview.Backlog.ByReason, result.Backlog.ByReason);
        Assert.Equal(overview.Jobs, result.Jobs);
        Assert.Equal(job, await browser.CreateRecomputeJobAsync(request));
    }

    [Theory]
    [InlineData("user")]
    [InlineData("timing-admin")]
    [InlineData("facilities-admin")]
    public async Task RecomputeBffRejectsOtherAdministratorRoles(string role)
    {
        var administration = PlatformServiceTests.TestProxy.Create<IStatisticsAdministrationService>((_, _) => throw new InvalidOperationException("must not call"));
        await using var app = await CreateAppAsync(administration: administration);
        using var client = CreateClient(app);
        await client.GetAsync($"signin?role={role}");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("api/admin/statistics/recompute")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("api/admin/statistics/recompute/jobs",
            new CreateRecomputeJobRequest(null, null, null, null, null))).StatusCode);
        Assert.Empty(((PlatformServiceTests.TestProxy)(object)administration).Calls);
    }

    [Fact]
    public async Task RecomputeBffPassesValidationMessagesToTheBrowser()
    {
        var administration = PlatformServiceTests.TestProxy.Create<IStatisticsAdministrationService>((_, _) =>
            throw new HttpRequestException("ToUtc must be after FromUtc.", null, HttpStatusCode.BadRequest));
        await using var app = await CreateAppAsync(administration: administration);
        using var client = CreateClient(app);
        await client.GetAsync("signin?role=statistics-admin");
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new StatisticsAdministrationHttpService(client)
            .CreateRecomputeJobAsync(new(null, null, null, null, null)));
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal("ToUtc must be after FromUtc.", error.Message);
    }
}

public sealed class StatisticsRecomputeTransportTests
{
    [Fact]
    public async Task ServerServiceMapsGeneratedContractsAndFirstValidationError()
    {
        var job = Job();
        var calls = 0;
        var client = PlatformServiceTests.TestProxy.Create<Api.IStatisticsClient>((method, args) =>
        {
            calls++;
            if (method.Name == nameof(Api.IStatisticsClient.GetStatisticsRecomputeOverviewAsync))
                return new Api.RecomputeOverviewDTO
                {
                    Backlog = new() { TransponderRequests = 2, RangeRequests = 1, ByReason = [new() { Reason = "Manual", Requests = 3 }], UnresolvedPassings = 5 },
                    Jobs = [Generated(job)]
                };
            Assert.Equal(nameof(Api.IStatisticsClient.CreateStatisticsRecomputeJobAsync), method.Name);
            var body = Assert.IsType<Api.CreateRecomputeJobRequest>(args[0]);
            if (body.Note == "invalid")
                throw new Api.ApiException("Bad Request", 400,
                    """{"title":"One or more validation errors occurred.","errors":{"ToUtc":["ToUtc must be after FromUtc."]}}""", null!, null!);
            Assert.Equal((job.FromUtc, job.ToUtc, job.InstallationId), (body.FromUtc, body.ToUtc, body.InstallationId));
            return Generated(job);
        });
        using var cache = new WebUiCacheTestHost(client);
        var service = cache.StatisticsAdministration(client);

        var overview = await service.GetRecomputeOverviewAsync();
        Assert.Equal((2, 1, 5), (overview.Backlog.TransponderRequests, overview.Backlog.RangeRequests, overview.Backlog.UnresolvedPassings));
        Assert.Equal(new RecomputeBacklogReasonDTO("Manual", 3), Assert.Single(overview.Backlog.ByReason));
        Assert.Equal(job, Assert.Single(overview.Jobs));
        Assert.Equal(job, await service.CreateRecomputeJobAsync(new(job.FromUtc, job.ToUtc, null, job.InstallationId, null)));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => service.CreateRecomputeJobAsync(new(null, null, null, null, "invalid")));
        Assert.Equal((HttpStatusCode.BadRequest, "ToUtc must be after FromUtc."), (error.StatusCode, error.Message));

        cache.Authentication.User = WebUiCacheTestHost.Principal(admin: false);
        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetRecomputeOverviewAsync());
        Assert.Equal(3, calls);
    }

    internal static RecomputeJobDTO Job() => new(Guid.NewGuid(),
        new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero), null, Guid.NewGuid(),
        "after layout fix", Guid.NewGuid(), new(2026, 11, 27, 22, 0, 0, TimeSpan.Zero), RecomputeJobStatus.Running,
        12, 5, 1, "HttpRequestException: down", null);

    private static Api.RecomputeJobDTO Generated(RecomputeJobDTO job) => new()
    {
        Id = job.Id, FromUtc = job.FromUtc, ToUtc = job.ToUtc, TransponderId = job.TransponderId, InstallationId = job.InstallationId,
        Note = job.Note, RequestedByUserId = job.RequestedByUserId, CreatedAtUtc = job.CreatedAtUtc, Status = (int)job.Status,
        TotalTransponders = job.TotalTransponders, RemainingTransponders = job.RemainingTransponders,
        FailingTransponders = job.FailingTransponders, LastError = job.LastError, CompletedAtUtc = job.CompletedAtUtc
    };
}
