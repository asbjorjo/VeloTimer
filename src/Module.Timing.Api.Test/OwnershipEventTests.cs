using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Interface.Messages;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Api.Test;

public sealed partial class TimingEndpointTests
{
    [Theory]
    [InlineData(false, 2020)]
    [InlineData(true, 2030)]
    public async Task OwnershipEvents_CreateEditAndNoOp(bool legacy, int year)
    {
        using var client = factory.CreateClient();
        var id = await SeedUnownedTransponder();
        var request = new SaveTransponderOwnershipRequest(id, TestAuthenticationHandler.DefaultOwnerId,
            new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.FromHours(2)), null);
        var response = legacy
            ? await client.PostAsJsonAsync($"/api/timing/transponders/{id}/owners",
                new TransponderOwnership(request.OwnerId, request.OwnedFrom.UtcDateTime, null))
            : await client.PostAsJsonAsync("/api/timing/admin/ownerships", request);
        Assert.Equal(legacy ? HttpStatusCode.OK : HttpStatusCode.NoContent, response.StatusCode);
        var before = Assert.Single(await PersonalOwnerships(client));
        var message = Assert.Single(factory.Messages.Enqueued);
        Assert.NotEqual(Guid.Empty, message.EventId);
        Assert.Equal(id, message.TransponderId);
        Assert.Equal(new TransponderOwnershipChange(null, before), Assert.Single(message.Changes));
        Assert.Equal(TimeSpan.Zero, before.OwnedFrom.Offset);
        var roundTrip = JsonSerializer.Deserialize<TransponderOwnershipChanged>(JsonSerializer.Serialize(message))!;
        Assert.Equal(message.EventId, roundTrip.EventId);
        Assert.Equal(message.Changes, roundTrip.Changes);
        factory.Messages.Reset();
        var changed = request with { OwnerId = Guid.NewGuid(), OwnedTo = request.OwnedFrom.AddDays(3) };
        var url = $"/api/timing/admin/ownerships/{before.Id}";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(url, changed)).StatusCode);
        var after = before with { OwnerId = changed.OwnerId, OwnedTo = changed.OwnedTo!.Value.ToUniversalTime() };
        Assert.Equal(new TransponderOwnershipChange(before, after), Assert.Single(Assert.Single(factory.Messages.Enqueued).Changes));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(url, changed)).StatusCode);
        Assert.Single(factory.Messages.Enqueued);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OwnershipEvents_ClaimEndAndDelete(bool personal, bool end)
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/timing/my/transponders", new ClaimTransponderRequest("HT-24422"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var transponder = (await response.Content.ReadFromJsonAsync<TransponderDTO>())!;
        var before = Assert.Single(transponder.Ownerships);
        Assert.Equal(new TransponderOwnershipChange(null, before), Assert.Single(Assert.Single(factory.Messages.Enqueued).Changes));
        await client.PostAsJsonAsync("/api/timing/my/transponders", new ClaimTransponderRequest("HT-24422"));
        Assert.Single(factory.Messages.Enqueued);
        factory.Messages.Reset();
        var url = $"/api/timing/{(personal ? "my" : "admin")}/ownerships/{before.Id}";
        var mutation = end ? await client.PostAsync($"{url}/end", null) : await client.DeleteAsync(url);
        Assert.Equal(HttpStatusCode.NoContent, mutation.StatusCode);
        var message = Assert.Single(factory.Messages.Enqueued);
        Assert.Equal(transponder.Id, message.TransponderId);
        var change = Assert.Single(message.Changes);
        Assert.Equal(before, change.Before);
        if (end)
        {
            Assert.Equal(Assert.Single(await PersonalOwnerships(client)), change.After);
            Assert.NotNull(change.After!.OwnedTo);
        }
        else Assert.Null(change.After);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnershipEvents_EnqueueFailureRollsBackAndReturnsServerError(bool legacy)
    {
        using var client = factory.CreateClient();
        var id = await SeedUnownedTransponder();
        factory.Messages.FailEnqueue = true;
        var response = legacy
            ? await client.PostAsJsonAsync($"/api/timing/transponders/{id}/owners",
                new TransponderOwnership(TestAuthenticationHandler.DefaultOwnerId, RegistrationStart.UtcDateTime, null))
            : await client.PostAsJsonAsync("/api/timing/admin/ownerships",
                new SaveTransponderOwnershipRequest(id, TestAuthenticationHandler.DefaultOwnerId, RegistrationStart, null));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("could not be queued", await response.Content.ReadAsStringAsync());
        Assert.Empty(await PersonalOwnerships(client));
        Assert.Empty(factory.Messages.Enqueued);
    }

    [Fact]
    public async Task OwnershipEvents_RejectedRequestsEnqueueNothing()
    {
        using var client = factory.CreateClient();
        var request = new RegisterTransponderRequest("FF-87718", RegistrationStart, null);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(PersonalRegistration, request with { OwnedTo = request.OwnedFrom })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/timing/admin/ownerships/{Guid.NewGuid()}")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Role", "user");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(AdminRegistration, request with { OwnerId = Guid.NewGuid() })).StatusCode);
        Assert.Empty(factory.Messages.Enqueued);
    }

    private async Task<Guid> SeedUnownedTransponder()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
        var transponder = new MylapsX2Transponder { Id = Guid.NewGuid(), SystemId = "106187718" };
        storage.Add(transponder);
        await storage.SaveChangesAsync();
        return transponder.Id;
    }
}
