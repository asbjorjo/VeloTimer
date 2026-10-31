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

    [Theory]
    [InlineData("facilities-admin", HttpStatusCode.OK)]
    [InlineData("timing-admin", HttpStatusCode.OK)]
    [InlineData("statistics-admin", HttpStatusCode.OK)]
    [InlineData("velotime-admin", HttpStatusCode.OK)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    public async Task DependencyReads_RequireAdministratorRole(string role, HttpStatusCode status)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        foreach (var kind in new[] { "facilities", "coursepoints", "transponders" })
            Assert.Equal(status, (await client.GetAsync($"/api/statistics/references/{kind}/{Guid.NewGuid()}/dependencies")).StatusCode);
    }

    [Theory]
    [InlineData("statistics-admin", HttpStatusCode.OK)]
    [InlineData("velotime-admin", HttpStatusCode.OK)]
    [InlineData("timing-admin", HttpStatusCode.Forbidden)]
    [InlineData("facilities-admin", HttpStatusCode.Forbidden)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    public async Task DefinitionEndpoints_EnforceModuleRole(string role, HttpStatusCode status)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        Assert.Equal(status, (await client.GetAsync("/api/statistics/admin/items")).StatusCode);
        Assert.Equal(status, (await client.GetAsync("/api/statistics/admin/configurations")).StatusCode);
    }

    [Fact]
    public async Task DefinitionEndpoints_ManageItemsSimpleAndMultiConfigurations()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "statistics-admin");
        var created = await client.PostAsJsonAsync("/api/statistics/admin/items", new SaveStatisticsItemRequest("Lap", "250m lap", 250));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = (await created.Content.ReadFromJsonAsync<StatisticsItemDTO>())!;
        var pointId = Guid.NewGuid();
        var simpleRequest = new SaveStatisticsConfigurationRequest(item.Id, StatisticsConfigurationType.Simple, pointId, pointId, null, null);
        var simpleResponse = await client.PostAsJsonAsync("/api/statistics/admin/configurations", simpleRequest);
        Assert.Equal(HttpStatusCode.Created, simpleResponse.StatusCode);
        var simple = (await simpleResponse.Content.ReadFromJsonAsync<StatisticsConfigurationDTO>())!;
        var multiRequest = new SaveStatisticsConfigurationRequest(item.Id, StatisticsConfigurationType.Multi, null, null, simple.Id, 2);
        var multiResponse = await client.PostAsJsonAsync("/api/statistics/admin/configurations", multiRequest);
        Assert.Equal(HttpStatusCode.Created, multiResponse.StatusCode);
        var multi = (await multiResponse.Content.ReadFromJsonAsync<StatisticsConfigurationDTO>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/statistics/admin/items/{item.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/statistics/admin/configurations/{simple.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/statistics/admin/configurations/{simple.Id}", multiRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/statistics/admin/configurations", multiRequest with { ParentConfigId = multi.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/statistics/admin/configurations", multiRequest with { Repetitions = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/statistics/admin/items/{item.Id}", new SaveStatisticsItemRequest("Lap updated", "", 250))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/statistics/admin/configurations/{multi.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/statistics/admin/configurations/{simple.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/statistics/admin/items/{item.Id}")).StatusCode);
    }

    [Fact]
    public async Task DefinitionEndpoints_PreserveRecordedResults()
    {
        var itemId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var pointId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
            var item = new StatisticsItem { Id = itemId, Name = "Lap", Distance = 250 };
            var config = new SimpleStatisticsItemConfig { Id = configId, StatisticsItem = item, CoursePointStart = pointId, CoursePointEnd = pointId };
            storage.Add(new StatisticsEntry
            {
                Id = Guid.NewGuid(), StatisticsItem = item, StatisticsItemConfig = config, TransponderId = Guid.NewGuid(),
                FacilityId = Guid.NewGuid(), TimeStart = DateTime.UtcNow.AddMinutes(-1), TimeEnd = DateTime.UtcNow,
                Duration = TimeSpan.FromMinutes(1), Speed = 250d / 60
            });
            await storage.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "statistics-admin");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/statistics/admin/items/{itemId}", new SaveStatisticsItemRequest("Lap", "", 500))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/statistics/admin/items/{itemId}", new SaveStatisticsItemRequest("Renamed", "", 250))).StatusCode);
        var update = new SaveStatisticsConfigurationRequest(itemId, StatisticsConfigurationType.Simple, Guid.NewGuid(), pointId, null, null);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/statistics/admin/configurations/{configId}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/statistics/admin/configurations/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/statistics/admin/configurations/{configId}")).StatusCode);
        await using var verification = factory.Services.CreateAsyncScope();
        Assert.Single(verification.ServiceProvider.GetRequiredService<StatisticsDbContext>().Set<StatisticsEntry>());
    }

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
    public async Task ProfileEndpoints_AllowAuthenticatedReads()
    {
        var userId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
            storage.Add(new StatsProfile { UserId = userId, ProfileName = "Before" });
            await storage.SaveChangesAsync();
        }

        using var readClient = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await readClient.GetAsync("/api/statistics/profiles")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await readClient.GetAsync($"/api/statistics/profiles/{userId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await readClient.PutAsJsonAsync($"/api/statistics/profiles/{userId}", new UpdateStatsProfileRequest("Updated", true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await readClient.DeleteAsync($"/api/statistics/profiles/{userId}")).StatusCode);

        using var anonymousClient = factory.CreateClient();
        anonymousClient.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.GetAsync("/api/statistics/profiles")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.GetAsync($"/api/statistics/profiles/{userId}")).StatusCode);
    }

    [Theory]
    [InlineData("statistics-admin")]
    [InlineData("velotime-admin")]
    [InlineData("timing-admin,statistics-admin")]
    public async Task ProfileEndpoints_AllowStatisticsAdministratorsToManageProfiles(string roles)
    {
        var userId = Guid.NewGuid();
        using var writeClient = factory.CreateClient();
        writeClient.DefaultRequestHeaders.Add("X-Test-Role", roles);
        var request = new CreateStatsProfileRequest(userId, "Created", false);
        var createResponse = await writeClient.PostAsJsonAsync("/api/statistics/profiles", request);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal($"/api/statistics/profiles/{userId}", createResponse.Headers.Location?.OriginalString);
        Assert.Equal(new StatsProfileResponse(userId, "Created", false),
            await writeClient.GetFromJsonAsync<StatsProfileResponse>($"/api/statistics/profiles/{userId}"));
        Assert.Equal(HttpStatusCode.Conflict,
            (await writeClient.PostAsJsonAsync("/api/statistics/profiles", request)).StatusCode);

        var updateResponse = await writeClient.PutAsJsonAsync($"/api/statistics/profiles/{userId}", new UpdateStatsProfileRequest("Updated", true));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(new StatsProfileResponse(userId, "Updated", true),
            await writeClient.GetFromJsonAsync<StatsProfileResponse>($"/api/statistics/profiles/{userId}"));
        Assert.Equal(HttpStatusCode.NoContent, (await writeClient.DeleteAsync($"/api/statistics/profiles/{userId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await writeClient.GetAsync($"/api/statistics/profiles/{userId}")).StatusCode);

        await using var verification = factory.Services.CreateAsyncScope();
        Assert.Empty(verification.ServiceProvider.GetRequiredService<StatisticsDbContext>().Set<StatsProfile>());
    }

    [Theory]
    [InlineData(null, false, false, HttpStatusCode.Forbidden)]
    [InlineData("user", false, false, HttpStatusCode.Forbidden)]
    [InlineData("timing-admin", false, false, HttpStatusCode.Forbidden)]
    [InlineData("facilities-admin", false, false, HttpStatusCode.Forbidden)]
    [InlineData(null, true, false, HttpStatusCode.Forbidden)]
    [InlineData(null, false, true, HttpStatusCode.Unauthorized)]
    public async Task ProfileEndpoints_RejectUnauthorizedMutations(
        string? role, bool writeScope, bool anonymous, HttpStatusCode expectedStatus)
    {
        var userId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
            storage.Add(new StatsProfile { UserId = userId, ProfileName = "Before", IsPublic = false });
            await storage.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        if (role is not null)
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
        if (writeScope)
            client.DefaultRequestHeaders.Add("X-Test-Scope", "Statistics.Write");
        if (anonymous)
            client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");

        Assert.Equal(expectedStatus, (await client.PostAsJsonAsync("/api/statistics/profiles",
            new CreateStatsProfileRequest(Guid.NewGuid(), "Created"))).StatusCode);
        Assert.Equal(expectedStatus, (await client.PutAsJsonAsync($"/api/statistics/profiles/{userId}",
            new UpdateStatsProfileRequest("Updated", true))).StatusCode);
        Assert.Equal(expectedStatus, (await client.DeleteAsync($"/api/statistics/profiles/{userId}")).StatusCode);

        await using var verification = factory.Services.CreateAsyncScope();
        var profile = Assert.Single(verification.ServiceProvider.GetRequiredService<StatisticsDbContext>().Set<StatsProfile>());
        Assert.Equal(userId, profile.UserId);
        Assert.Equal("Before", profile.ProfileName);
        Assert.False(profile.IsPublic);
    }
}
