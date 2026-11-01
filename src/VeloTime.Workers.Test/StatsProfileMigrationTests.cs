using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VeloTime.Module.Statistics.Client;
using VeloTime.Users.Migration.Services;
using Timing = VeloTime.Module.Timing.Client;

namespace VeloTime.Workers.Test;

public sealed class StatsProfileMigrationTests
{
    private static readonly Guid OwnerId = Guid.Parse("887106bb-62ea-4c7c-8477-7e571ca057f3");

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    public async Task Ensure_AcceptsBothSuccessStatusesWithoutUpdating(int status)
    {
        using var handler = new ProfileHandler((HttpStatusCode)status, "Legacy", false);
        using var http = new HttpClient(handler) { BaseAddress = new("http://statistics/") };
        var writer = new StatsProfileMigrationWriter(new StatisticsClient(http), NullLogger.Instance);

        await writer.EnsureAsync(42, OwnerId, "Legacy", false, CancellationToken.None);
        writer.LogSummary();

        Assert.Empty(writer.Issues);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(OwnerId, request.GetProperty("userId").GetGuid());
        Assert.Equal("Legacy", request.GetProperty("profileName").GetString());
        Assert.False(request.GetProperty("isPublic").GetBoolean());
    }

    [Theory]
    [InlineData("Current", false)]
    [InlineData("Legacy", true)]
    [InlineData("Current", true)]
    [InlineData("legacy", false)]
    [InlineData("Legacy ", false)]
    public async Task Ensure_ReportsExactNameAndPrivacyMismatches(string savedName, bool savedPublic)
    {
        using var handler = new ProfileHandler(HttpStatusCode.OK, savedName, savedPublic);
        using var http = new HttpClient(handler) { BaseAddress = new("http://statistics/") };
        var writer = new StatsProfileMigrationWriter(new StatisticsClient(http), NullLogger.Instance);

        await writer.EnsureAsync(42, OwnerId, "Legacy", false, CancellationToken.None);

        var issue = Assert.Single(writer.Issues);
        Assert.Contains("Rider 42", issue);
        Assert.Contains(OwnerId.ToString(), issue);
        Assert.Contains($"saved name '{savedName}', public {savedPublic}", issue);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Mismatch_ContinuesOwnershipAndLaterRidersAndReportsBothIssueCategoriesWithoutFailingCompletion()
    {
        using var handler = new ProfileHandler(HttpStatusCode.OK, "Existing", false);
        using var http = new HttpClient(handler) { BaseAddress = new("http://statistics/") };
        var profiles = new StatsProfileMigrationWriter(new StatisticsClient(http), NullLogger.Instance);
        using var timingHandler = new OwnershipHandler();
        using var timingHttp = new HttpClient(timingHandler) { BaseAddress = new("http://timing/") };
        var start = DateTimeOffset.UtcNow;
        var preflight = new OwnershipPreflightResult([
            new(42, "HT-24422", start, null, [1]),
            new(43, "HT-24423", start, null, [2])
        ], [], 2, 2);
        var logger = new MigrationTestLogger();
        var ownerships = new OwnershipMigrationWriter(new Timing.TimingClient(timingHttp), logger, preflight);

        foreach (var rider in new long[] { 42, 43 })
        {
            await profiles.EnsureAsync(rider, OwnerId, "Legacy", true, CancellationToken.None);
            await ownerships.RegisterRiderAsync(rider, OwnerId, CancellationToken.None);
        }
        profiles.LogSummary();
        Assert.Null(Record.Exception(() => ownerships.Complete(profiles.Issues)));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(2, timingHandler.RequestCount);
        Assert.Equal(2, profiles.Issues.Count);
        Assert.Equal(3, logger.ReportedIssues.Count);
        Assert.Equal(2, logger.ReportedIssues.Count(issue => issue.Contains("statistics profile differs")));
        Assert.All(profiles.Issues, issue => Assert.Contains(issue, logger.ReportedIssues));
        Assert.Single(logger.ReportedIssues, issue => issue.Contains("HTTP 409"));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(422)]
    public async Task Ensure_ReportsNonFatalFailureAndContinuesLaterRiders(int status)
    {
        using var handler = new ProfileHandler((HttpStatusCode)status, "Legacy", false);
        using var http = new HttpClient(handler) { BaseAddress = new("http://statistics/") };
        var writer = new StatsProfileMigrationWriter(new StatisticsClient(http), NullLogger.Instance);
        await writer.EnsureAsync(42, OwnerId, "Legacy", false, CancellationToken.None);
        Assert.Contains($"statistics profile could not be ensured: HTTP {status}:", Assert.Single(writer.Issues));
        await writer.EnsureAsync(43, OwnerId, "Legacy", false, CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(2, writer.Issues.Count);
        Assert.Contains("Rider 43", writer.Issues[1]);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Ensure_PropagatesServiceAndAuthenticationErrors(int status)
    {
        using var handler = new ProfileHandler((HttpStatusCode)status, "Legacy", false);
        using var http = new HttpClient(handler) { BaseAddress = new("http://statistics/") };
        var writer = new StatsProfileMigrationWriter(new StatisticsClient(http), NullLogger.Instance);
        var failure = await Assert.ThrowsAsync<ApiException>(() =>
            writer.EnsureAsync(42, OwnerId, "Legacy", false, CancellationToken.None));
        Assert.Equal(status, failure.StatusCode);
        Assert.Empty(writer.Issues);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void MissingIdentity_IsReportedEvenWithoutOwnershipRegistrations()
    {
        using var handler = new ProfileHandler(HttpStatusCode.OK, "Legacy", false);
        using var http = new HttpClient(handler) { BaseAddress = new("http://statistics/") };
        var writer = new StatsProfileMigrationWriter(new StatisticsClient(http), NullLogger.Instance);
        writer.MissingOwner(42);
        Assert.Contains("no identity account", Assert.Single(writer.Issues));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Ensure_PropagatesCancellationWithoutMakingARequest()
    {
        using var handler = new ProfileHandler(HttpStatusCode.OK, "Legacy", false);
        using var http = new HttpClient(handler) { BaseAddress = new("http://statistics/") };
        var writer = new StatsProfileMigrationWriter(new StatisticsClient(http), NullLogger.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            writer.EnsureAsync(42, OwnerId, "Legacy", false, cancellation.Token));
        Assert.Empty(handler.Requests);
        Assert.Empty(writer.Issues);
    }

    [Theory]
    [InlineData(false, "Valid")]
    [InlineData(true, "")]
    public async Task Ensure_ReportsMalformedSuccessAndContinuesLaterRiders(bool correctIdentity, string name)
    {
        using var handler = new ProfileHandler(HttpStatusCode.OK, name, false)
        {
            ResponseOwnerId = correctIdentity ? OwnerId : Guid.NewGuid()
        };
        using var http = new HttpClient(handler) { BaseAddress = new("http://statistics/") };
        var writer = new StatsProfileMigrationWriter(new StatisticsClient(http), NullLogger.Instance);
        await writer.EnsureAsync(42, OwnerId, "Legacy", false, CancellationToken.None);
        Assert.Contains("statistics profile could not be ensured: Statistics ensure returned an invalid profile", Assert.Single(writer.Issues));
        await writer.EnsureAsync(43, OwnerId, "Legacy", false, CancellationToken.None);
        Assert.Equal(2, writer.Issues.Count);
        Assert.Contains("Rider 43", writer.Issues[1]);
        Assert.Equal(2, handler.Requests.Count);
    }

    private sealed class ProfileHandler(HttpStatusCode status, string name, bool isPublic) : HttpMessageHandler
    {
        internal Guid ResponseOwnerId { get; init; } = OwnerId;
        internal List<JsonElement> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/profiles/ensure", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Requests.Add(json.RootElement.Clone());
            return new(status)
            {
                Content = new StringContent(status is HttpStatusCode.OK or HttpStatusCode.Created
                    ? JsonSerializer.Serialize(new { userId = ResponseOwnerId, profileName = name, isPublic })
                    : "\"Profile validation failed\"", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class OwnershipHandler : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/admin/ownerships/register", request.RequestUri!.AbsolutePath);
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(RequestCount == 1 ? HttpStatusCode.Conflict : HttpStatusCode.OK)
            {
                Content = new StringContent(RequestCount == 1 ? "{}" :
                    "{\"requiresConfirmation\":false,\"confirmationToken\":null,\"existingOwnerships\":[]}", Encoding.UTF8, "application/json")
            });
        }
    }
}
