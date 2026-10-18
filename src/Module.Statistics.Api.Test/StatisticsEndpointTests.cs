using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Statistics.Api.Endpoints;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class StatisticsEndpointTests(StatisticsApiFactory factory) : IClassFixture<StatisticsApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SampleEndpoint_ReturnsEmptyCursorPage()
    {
        using var client = factory.CreateClient();
        var cursor = Uri.EscapeDataString(DateTime.UtcNow.AddHours(1).ToString("O"));

        var response = await client.GetAsync($"/api/statistics/sample?Cursor={cursor}&IsNextPage=true&PageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<GetSamplesResponse>();
        Assert.NotNull(page);
        Assert.Empty(page.Samples);
        Assert.Null(page.Next);
        Assert.Null(page.Previous);
    }

    [Fact]
    public async Task SampleEndpoint_ConvertsSpeedToKilometersPerHour()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
            storage.Add(new Sample
            {
                Id = Guid.NewGuid(),
                TimeStart = DateTime.UtcNow.AddSeconds(-10),
                TimeEnd = DateTime.UtcNow,
                TransponderId = Guid.NewGuid(),
                CoursePointStartId = Guid.NewGuid(),
                CoursePointEndId = Guid.NewGuid(),
                Duration = TimeSpan.FromSeconds(10),
                Distance = 50,
                Speed = 5,
                FacilityId = Guid.NewGuid()
            });
            await storage.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/statistics/sample");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<GetSamplesResponse>();
        Assert.NotNull(page);
        Assert.Equal(18, Assert.Single(page.Samples).Speed);
    }

    [Fact]
    public async Task ProfileEndpoints_EnforceReadAndWritePolicies()
    {
        var userId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
            storage.Add(new StatsProfile { UserId = userId, ProfileName = "Before" });
            await storage.SaveChangesAsync();
        }

        using var readClient = factory.CreateClient();
        readClient.DefaultRequestHeaders.Add("X-Test-Scope", "Statistics.Read");
        Assert.Equal(HttpStatusCode.OK, (await readClient.GetAsync("/api/statistics/profiles")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await readClient.GetAsync($"/api/statistics/profiles/{userId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await readClient.PutAsJsonAsync($"/api/statistics/profiles/{userId}", new UpdateStatsProfileRequest("Updated", true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await readClient.DeleteAsync($"/api/statistics/profiles/{userId}")).StatusCode);

        using var anonymousClient = factory.CreateClient();
        anonymousClient.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.GetAsync("/api/statistics/profiles")).StatusCode);

        using var writeClient = factory.CreateClient();
        writeClient.DefaultRequestHeaders.Add("X-Test-Scope", "Statistics.Write");
        var createResponse = await writeClient.PostAsJsonAsync("/api/statistics/profiles", new CreateStatsProfileRequest(Guid.NewGuid(), "Created"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var updateResponse = await writeClient.PutAsJsonAsync($"/api/statistics/profiles/{userId}", new UpdateStatsProfileRequest("Updated", false));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await writeClient.DeleteAsync($"/api/statistics/profiles/{userId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await writeClient.GetAsync($"/api/statistics/profiles/{userId}")).StatusCode);
    }
}
