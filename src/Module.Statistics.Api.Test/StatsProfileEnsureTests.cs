using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Statistics.Api.Endpoints;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class StatsProfileEnsureTests(StatisticsApiFactory factory) : IClassFixture<StatisticsApiFactory>, IAsyncLifetime
{
    private const string Root = "/api/statistics/profiles";
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Ensure_CreatesThenReturnsExistingPreferencesUnchanged()
    {
        using var client = Client();
        var request = new CreateStatsProfileRequest(Guid.NewGuid(), "Saved private name", false);
        var first = await client.PostAsJsonAsync($"{Root}/ensure", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal($"{Root}/{request.UserId}", first.Headers.Location?.OriginalString);
        var expected = new StatsProfileResponse(request.UserId, request.ProfileName, request.IsPublic);
        Assert.Equal(expected, await first.Content.ReadFromJsonAsync<StatsProfileResponse>());

        foreach (var retry in new[] { request, request with { ProfileName = "Legacy public name", IsPublic = true } })
        {
            var response = await client.PostAsJsonAsync($"{Root}/ensure", retry);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(expected, await response.Content.ReadFromJsonAsync<StatsProfileResponse>());
        }
        Assert.Equal(expected, await client.GetFromJsonAsync<StatsProfileResponse>($"{Root}/{request.UserId}"));
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<StatisticsDbContext>().Set<Model.StatsProfile>().CountAsync());
    }

    [Theory]
    [InlineData(true, "Valid")]
    [InlineData(false, "")]
    [InlineData(false, "   ")]
    [InlineData(false, null)]
    public async Task EnsureAndCreate_RejectInvalidInputWithoutMutation(bool emptyId, string? name)
    {
        using var client = Client();
        var request = new { UserId = emptyId ? Guid.Empty : Guid.NewGuid(), ProfileName = name, IsPublic = false };
        foreach (var route in new[] { Root, $"{Root}/ensure" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(route, request)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<StatisticsDbContext>().Set<Model.StatsProfile>().ToListAsync());
    }

    [Theory]
    [InlineData("user", false)]
    [InlineData("timing-admin", false)]
    [InlineData("facilities-admin", false)]
    [InlineData("statistics-admin", true)]
    [InlineData("velotime-admin", true)]
    public async Task Ensure_RequiresStatisticsAdministration(string role, bool allowed)
    {
        using var client = Client(role);
        var response = await client.PostAsJsonAsync($"{Root}/ensure", new CreateStatsProfileRequest(Guid.NewGuid(), "Name", false));
        Assert.Equal(allowed ? HttpStatusCode.Created : HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ensure_RejectsAnonymous()
    {
        using var client = Client();
        client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        var response = await client.PostAsJsonAsync($"{Root}/ensure", new CreateStatsProfileRequest(Guid.NewGuid(), "Name", false));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ensure_OpenApiDocumentsBothSuccessResponsesWithTheSameProfileShape()
    {
        using var client = Client();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var operation = document.RootElement.GetProperty("paths")
            .GetProperty($"{Root}/ensure").GetProperty("post");
        Assert.Equal("EnsureStatsProfile", operation.GetProperty("operationId").GetString());
        var responses = operation.GetProperty("responses");
        var okSchema = responses.GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        var createdSchema = responses.GetProperty("201").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Equal("#/components/schemas/StatsProfileResponse", okSchema.GetProperty("$ref").GetString());
        Assert.Equal(okSchema.GetProperty("$ref").GetString(), createdSchema.GetProperty("$ref").GetString());
    }

    [Fact]
    public async Task Create_StillReturnsConflictWithoutUpdatingPreferences()
    {
        using var client = Client();
        var request = new CreateStatsProfileRequest(Guid.NewGuid(), "Original", false);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(Root, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync(Root, request with { ProfileName = "Replacement", IsPublic = true })).StatusCode);
        Assert.Equal(new StatsProfileResponse(request.UserId, "Original", false),
            await client.GetFromJsonAsync<StatsProfileResponse>($"{Root}/{request.UserId}"));
    }

    private HttpClient Client(string role = "statistics-admin")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }
}
