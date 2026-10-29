using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Timing.Storage;
using VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.Module.Timing.Api.Test;

public sealed partial class TimingEndpointTests(TimingApiFactory factory) : IClassFixture<TimingApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("CreateTransponderOwnership")]
    [InlineData("UpdateTransponderOwnership")]
    [InlineData("EndTransponderOwnership")]
    [InlineData("DeleteTransponderOwnership")]
    [InlineData("EndMyTransponderOwnership")]
    [InlineData("DeleteMyTransponderOwnership")]
    [InlineData("DeleteManagedInstallation")]
    [InlineData("DeleteTimingPoint")]
    public async Task NoContentMutations_Declare204InOpenApi(string operationId)
    {
        using var client = factory.CreateClient();
        using var document = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var operation = Assert.Single(document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Where(method => method.Value.TryGetProperty("operationId", out var id) && id.GetString() == operationId));
        var responses = operation.Value.GetProperty("responses");
        Assert.True(responses.TryGetProperty("204", out _));
        Assert.False(responses.TryGetProperty("200", out _));
    }

    [Fact]
    public async Task TransponderAdministration_UpdatesLabelAndProtectsDependencies()
    {
        using var client = factory.CreateClient();
        var created = await client.PostAsync("/api/timing/transponders?Type=X2&SystemId=106187718", null);
        var id = await created.Content.ReadFromJsonAsync<Guid>();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/timing/transponders/{id}", new SaveTransponderRequest("Training"))).StatusCode);
        Assert.Equal("Training", (await client.GetFromJsonAsync<TransponderDTO>($"/api/timing/transponders/{id}"))!.Label);
        factory.References.Dependencies = 1;
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/timing/transponders/{id}")).StatusCode);
        factory.References.Dependencies = 0;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/timing/transponders/{id}")).StatusCode);
    }

    [Fact]
    public async Task ManagedInstallationsAndPoints_SupportCrudAndBlockDependencies()
    {
        using var client = factory.CreateClient();
        var request = new SaveInstallationRequest(Guid.NewGuid(), "new-agent", "MyLaps_X2", "Finish installation");
        var created = await client.PostAsJsonAsync("/api/timing/admin/installations", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var installation = (await created.Content.ReadFromJsonAsync<InstallationDTO>())!;
        var root = $"/api/timing/admin/installations/{installation.Id}";
        var pointResponse = await client.PostAsJsonAsync($"{root}/points", new SaveTimingPointRequest("loop-1", "Finish"));
        Assert.Equal(HttpStatusCode.OK, pointResponse.StatusCode);
        var point = (await pointResponse.Content.ReadFromJsonAsync<TimingPointDTO>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"{root}/points", new SaveTimingPointRequest("loop-1", "Duplicate"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"{root}/points/{point.Id}", new SaveTimingPointRequest("loop-1", "Updated"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(root)).StatusCode);
        factory.References.Dependencies = 1;
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"{root}/points/{point.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(root, request with { FacilityId = Guid.NewGuid() })).StatusCode);
        factory.References.Dependencies = 0;
        factory.References.Unavailable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.DeleteAsync($"{root}/points/{point.Id}")).StatusCode);
        factory.References.Unavailable = false;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{root}/points/{point.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(root, request with { Description = "Updated" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(root)).StatusCode);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("facilities-admin")]
    [InlineData("statistics-admin")]
    public async Task InstallationMutations_RequireTimingRole(string role)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        var installation = new InstallationDTO { AgentId = "denied", Description = "Denied", TimingSystem = new TimingSystemDTO { Name = "Unknown" } };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/timing/installations", installation)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/timing/installations/{installation.Id}", installation)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/timing/installations/{installation.Id}")).StatusCode);
    }

    [Fact]
    public async Task OwnershipEndpoints_CompetingClaimsHaveOneWinner()
    {
        using var first = factory.CreateClient();
        using var second = factory.CreateClient();
        first.DefaultRequestHeaders.Add("X-Test-Role", "user");
        second.DefaultRequestHeaders.Add("X-Test-Role", "user");
        second.DefaultRequestHeaders.Add("X-Test-Subject", Guid.NewGuid().ToString());
        HttpResponseMessage[] responses;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            if (scope.ServiceProvider.GetRequiredService<TimingDbContext>().Database.IsNpgsql())
                responses = await Task.WhenAll(first.PostAsJsonAsync("/api/timing/my/transponders", new ClaimTransponderRequest("FF-87718")),
                    second.PostAsJsonAsync("/api/timing/my/transponders", new ClaimTransponderRequest("FF-87718")));
            else responses = [await first.PostAsJsonAsync("/api/timing/my/transponders", new ClaimTransponderRequest("FF-87718")),
                await second.PostAsJsonAsync("/api/timing/my/transponders", new ClaimTransponderRequest("FF-87718"))];
        }
        Assert.Single(responses.Where(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Single(responses.Where(response => response.StatusCode == HttpStatusCode.Conflict));
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task OwnershipEndpoints_IsolateUsersAndSupportEndAndDelete()
    {
        using var owner = factory.CreateClient();
        owner.DefaultRequestHeaders.Add("X-Test-Role", "user");
        var registered = await owner.PostAsJsonAsync("/api/timing/my/transponders", new ClaimTransponderRequest("FF-87718"));
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        var transponder = (await registered.Content.ReadFromJsonAsync<TransponderDTO>())!;
        var ownership = Assert.Single(transponder.Ownerships);
        Assert.Equal(TestAuthenticationHandler.DefaultOwnerId, ownership.OwnerId);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/timing/transponders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/timing/admin/ownerships")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/timing/my/transponders", new ClaimTransponderRequest("FF-87718"))).StatusCode);
        Assert.Single(Assert.Single((await owner.GetFromJsonAsync<List<TransponderDTO>>("/api/timing/my/transponders"))!).Ownerships);

        using var other = factory.CreateClient();
        other.DefaultRequestHeaders.Add("X-Test-Role", "user");
        other.DefaultRequestHeaders.Add("X-Test-Subject", Guid.NewGuid().ToString());
        Assert.Empty((await other.GetFromJsonAsync<List<TransponderDTO>>("/api/timing/my/transponders"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/timing/my/ownerships/{ownership.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/timing/my/ownerships/{ownership.Id}/end", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await other.PostAsJsonAsync("/api/timing/my/transponders", new ClaimTransponderRequest("FF-87718"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/timing/my/ownerships/{ownership.Id}/end", null)).StatusCode);
        Assert.NotNull(Assert.Single(Assert.Single((await owner.GetFromJsonAsync<List<TransponderDTO>>("/api/timing/my/transponders"))!).Ownerships).OwnedTo);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/timing/my/ownerships/{ownership.Id}")).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<List<TransponderDTO>>("/api/timing/my/transponders"))!);
        using var admin = factory.CreateClient();
        Assert.Equal(transponder.Id, Assert.Single((await admin.GetFromJsonAsync<List<TransponderDTO>>("/api/timing/transponders"))!).Id);
    }

    [Theory]
    [InlineData("user", HttpStatusCode.Forbidden)]
    [InlineData("facilities-admin", HttpStatusCode.Forbidden)]
    [InlineData("statistics-admin", HttpStatusCode.Forbidden)]
    [InlineData("timing-admin", HttpStatusCode.OK)]
    [InlineData("velotime-admin", HttpStatusCode.OK)]
    public async Task OwnershipAdministration_RequiresTimingRole(string role, HttpStatusCode status)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        Assert.Equal(status, (await client.GetAsync("/api/timing/admin/ownerships")).StatusCode);
    }

    [Fact]
    public async Task PersonalOwnership_RejectsAnonymousInvalidIdentityAndInvalidCodes()
    {
        using var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/timing/my/transponders")).StatusCode);
        using var invalidSubject = factory.CreateClient();
        invalidSubject.DefaultRequestHeaders.Add("X-Test-Subject", "invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await invalidSubject.GetAsync("/api/timing/my/transponders")).StatusCode);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/timing/my/transponders", new ClaimTransponderRequest("invalid"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/timing/my/transponders", new ClaimTransponderRequest("CCC-00000"))).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<TransponderDTO>>("/api/timing/my/transponders"))!);
    }

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
