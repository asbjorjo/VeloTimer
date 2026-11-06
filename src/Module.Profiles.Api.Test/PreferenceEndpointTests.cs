using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Profiles.Interface.Data;

namespace VeloTime.Module.Profiles.Api.Test;

public sealed class PreferenceEndpointTests
{
    // Deliberately literal: changing a production route/error constant must not silently change the contract test.
    private const string Read = "/api/profiles/me/preferences";
    private const string Write = Read + "/language";

    [Fact]
    public async Task MissingPreference_IsExplicitJsonNull_AndOnlyCallsRead()
    {
        using var factory = new ProfilesApiFactory();
        using var client = factory.CreateContractClient();
        using var response = await client.GetAsync(Read);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["language"], body.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("language").ValueKind);
        Assert.False(Assert.Single(factory.Preferences.Calls).Write);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task MissingAuthentication_Is401_AndNeverCallsService(string method)
    {
        using var factory = new ProfilesApiFactory { Principal = null };
        using var client = factory.CreateContractClient();
        using var response = await Send(client, method);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.Preferences.Calls);
    }

    public static TheoryData<string?, string?> InvalidIdentities => new()
    {
        { null, "subject" }, { "", "subject" }, { " \t", "subject" },
        { "https://issuer.example", null }, { "https://issuer.example", "" },
        { "https://issuer.example", " \t" }, { null, null }
    };

    [Theory]
    [MemberData(nameof(InvalidIdentities))]
    public async Task MissingOrBlankValidatedClaims_Are403_ForBothOperations(string? issuer, string? subject)
    {
        using var factory = new ProfilesApiFactory { Principal = ProfilesApiFactory.Identity(issuer, subject) };
        using var client = factory.CreateContractClient();
        foreach (var method in new[] { "GET", "PUT" })
        {
            using var response = await Send(client, method);
            await AssertProblem(response, HttpStatusCode.Forbidden, "profiles.invalid_identity");
        }
        Assert.Empty(factory.Preferences.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"language\":null}")]
    [InlineData("{\"language\":\"\"}")]
    [InlineData("{\"language\":\"EN\"}")]
    [InlineData("{\"language\":\"En\"}")]
    [InlineData("{\"language\":\"nb-no\"}")]
    [InlineData("{\"language\":\"NB-NO\"}")]
    [InlineData("{\"language\":\" en\"}")]
    [InlineData("{\"language\":\"en \"}")]
    [InlineData("{\"language\":\"\\t\"}")]
    [InlineData("{\"language\":\"nb\"}")]
    [InlineData("{\"language\":\"no\"}")]
    [InlineData("{\"language\":\"nn-NO\"}")]
    [InlineData("{\"language\":\"en-US\"}")]
    [InlineData("{\"language\":\"fr\"}")]
    [InlineData("")]
    public async Task NoncanonicalOrMissingLanguage_IsStable400_WithoutServiceCall(string json)
    {
        using var factory = new ProfilesApiFactory();
        using var client = factory.CreateContractClient();
        using var response = await client.PutAsync(Write, Json(json));
        await AssertProblem(response, HttpStatusCode.BadRequest, "profiles.invalid_language");
        Assert.Empty(factory.Preferences.Calls);
    }

    [Fact]
    public async Task WritesAndReads_AreSelfOnly_AndSupportBothTransitions()
    {
        using var factory = new ProfilesApiFactory
        {
            Principal = ProfilesApiFactory.Identity(" https://Issuer.example/ ", " Subject ")
        };
        using var client = factory.CreateContractClient();
        foreach (var language in new[] { "en", "nb-NO", "en" })
        {
            using var response = await client.PutAsync(
                Write + "?issuer=attacker&subject=victim&userId=victim",
                Json($$"""{"language":"{{language}}","issuer":"attacker","subject":"victim","userId":"victim"}"""));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await AssertLanguage(response, language);
            using var read = await client.GetAsync(Read + "?issuer=attacker&subject=victim");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            await AssertLanguage(read, language);
        }
        Assert.All(factory.Preferences.Calls, call =>
        {
            Assert.Equal(" https://Issuer.example/ ", call.Issuer);
            Assert.Equal(" Subject ", call.Subject);
        });
        using var otherIdentityRoute = await client.PutAsync("/api/profiles/victim/preferences/language", Json("""{"language":"en"}"""));
        Assert.Equal(HttpStatusCode.NotFound, otherIdentityRoute.StatusCode);
        Assert.Equal(6, factory.Preferences.Calls.Count);
    }

    [Fact]
    public async Task Put_DoesNotReturnSuccessBeforePersistenceCompletes_AndReturnsServiceResult()
    {
        using var factory = new ProfilesApiFactory();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<LanguagePreferenceDTO>(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Preferences.OnCall = _ => { entered.SetResult(); return completion.Task; };
        using var client = factory.CreateContractClient();
        var pending = client.PutAsync(Write, Json("""{"language":"en"}"""));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(pending.IsCompleted);
        }
        finally
        {
            completion.TrySetResult(new LanguagePreferenceDTO("en"));
        }
        using var response = await pending;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertLanguage(response, "en");
        Assert.True(Assert.Single(factory.Preferences.Calls).Write);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task DependencyException_IsNotConvertedToUnsetOrSuccess(string method)
    {
        using var factory = new ProfilesApiFactory();
        var failure = new InvalidOperationException("sensitive database detail: must not be a successful response");
        factory.Preferences.OnCall = _ => Task.FromException<LanguagePreferenceDTO>(failure);
        using var client = factory.CreateContractClient();
        // TestServer propagates unhandled exceptions rather than synthesizing a production-server 500.
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => Send(client, method));
        Assert.Same(failure, actual);
        Assert.Single(factory.Preferences.Calls);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task ProductionHttpFailure_Is500_AndNeverExposesSensitiveDependencyDetails(string method)
    {
        using var factory = new ProfilesApiFactory();
        factory.Preferences.OnCall = _ => Task.FromException<LanguagePreferenceDTO>(
            new InvalidOperationException("sensitive-contract-canary connection-string credentials"));
        // Unlike TestServer, real Kestrel produces the externally observable 500 body.
        factory.UseKestrel(0);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var addresses = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
        client.BaseAddress = new Uri(Assert.Single(addresses.Addresses));
        using var response = await Send(client, method);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sensitive-contract-canary", body, StringComparison.Ordinal);
        Assert.DoesNotContain("credentials", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"language\"", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task RequestAbort_ReachesService_AndRemainsCancellation(string method)
    {
        using var factory = new ProfilesApiFactory();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Preferences.OnCall = async ct =>
        {
            entered.TrySetResult(ct);
            await Task.Delay(Timeout.Infinite, ct);
            return new LanguagePreferenceDTO("en");
        };
        using var client = factory.CreateContractClient();
        using var cancellation = new CancellationTokenSource();
        var pending = Send(client, method, cancellation.Token);
        var serviceToken = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(serviceToken.IsCancellationRequested);
    }

    [Fact]
    public void OperationNames_AreStable()
    {
        using var factory = new ProfilesApiFactory();
        using var client = factory.CreateContractClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        Assert.Contains(endpoints, e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "GetCurrentLanguagePreference");
        Assert.Contains(endpoints, e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "UpdateLanguagePreference");
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string method, CancellationToken ct = default) =>
        method == "GET" ? client.GetAsync(Read, ct) : client.PutAsync(Write, Json("""{"language":"en"}"""), ct);

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task AssertLanguage(HttpResponseMessage response, string language)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["language"], body.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(language, body.RootElement.GetProperty("language").GetString());
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)status, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("title").GetString()));
        Assert.DoesNotContain("stackTrace", body.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("subject", body.RootElement.GetProperty("title").GetString()!, StringComparison.OrdinalIgnoreCase);
    }
}
