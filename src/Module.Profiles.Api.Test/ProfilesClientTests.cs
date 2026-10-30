using System.Net;
using System.Text;
using System.Text.Json;
using VeloTime.Module.Profiles.Client;

namespace VeloTime.Module.Profiles.Api.Test;

public sealed class ProfilesClientTests
{
    [Theory]
    [InlineData(false, 400)]
    [InlineData(false, 401)]
    [InlineData(false, 403)]
    [InlineData(false, 500)]
    [InlineData(false, 503)]
    [InlineData(true, 400)]
    [InlineData(true, 401)]
    [InlineData(true, 403)]
    [InlineData(true, 500)]
    [InlineData(true, 503)]
    public async Task NonSuccess_PreservesStatus_AndProblemCode(bool write, int status)
    {
        var json = $$"""{"status":{{status}},"title":"Failure","code":"profiles.test_failure"}""";
        using var http = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json")
        }))) { BaseAddress = new Uri("https://profiles.invalid") };
        var client = new ProfilesClient(http);
        var exception = await Assert.ThrowsAnyAsync<ApiException>(() => write
            ? client.UpdateLanguagePreferenceAsync(new UpdateLanguagePreferenceRequest { Language = "en" })
            : client.GetCurrentLanguagePreferenceAsync());
        Assert.Equal(status, exception.StatusCode);
        // Generated clients expose documented Problems as a typed Result and other statuses as raw JSON.
        // The observable requirement is retaining the code, not which representation NSwag chooses.
        if (exception is ApiException<ProblemDetails> problem)
        {
            Assert.Equal(status, problem.Result.Status);
            Assert.Equal("profiles.test_failure", problem.Result.AdditionalProperties["code"].ToString());
        }
        else
        {
            using var body = JsonDocument.Parse(exception.Response);
            Assert.Equal("profiles.test_failure", body.RootElement.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task SuccessfulUnset_RemainsNull()
    {
        using var http = new HttpClient(new StubHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/profiles/me/preferences", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"language":null}""", Encoding.UTF8, "application/json")
            });
        })) { BaseAddress = new Uri("https://profiles.invalid") };
        Assert.Null((await new ProfilesClient(http).GetCurrentLanguagePreferenceAsync()).Language);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransportFailure_IsNotAnUnsetOrSuccessfulWrite(bool write)
    {
        var failure = new HttpRequestException("test transport failure");
        using var http = new HttpClient(new StubHandler((_, _) => throw failure))
        {
            BaseAddress = new Uri("https://profiles.invalid")
        };
        var client = new ProfilesClient(http);
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => write
            ? client.UpdateLanguagePreferenceAsync(new UpdateLanguagePreferenceRequest { Language = "en" })
            : client.GetCurrentLanguagePreferenceAsync());
        Assert.Same(failure, exception);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_ReachesTransport(bool write)
    {
        using var source = new CancellationTokenSource();
        using var http = new HttpClient(new StubHandler(async (_, ct) =>
        {
            source.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        })) { BaseAddress = new Uri("https://profiles.invalid") };
        var client = new ProfilesClient(http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write
            ? client.UpdateLanguagePreferenceAsync(new UpdateLanguagePreferenceRequest { Language = "en" }, source.Token)
            : client.GetCurrentLanguagePreferenceAsync(source.Token));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
