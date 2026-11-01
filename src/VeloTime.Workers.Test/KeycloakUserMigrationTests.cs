using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VeloTime.Users.Migration.Keycloak;
using VeloTime.Users.Migration.Models.Identity;
using VeloTime.Users.Migration.Services;

namespace VeloTime.Workers.Test;

public sealed class KeycloakUserMigrationTests
{
    private const string UsersPath = "/admin/realms/velotime/users";
    private static readonly Guid UserId = Guid.Parse("887106bb-62ea-4c7c-8477-7e571ca057f3");
    private static readonly Guid InitialId = Guid.Parse("62b7a323-8709-42d3-882f-a89e40a5a412");
    private static readonly User Source = new() { UserName = "rider+42@example.org", Email = "rider42@example.org" };
    private static readonly UserLoginInfo[] Logins = [new("GoOgLe", "Key-A", "Google display"), new("FACEBOOK", "Key-B", null)];
    private static string IdentitiesPath(Guid id) => $"{UsersPath}/{id}/federated-identity";
    private static string Users(Guid id) => JsonSerializer.Serialize(new[] { new { id = id.ToString(), username = Source.UserName } });
    private const string MatchingLinks = "[{\"identityProvider\":\"GOOGLE\",\"userId\":\"Key-A\"},{\"identityProvider\":\"facebook\",\"userId\":\"Key-B\"}]";

    [Theory]
    [InlineData("Anne", "Smith", false)]
    [InlineData("  Anne<Marie>  ", "Smith;Jones", true)]
    public async Task NewUser_PostsNormalizedProfileAndFederatedIdentitiesAndReturnsVerifiedId(string first, string last, bool normalized)
    {
        using var fixture = new Fixture();
        fixture.Reply(HttpMethod.Get, UsersPath, "[]");
        fixture.Reply(HttpMethod.Post, UsersPath, "", 201);
        fixture.Reply(HttpMethod.Get, UsersPath, Users(UserId));
        fixture.Reply(HttpMethod.Get, IdentitiesPath(UserId), MatchingLinks);

        Assert.Equal(UserId, await fixture.Writer.EnsureUserAsync(42, Source, Logins, first, last, CancellationToken.None));
        fixture.AssertComplete();
        var post = Assert.Single(fixture.Handler.Requests, request => request.Method == HttpMethod.Post);
        var body = post.Body!.Value;
        Assert.Equal(Source.UserName, body.GetProperty("username").GetString());
        Assert.Equal(Source.Email, body.GetProperty("email").GetString());
        Assert.True(body.GetProperty("enabled").GetBoolean());
        Assert.True(body.GetProperty("emailVerified").GetBoolean());
        Assert.Equal(normalized ? "Anne Marie" : "Anne", body.GetProperty("firstName").GetString());
        Assert.Equal(normalized ? "Smith Jones" : "Smith", body.GetProperty("lastName").GetString());
        var links = body.GetProperty("federatedIdentities").EnumerateArray().ToArray();
        Assert.Equal(2, links.Length);
        AssertLink(links[0], "google", "Key-A", "Google display");
        AssertLink(links[1], "facebook", "Key-B", "Key-B");
        Assert.Empty(fixture.Writer.Issues);
        if (normalized) Assert.Contains("normalized name", Assert.Single(fixture.Writer.Warnings));
        else Assert.Empty(fixture.Writer.Warnings);
        fixture.Writer.LogSummary();
    }

    [Fact]
    public async Task NewUser_MultipleProviderKeys_SelectsFirstAndSilentlyIgnoresExactDuplicate()
    {
        var logger = new CapturingLogger();
        using var fixture = new Fixture(logger);
        fixture.Reply(HttpMethod.Get, UsersPath, "[]");
        fixture.Reply(HttpMethod.Post, UsersPath, "", 201);
        fixture.Reply(HttpMethod.Get, UsersPath, Users(UserId));
        fixture.Reply(HttpMethod.Get, IdentitiesPath(UserId), GoogleAndFacebookLinks);

        Assert.Equal(UserId, await fixture.Ensure([
            new("Google", "a", null), new("google", "b", null),
            new("Google", "a", null), new("Facebook", "x", null)]));
        fixture.AssertComplete();
        var post = Assert.Single(fixture.Handler.Requests, request => request.Method == HttpMethod.Post);
        Assert.Equal(UsersPath, post.Uri.AbsolutePath);
        var links = post.Body!.Value.GetProperty("federatedIdentities").EnumerateArray().ToArray();
        Assert.Equal(2, links.Length);
        AssertLink(links[0], "google", "a", "a");
        AssertLink(links[1], "facebook", "x", "x");
        Assert.Equal(MultipleGoogleLoginsWarning, Assert.Single(fixture.Writer.Warnings));
        Assert.Empty(fixture.Writer.Issues);
        fixture.Writer.LogSummary();
        Assert.Equal(2, logger.SummaryCount("IdentitiesLinkedCount"));
        Assert.Equal(0, logger.SummaryCount("UsersVerifiedCleanCount"));
    }

    [Fact]
    public async Task ExistingUser_MultipleProviderKeys_LinksOnlyFirstKeyWithoutReplacing()
    {
        var logger = new CapturingLogger();
        using var fixture = new Fixture(logger);
        fixture.Existing("[]", GoogleALink);
        fixture.Reply(HttpMethod.Post, IdentitiesPath(UserId) + "/google", "", 204);

        Assert.Equal(UserId, await fixture.Ensure([new("Google", "a", null), new("Google", "b", null)]));
        fixture.AssertComplete();
        var post = Assert.Single(fixture.Handler.Requests, request => request.Method == HttpMethod.Post);
        Assert.Equal(IdentitiesPath(UserId) + "/google", post.Uri.AbsolutePath);
        AssertLink(post.Body!.Value, "google", "a", "a");
        Assert.Equal(MultipleGoogleLoginsWarning, Assert.Single(fixture.Writer.Warnings));
        Assert.Empty(fixture.Writer.Issues);
        fixture.Writer.LogSummary();
        Assert.Equal(1, logger.SummaryCount("IdentitiesLinkedCount"));
        Assert.Equal(0, logger.SummaryCount("UsersVerifiedCleanCount"));
    }

    [Fact]
    public async Task ExistingUser_MultipleProviderKeysWithDifferentLinkedKey_VerifiesOnlyFirstAndNeverReplaces()
    {
        using var fixture = new Fixture();
        var links = JsonSerializer.Serialize(new[] { new { identityProvider = "google", userId = "z" } });
        fixture.Existing(links, links);

        Assert.Equal(UserId, await fixture.Ensure([new("Google", "a", null), new("Google", "b", null)]));
        fixture.AssertComplete();
        Assert.All(fixture.Handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        Assert.Equal(new[]
        {
            MultipleGoogleLoginsWarning,
            "Rider 42: Keycloak federated identity 'google' for source key 'a' is linked to a different key after verification."
        }, fixture.Writer.Warnings);
        Assert.Empty(fixture.Writer.Issues);
    }

    [Fact]
    public async Task NonFatalLinkFailure_DoesNotCountAsLinkedInSummary()
    {
        var logger = new CapturingLogger();
        using var fixture = new Fixture(logger);
        fixture.Existing("[]", "[]");
        fixture.Reply(HttpMethod.Post, IdentitiesPath(UserId) + "/google", "{}", 400);

        Assert.Equal(UserId, await fixture.Ensure([new("Google", "a", null)]));
        fixture.AssertComplete();
        var post = Assert.Single(fixture.Handler.Requests, request => request.Method == HttpMethod.Post);
        Assert.Equal(IdentitiesPath(UserId) + "/google", post.Uri.AbsolutePath);
        AssertLink(post.Body!.Value, "google", "a", "a");
        Assert.Contains("could not link provider 'google' in Keycloak: HTTP 400:", Assert.Single(fixture.Writer.Issues));
        Assert.Equal("Rider 42: Keycloak federated identity 'google' for source key 'a' is missing after verification.",
            Assert.Single(fixture.Writer.Warnings));
        fixture.Writer.LogSummary();
        Assert.Equal(0, logger.SummaryCount("IdentitiesLinkedCount"));
        Assert.Equal(0, logger.SummaryCount("UsersVerifiedCleanCount"));
        Assert.Equal(1, logger.SummaryCount("IssueCount"));
    }

    private const string MultipleGoogleLoginsWarning =
        "Rider 42: source has multiple 'google' logins; Keycloak supports one link per provider, so key 'b' was not linked.";
    private static readonly string GoogleALink = JsonSerializer.Serialize(new[] { new { identityProvider = "google", userId = "a" } });
    private static readonly string GoogleAndFacebookLinks = JsonSerializer.Serialize(new[]
    {
        new { identityProvider = "google", userId = "a" }, new { identityProvider = "facebook", userId = "x" }
    });

    [Fact]
    public async Task ExistingUser_LinksOnlyMissingProviderWithoutUpdatingOrDeletingAndReturnsReReadId()
    {
        using var fixture = new Fixture();
        fixture.Reply(HttpMethod.Get, UsersPath, Users(InitialId));
        fixture.Reply(HttpMethod.Get, IdentitiesPath(InitialId), "[{\"identityProvider\":\"GOOGLE\",\"userId\":\"Key-A\"}]");
        fixture.Reply(HttpMethod.Post, IdentitiesPath(InitialId) + "/facebook", "", 204);
        fixture.Reply(HttpMethod.Get, UsersPath, Users(UserId));
        fixture.Reply(HttpMethod.Get, IdentitiesPath(UserId), MatchingLinks);

        Assert.Equal(UserId, await fixture.Ensure());
        fixture.AssertComplete();
        AssertLink(Assert.Single(fixture.Handler.Requests, request => request.Method == HttpMethod.Post).Body!.Value,
            "facebook", "Key-B", "Key-B");
        Assert.Empty(fixture.Writer.Issues);
        Assert.Empty(fixture.Writer.Warnings);
    }

    [Fact]
    public async Task ExistingProviderWithDifferentOrdinalKey_IsNeverReplacedAndIsWarned()
    {
        using var fixture = new Fixture();
        const string links = "[{\"identityProvider\":\"GOOGLE\",\"userId\":\"key-a\"}]";
        fixture.Existing(links, links);
        Assert.Equal(UserId, await fixture.Ensure([Logins[0]]));
        fixture.AssertComplete();
        Assert.All(fixture.Handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        Assert.Contains("linked to a different key", Assert.Single(fixture.Writer.Warnings));
        Assert.Empty(fixture.Writer.Issues);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VerificationMissingLoginAfterCreateOrLink_WarnsButReturnsId(bool create)
    {
        using var fixture = new Fixture();
        fixture.Reply(HttpMethod.Get, UsersPath, create ? "[]" : Users(UserId));
        if (!create) fixture.Reply(HttpMethod.Get, IdentitiesPath(UserId), "[]");
        fixture.Reply(HttpMethod.Post, create ? UsersPath : IdentitiesPath(UserId) + "/google", "", create ? 201 : 204);
        fixture.Reply(HttpMethod.Get, UsersPath, Users(UserId));
        fixture.Reply(HttpMethod.Get, IdentitiesPath(UserId), "[]");
        Assert.Equal(UserId, await fixture.Ensure([Logins[0]]));
        fixture.AssertComplete();
        Assert.Contains("is missing after verification", Assert.Single(fixture.Writer.Warnings));
        Assert.Empty(fixture.Writer.Issues);
    }

    [Fact]
    public async Task LinkConflict_WarnsAndVerifiesWithoutRecordingIssue()
    {
        using var fixture = new Fixture();
        fixture.Existing("[]", MatchingLinks);
        fixture.Reply(HttpMethod.Post, IdentitiesPath(UserId) + "/google", "{\"error\":\"already linked\"}", 409);
        Assert.Equal(UserId, await fixture.Ensure([Logins[0]]));
        fixture.AssertComplete();
        Assert.Contains("HTTP 409 while linking provider", Assert.Single(fixture.Writer.Warnings));
        Assert.Empty(fixture.Writer.Issues);
    }

    [Theory]
    [InlineData("multiple", "multiple Keycloak users exactly match")]
    [InlineData("missing", "no exact Keycloak user was found after create/link")]
    [InlineData("invalid", "invalid user ID")]
    [InlineData("no-id", "matching Keycloak user has no ID")]
    [InlineData("multiple-after", "multiple Keycloak users exactly match")]
    public async Task UnverifiableUser_IsReportedAndReturnsNull(string scenario, string message)
    {
        using var fixture = new Fixture();
        var multiple = "[{} , {}]";
        fixture.Reply(HttpMethod.Get, UsersPath, scenario switch { "multiple" => multiple, "no-id" => "[{}]", _ => "[]" });
        if (scenario is not ("multiple" or "no-id"))
        {
            fixture.Reply(HttpMethod.Post, UsersPath, "", 201);
            fixture.Reply(HttpMethod.Get, UsersPath, scenario switch { "missing" => "[]", "multiple-after" => multiple, _ => "[{\"id\":\"not-a-guid\"}]" });
        }
        Assert.Null(await fixture.Ensure());
        fixture.AssertComplete();
        Assert.Contains(message, Assert.Single(fixture.Writer.Issues));
        Assert.Empty(fixture.Writer.Warnings);
    }

    [Theory]
    [InlineData(400, false)]
    [InlineData(404, false)]
    [InlineData(409, false)]
    [InlineData(422, false)]
    [InlineData(400, true)]
    [InlineData(404, true)]
    [InlineData(409, true)]
    public async Task NonFatalLookupOrCreateFailure_IsReportedAndNextRiderSucceeds(int status, bool create)
    {
        using var fixture = new Fixture();
        if (create) fixture.Reply(HttpMethod.Get, UsersPath, "[]");
        fixture.Reply(create ? HttpMethod.Post : HttpMethod.Get, UsersPath, "{\"error\":\"rejected\"}", status);
        Assert.Null(await fixture.Ensure());
        var issue = Assert.Single(fixture.Writer.Issues);
        Assert.Contains($"Keycloak user could not be created or verified: HTTP {status}:", issue);
        fixture.Existing(MatchingLinks, MatchingLinks);
        Assert.Equal(UserId, await fixture.Ensure());
        fixture.AssertComplete();
        Assert.Equal(issue, Assert.Single(fixture.Writer.Issues));
        Assert.Empty(fixture.Writer.Warnings);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task FatalHttpFailuresAtEveryStage_PropagateWithoutIssues(int status)
    {
        foreach (var stage in new[] { "lookup", "create", "read-links", "link", "verify-user", "verify-links" })
        {
            using var fixture = new Fixture();
            if (stage != "lookup") fixture.Reply(HttpMethod.Get, UsersPath, stage == "create" ? "[]" : Users(UserId));
            if (stage is "link" or "verify-user" or "verify-links") fixture.Reply(HttpMethod.Get, IdentitiesPath(UserId), stage == "link" ? "[]" : MatchingLinks);
            if (stage == "verify-links") fixture.Reply(HttpMethod.Get, UsersPath, Users(UserId));
            var method = stage is "create" or "link" ? HttpMethod.Post : HttpMethod.Get;
            var path = stage is "read-links" or "verify-links" ? IdentitiesPath(UserId)
                : stage == "link" ? IdentitiesPath(UserId) + "/google" : UsersPath;
            fixture.Reply(method, path, "{\"error\":\"unavailable\"}", status);
            var exception = await Assert.ThrowsAsync<ApiException>(() => fixture.Ensure([Logins[0]]));
            Assert.Equal(status, exception.StatusCode);
            fixture.AssertComplete();
            Assert.Empty(fixture.Writer.Issues);
            Assert.Empty(fixture.Writer.Warnings);
        }
    }

    [Fact]
    public async Task ConnectionFailure_PropagatesWithoutIssues()
    {
        using var fixture = new Fixture();
        var failure = new HttpRequestException("Connection refused");
        fixture.Handler.Route(HttpMethod.Get, UsersPath, () => throw failure);
        Assert.Same(failure, await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Ensure()));
        fixture.AssertComplete();
        Assert.Empty(fixture.Writer.Issues);
        Assert.Empty(fixture.Writer.Warnings);
    }

    [Fact]
    public async Task PreCancelledToken_ThrowsWithoutRequestsOrIssues()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Writer.EnsureUserAsync(42, Source, Logins, "Anne", "Smith", cancellation.Token));
        fixture.AssertComplete();
        Assert.Empty(fixture.Handler.Requests);
        Assert.Empty(fixture.Writer.Issues);
        Assert.Empty(fixture.Writer.Warnings);
    }

    private static void AssertLink(JsonElement body, string provider, string key, string display)
    {
        Assert.Equal(provider, body.GetProperty("identityProvider").GetString());
        Assert.Equal(key, body.GetProperty("userId").GetString());
        Assert.Equal(display, body.GetProperty("userName").GetString());
    }

    private sealed class Fixture : IDisposable
    {
        internal RoutedHandler Handler { get; } = new();
        private readonly HttpClient http;
        internal KeycloakUserMigrationWriter Writer { get; }
        internal Fixture(ILogger? logger = null)
        {
            http = new HttpClient(Handler) { BaseAddress = new("http://keycloak/") };
            Writer = new(new KeycloakClient(http), logger ?? NullLogger.Instance);
        }
        internal Task<Guid?> Ensure(IList<UserLoginInfo>? logins = null) => Writer.EnsureUserAsync(42, Source, logins ?? Logins, "Anne", "Smith", CancellationToken.None);
        internal void Reply(HttpMethod method, string path, string body, int status = 200) => Handler.Route(method, path,
            () => new((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        internal void Existing(string initialLinks, string verifiedLinks)
        {
            Reply(HttpMethod.Get, UsersPath, Users(UserId));
            Reply(HttpMethod.Get, IdentitiesPath(UserId), initialLinks);
            Reply(HttpMethod.Get, UsersPath, Users(UserId));
            Reply(HttpMethod.Get, IdentitiesPath(UserId), verifiedLinks);
        }
        internal void AssertComplete()
        {
            Assert.All(Handler.Routes.Values, queue => Assert.Empty(queue));
            Assert.Equal(Handler.ExpectedRequests, Handler.Requests.Count);
            Assert.All(Handler.Requests, request => Assert.True(request.Method == HttpMethod.Get || request.Method == HttpMethod.Post, "No PUT/DELETE allowed"));
            foreach (var request in Handler.Requests.Where(request => request.Method == HttpMethod.Get && request.Uri.AbsolutePath == UsersPath))
            {
                Assert.Equal("keycloak", request.Uri.Host);
                var query = QueryHelpers.ParseQuery(request.Uri.Query);
                Assert.Equal("true", query["exact"].ToString());
                Assert.Equal(Source.UserName, query["username"].ToString());
            }
        }
        public void Dispose() => http.Dispose();
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly List<KeyValuePair<string, object?>[]> summaries = [];
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Information && state is IEnumerable<KeyValuePair<string, object?>> values)
                summaries.Add(values.ToArray());
        }
        internal int SummaryCount(string name) => Assert.IsType<int>(
            Assert.Single(Assert.Single(summaries), value => value.Key == name).Value);
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, JsonElement? Body);
    private sealed class RoutedHandler : HttpMessageHandler
    {
        internal Dictionary<(HttpMethod, string), Queue<Func<HttpResponseMessage>>> Routes { get; } = [];
        internal List<RecordedRequest> Requests { get; } = [];
        internal int ExpectedRequests { get; private set; }
        internal void Route(HttpMethod method, string path, Func<HttpResponseMessage> response)
        {
            if (!Routes.TryGetValue((method, path), out var queue)) Routes[(method, path)] = queue = new();
            queue.Enqueue(response);
            ExpectedRequests++;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            JsonElement? body = null;
            if (request.Content is not null)
            {
                using var json = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
                body = json.RootElement.Clone();
            }
            Requests.Add(new(request.Method, request.RequestUri!, body));
            Assert.True(Routes.TryGetValue((request.Method, request.RequestUri!.AbsolutePath), out var queue) && queue.Count > 0,
                $"Unexpected request: {request.Method} {request.RequestUri}");
            return queue!.Dequeue()();
        }
    }
}
