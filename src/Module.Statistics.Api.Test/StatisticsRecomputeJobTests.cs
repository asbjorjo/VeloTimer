using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Test;

/// <summary>Admin "recompute range" action and progress view (replay-safe plan Phase 5).</summary>
public sealed class StatisticsRecomputeJobTests
{
    private const string Root = "/api/statistics/admin/recompute";
    private static readonly Guid Actor = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset From = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    [InlineData("timing-admin", HttpStatusCode.Forbidden)]
    [InlineData("facilities-admin", HttpStatusCode.Forbidden)]
    public async Task OverviewJobAndCreateRequireStatisticsAdministration(string? role, HttpStatusCode expected)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        using var client = host.App.CreateClient();
        if (role is null) client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        else client.DefaultRequestHeaders.Add("X-Test-Role", role);
        Assert.Equal(expected, (await client.GetAsync(Root)).StatusCode);
        Assert.Equal(expected, (await client.GetAsync($"{Root}/jobs/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(expected, (await client.PostAsJsonAsync($"{Root}/jobs", new CreateRecomputeJobRequest(null, null, null, null, null))).StatusCode);
    }

    public static TheoryData<string, string> InvalidRequests => new()
    {
        { """{"fromUtc":"2026-11-02T00:00:00Z","toUtc":"2026-11-01T00:00:00Z"}""", "ToUtc" },
        { """{"fromUtc":"2026-11-01T00:00:00Z","toUtc":"2026-11-01T00:00:00Z"}""", "ToUtc" },
        { """{"transponderId":"00000000-0000-0000-0000-000000000000"}""", "TransponderId" },
        { """{"installationId":"00000000-0000-0000-0000-000000000000"}""", "InstallationId" },
        { """{"transponderId":"40000000-0000-0000-0000-000000000001","installationId":"40000000-0000-0000-0000-000000000002"}""", "InstallationId" },
        { "{\"note\":\"" + new string('x', 501) + "\"}", "Note" },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task CreateRejectsInvalidRanges(string body, string field)
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        using var client = Admin(host);
        var response = await client.PostAsync($"{Root}/jobs", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(field, out _), await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await Count<RecomputeRequest>(host));
    }

    [Fact]
    public async Task UnknownJobIsNotFound()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync();
        using var client = Admin(host);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Root}/jobs/{Guid.NewGuid()}")).StatusCode);
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task CreateQueuesOneManualRangeRequestAndReportsTheJobAsQueued()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        using var client = Admin(host);
        var installation = Guid.NewGuid();

        var response = await client.PostAsJsonAsync($"{Root}/jobs",
            new CreateRecomputeJobRequest(From, From.AddDays(1), null, installation, "  layout fixed  "));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<RecomputeJobDTO>())!;
        Assert.Equal(new Uri($"{Root}/jobs/{job.Id}", UriKind.Relative), response.Headers.Location);
        Assert.Equal((RecomputeJobStatus.Queued, (int?)null, 0, "layout fixed", Actor, installation, From, From.AddDays(1)),
            (job.Status, job.TotalTransponders, job.RemainingTransponders, job.Note, job.RequestedByUserId, job.InstallationId, job.FromUtc, job.ToUtc));
        await using (var scope = host.App.Services.CreateAsyncScope())
        {
            var request = await scope.ServiceProvider.GetRequiredService<StatisticsDbContext>().Set<RecomputeRequest>().SingleAsync();
            Assert.Equal((null, installation, RecomputeReason.Manual, job.Id, From.UtcDateTime, From.AddDays(1).UtcDateTime),
                (request.TransponderId, request.InstallationId, request.Reason, request.CausationId, request.FromUtc, request.ToUtc));
        }

        var overview = await Overview(client);
        Assert.Equal(job.Id, Assert.Single(overview.Jobs).Id);
        Assert.Equal((0, 1, 0), (overview.Backlog.TransponderRequests, overview.Backlog.RangeRequests, overview.Backlog.FailingRequests));
        Assert.Equal("Manual", Assert.Single(overview.Backlog.ByReason).Reason);
        Assert.Equal(job, await client.GetFromJsonAsync<RecomputeJobDTO>($"{Root}/jobs/{job.Id}"));
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task ProgressCountsRemainingAndFailingTranspondersAndTheBacklog()
    {
        await using var host = await PublicStatisticsTestHost.CreateAsync(postgres: true);
        using var client = Admin(host);
        var job = new RecomputeJob { Id = Guid.NewGuid(), CreatedAtUtc = From.UtcDateTime, TotalTransponders = 3 };
        var done = new RecomputeJob { Id = Guid.NewGuid(), CreatedAtUtc = From.UtcDateTime.AddHours(1), TotalTransponders = 2, CompletedAtUtc = From.UtcDateTime.AddHours(2) };
        await host.SeedAsync(job, done,
            Request(job.Id, RecomputeReason.Manual),
            Request(job.Id, RecomputeReason.Manual, attempts: 2, error: "HttpRequestException: facilities down"),
            Request(Guid.NewGuid(), RecomputeReason.Passing),
            new UnresolvedPassing { PassingId = Guid.NewGuid(), TransponderId = Guid.NewGuid(), InstallationId = Guid.NewGuid(),
                TimingPointId = Guid.NewGuid(), Time = From.UtcDateTime, Reason = UnresolvedPassingReason.NoCoursePoint, DetectedAtUtc = From.UtcDateTime },
            new ManualDecisionReview { Id = Guid.NewGuid(), EntryId = Guid.NewGuid(), CreatedAtUtc = From.UtcDateTime.AddMinutes(5),
                TimeStart = From.UtcDateTime, TimeEnd = From.UtcDateTime });

        var overview = await Overview(client);

        Assert.Equal([done.Id, job.Id], overview.Jobs.Select(j => j.Id));
        var running = overview.Jobs[1];
        Assert.Equal((RecomputeJobStatus.Running, (int?)3, 2, 1, "HttpRequestException: facilities down"),
            (running.Status, running.TotalTransponders, running.RemainingTransponders, running.FailingTransponders, running.LastError));
        Assert.Equal((RecomputeJobStatus.Completed, 0), (overview.Jobs[0].Status, overview.Jobs[0].RemainingTransponders));
        var backlog = overview.Backlog;
        Assert.Equal((3, 0, 1, 1, 1), (backlog.TransponderRequests, backlog.RangeRequests, backlog.FailingRequests,
            backlog.UnresolvedPassings, backlog.ManualDecisionReviews));
        Assert.Equal("HttpRequestException: facilities down", backlog.LastError);
        Assert.Equal(From, backlog.OldestRequestCreatedAtUtc);
        Assert.Equal(From.AddMinutes(5), backlog.OldestManualDecisionReviewAtUtc);
        Assert.Equal([("Passing", 1), ("Manual", 2)], backlog.ByReason.Select(r => (r.Reason, r.Requests)));
    }

    private static RecomputeRequest Request(Guid causation, RecomputeReason reason, int attempts = 0, string? error = null) => new()
    {
        TransponderId = Guid.NewGuid(), Reason = reason, CausationId = causation, CreatedAtUtc = From.UtcDateTime,
        Attempts = attempts, LastError = error, NotBeforeUtc = attempts > 0 ? From.UtcDateTime.AddMinutes(attempts) : null
    };

    private static async Task<RecomputeOverviewDTO> Overview(HttpClient client)
    {
        var response = await client.GetAsync(Root);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<RecomputeOverviewDTO>())!;
    }

    private static async Task<int> Count<T>(PublicStatisticsTestHost host) where T : class
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<StatisticsDbContext>().Set<T>().CountAsync();
    }

    private static HttpClient Admin(PublicStatisticsTestHost host)
    {
        var client = host.App.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "statistics-admin");
        client.DefaultRequestHeaders.Add("X-Test-Subject", Actor.ToString());
        return client;
    }
}
