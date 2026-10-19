using System.Net;
using System.Net.Http.Json;
using VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.Module.Timing.Api.Test;

public sealed class TimingEndpointTests(TimingApiFactory factory) : IClassFixture<TimingApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task InstallationEndpoints_CreateReadUpdateAndDeleteInstallation()
    {
        using var client = factory.CreateClient();
        var installation = new InstallationDTO
        {
            AgentId = "agent-test",
            Description = "Test installation",
            TimingSystem = new TimingSystemDTO { Name = "Unknown" }
        };

        var createResponse = await client.PostAsJsonAsync("/api/timing/installations", installation);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal($"/api/timing/installations/{installation.Id}", createResponse.Headers.Location?.OriginalString);

        var getResponse = await client.GetAsync($"/api/timing/installations/{installation.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        installation.Description = "Updated installation";
        var updateResponse = await client.PutAsJsonAsync($"/api/timing/installations/{installation.Id}", installation);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/timing/installations");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var installations = await listResponse.Content.ReadFromJsonAsync<List<InstallationDTO>>();
        Assert.Contains(installations!, item => item.Id == installation.Id && item.Description == "Updated installation");

        var deleteResponse = await client.DeleteAsync($"/api/timing/installations/{installation.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/timing/installations/{installation.Id}")).StatusCode);
    }

    [Fact]
    public async Task AgentEndpoints_AreMappedAndReturnExpectedResults()
    {
        using var client = factory.CreateClient();

        var listResponse = await client.GetAsync("/api/timing/agents");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Empty(await listResponse.Content.ReadFromJsonAsync<List<InstallationDTO>>() ?? new List<InstallationDTO>());

        var missingResponse = await client.GetAsync($"/api/timing/agents/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task SampleAndTransponderEndpoints_ReturnSuccessfulEmptyLists()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/timing/sample")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/timing/transponders")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/timing/transponders/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task TransponderCommands_ReturnExpectedClientErrors()
    {
        using var client = factory.CreateClient();

        var invalidRegistration = await client.PostAsync("/api/timing/transponders?Type=X2", content: null);
        Assert.Equal((HttpStatusCode)422, invalidRegistration.StatusCode);

        var registration = await client.PostAsync("/api/timing/transponders?Type=X2&SystemId=106187718", content: null);
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        var transponderId = await registration.Content.ReadFromJsonAsync<Guid>();

        var ownedFrom = DateTime.UtcNow.AddDays(-1);
        var ownerId = Guid.NewGuid();
        var ownership = new TransponderOwnership(ownerId, ownedFrom, ownedFrom.AddDays(2));
        var ownerResponse = await client.PostAsJsonAsync($"/api/timing/transponders/{transponderId}/owners", ownership);
        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);

        var subsequentOwnership = new TransponderOwnership(ownerId, ownedFrom.AddDays(3), null);
        var subsequentOwnerResponse = await client.PostAsJsonAsync($"/api/timing/transponders/{transponderId}/owners", subsequentOwnership);
        Assert.Equal(HttpStatusCode.OK, subsequentOwnerResponse.StatusCode);

        var overlappingOwnership = new TransponderOwnership(Guid.NewGuid(), ownedFrom.AddDays(1), null);
        var conflictResponse = await client.PostAsJsonAsync($"/api/timing/transponders/{transponderId}/owners", overlappingOwnership);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);

        var missingOwnerTarget = await client.PostAsJsonAsync($"/api/timing/transponders/{Guid.NewGuid()}/owners", ownership);
        Assert.Equal(HttpStatusCode.NotFound, missingOwnerTarget.StatusCode);
    }
}
