using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Api.Test;

public sealed partial class TimingEndpointTests
{
    private const string PersonalRegistration = "/api/timing/my/transponders/register";
    private const string AdminRegistration = "/api/timing/admin/ownerships/register";
    private static readonly DateTimeOffset RegistrationStart = new(2026, 3, 28, 23, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-400, false)]
    [InlineData(400, true)]
    public async Task Registration_PersistsSelectedInstantsAndAuthenticatedOwner(int days, bool hasEnd)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "user");
        var start = RegistrationStart.AddDays(days);
        var request = new RegisterTransponderRequest("FF-87718", start, hasEnd ? start.AddHours(23) : null);

        var response = await client.PostAsJsonAsync(PersonalRegistration, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<RegisterTransponderResult>())!.RequiresConfirmation);
        var ownership = Assert.Single((await client.GetFromJsonAsync<TransponderPage>("/api/timing/my/transponders"))!.Items).Ownership!;
        Assert.Equal(start, ownership.OwnedFrom);
        Assert.Equal(request.OwnedTo, ownership.OwnedTo);
        Assert.Equal(TestAuthenticationHandler.DefaultOwnerId, ownership.OwnerId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Registration_RejectsInvalidPeriodWithoutCreatingTransponder(int hours)
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(PersonalRegistration,
            new RegisterTransponderRequest("FF-87718", RegistrationStart, RegistrationStart.AddHours(hours)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<TransponderPage>("/api/timing/transponders"))!.Items);
    }

    [Fact]
    public async Task Registration_RequiresStartAndValidCode()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(PersonalRegistration,
            new { Code = "FF-87718" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(PersonalRegistration,
            new RegisterTransponderRequest("invalid", RegistrationStart, null))).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<TransponderPage>("/api/timing/transponders"))!.Items);
    }

    [Fact]
    public async Task Registration_ExtendsExistingCoverageAndRetriesWithoutDuplicates()
    {
        using var client = factory.CreateClient();
        var original = new RegisterTransponderRequest("FF-87718", RegistrationStart, RegistrationStart.AddDays(2));
        await AssertRegistrationSaved(client, original);
        var before = Assert.Single(await PersonalOwnerships(client));
        var extended = original with { OwnedFrom = RegistrationStart.AddDays(-1), OwnedTo = null };

        await AssertRegistrationSaved(client, extended);
        await AssertRegistrationSaved(client, extended);

        var after = Assert.Single(await PersonalOwnerships(client));
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(extended.OwnedFrom, after.OwnedFrom);
        Assert.Null(after.OwnedTo);
    }

    [Fact]
    public async Task Registration_AddsSeparatePeriodAndPreservesHistory()
    {
        using var client = factory.CreateClient();
        var request = new RegisterTransponderRequest("FF-87718", RegistrationStart, RegistrationStart.AddDays(1));
        await AssertRegistrationSaved(client, request);
        var before = Assert.Single(await PersonalOwnerships(client));

        await AssertRegistrationSaved(client, request with { OwnedFrom = RegistrationStart.AddDays(3), OwnedTo = RegistrationStart.AddDays(4) });

        Assert.Equal(2, (await PersonalOwnerships(client)).Count);
        Assert.Contains(before, await PersonalOwnerships(client));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Registration_ShorteningRequiresConfirmationAndPreservesIdentity(bool hasEnd)
    {
        using var client = factory.CreateClient();
        var original = new RegisterTransponderRequest("FF-87718", RegistrationStart, hasEnd ? RegistrationStart.AddDays(5) : null);
        await AssertRegistrationSaved(client, original);
        var before = Assert.Single(await PersonalOwnerships(client));
        var shorter = original with { OwnedFrom = RegistrationStart.AddDays(1), OwnedTo = RegistrationStart.AddDays(3) };

        var response = await client.PostAsJsonAsync(PersonalRegistration, shorter);
        var proposal = (await response.Content.ReadFromJsonAsync<RegisterTransponderResult>())!;

        Assert.True(proposal.RequiresConfirmation);
        Assert.NotEmpty(proposal.ConfirmationToken!);
        Assert.Equal(before, Assert.Single(proposal.ExistingOwnerships));
        // Cancelling is simply not resubmitting; the first request must not save anything.
        Assert.Equal(before, Assert.Single(await PersonalOwnerships(client)));

        await AssertRegistrationSaved(client, shorter with { ConfirmationToken = proposal.ConfirmationToken });
        await AssertRegistrationSaved(client, shorter with { ConfirmationToken = proposal.ConfirmationToken });

        var after = Assert.Single(await PersonalOwnerships(client));
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(shorter.OwnedFrom, after.OwnedFrom);
        Assert.Equal(shorter.OwnedTo, after.OwnedTo);
    }

    [Fact]
    public async Task Registration_StaleConfirmationRequiresReviewOfCurrentOwnership()
    {
        using var client = factory.CreateClient();
        var original = new RegisterTransponderRequest("FF-87718", RegistrationStart, null);
        await AssertRegistrationSaved(client, original);
        var shorter = original with { OwnedTo = RegistrationStart.AddDays(3) };
        var proposal = (await (await client.PostAsJsonAsync(PersonalRegistration, shorter)).Content.ReadFromJsonAsync<RegisterTransponderResult>())!;
        await AssertRegistrationSaved(client, original with { OwnedFrom = RegistrationStart.AddDays(-1) });
        var before = Assert.Single(await PersonalOwnerships(client));

        var staleResponse = await client.PostAsJsonAsync(PersonalRegistration, shorter with { ConfirmationToken = proposal.ConfirmationToken });
        var refreshed = (await staleResponse.Content.ReadFromJsonAsync<RegisterTransponderResult>())!;

        Assert.True(refreshed.RequiresConfirmation);
        Assert.NotEqual(proposal.ConfirmationToken, refreshed.ConfirmationToken);
        Assert.Equal(before, Assert.Single(refreshed.ExistingOwnerships));
        Assert.Equal(before, Assert.Single(await PersonalOwnerships(client)));
        await AssertRegistrationSaved(client, shorter with { ConfirmationToken = refreshed.ConfirmationToken });
    }

    [Fact]
    public async Task Registration_RejectsAnotherOwnersOverlapWithoutChangingEitherPeriod()
    {
        using var client = factory.CreateClient();
        var otherOwner = Guid.NewGuid();
        var original = new RegisterTransponderRequest("FF-87718", RegistrationStart, RegistrationStart.AddDays(2));
        await AssertRegistrationSaved(client, original);
        var next = original with { OwnedFrom = RegistrationStart.AddDays(2), OwnedTo = null, OwnerId = otherOwner };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(AdminRegistration, next)).StatusCode);
        var before = (await client.GetFromJsonAsync<TransponderPage>("/api/timing/transponders"))!.Items.Select(row => row.Ownership).ToList();

        var conflict = await client.PostAsJsonAsync(PersonalRegistration, original with { OwnedTo = RegistrationStart.AddDays(4) });

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var after = (await client.GetFromJsonAsync<TransponderPage>("/api/timing/transponders"))!.Items.Select(row => row.Ownership).ToList();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Registration_MergesSameOwnerOverlappingPeriodsOnlyAfterConfirmingLostCoverage()
    {
        using var client = factory.CreateClient();
        var request = new RegisterTransponderRequest("FF-87718", RegistrationStart, RegistrationStart.AddDays(2));
        await AssertRegistrationSaved(client, request);
        await AssertRegistrationSaved(client, request with { OwnedFrom = RegistrationStart.AddDays(3), OwnedTo = RegistrationStart.AddDays(5) });
        var before = await PersonalOwnerships(client);
        var bridge = request with { OwnedFrom = RegistrationStart.AddDays(1), OwnedTo = RegistrationStart.AddDays(6) };

        var proposal = (await (await client.PostAsJsonAsync(PersonalRegistration, bridge)).Content.ReadFromJsonAsync<RegisterTransponderResult>())!;
        Assert.True(proposal.RequiresConfirmation);
        Assert.Equal(2, proposal.ExistingOwnerships.Count);
        Assert.Equal(before, await PersonalOwnerships(client));
        await AssertRegistrationSaved(client, bridge with { ConfirmationToken = proposal.ConfirmationToken });
        var after = Assert.Single(await PersonalOwnerships(client));
        Assert.Contains(after.Id, before.Select(owner => owner.Id));
        Assert.Equal(bridge.OwnedFrom, after.OwnedFrom);
        Assert.Equal(bridge.OwnedTo, after.OwnedTo);
    }

    [Theory]
    [InlineData("user", false)]
    [InlineData("facilities-admin", false)]
    [InlineData("statistics-admin", false)]
    [InlineData("timing-admin", true)]
    [InlineData("velotime-admin", true)]
    public async Task Registration_AdministrationRequiresTimingPermissionAndUsesSelectedOwner(string role, bool allowed)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        var owner = Guid.NewGuid();
        var response = await client.PostAsJsonAsync(AdminRegistration,
            new RegisterTransponderRequest("FF-87718", RegistrationStart, null, owner));
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        if (allowed)
        {
            var transponder = Assert.Single((await client.GetFromJsonAsync<TransponderPage>("/api/timing/transponders"))!.Items);
            Assert.Equal(owner, transponder.Ownership!.OwnerId);
        }
    }

    [Fact]
    public async Task Registration_RejectsForgedPersonalOwnerAndMissingAdminOwner()
    {
        using var client = factory.CreateClient();
        var request = new RegisterTransponderRequest("FF-87718", RegistrationStart, null);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(PersonalRegistration, request with { OwnerId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(AdminRegistration, request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(AdminRegistration, request with { OwnerId = Guid.Empty })).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<TransponderPage>("/api/timing/transponders"))!.Items);
    }

    [Fact]
    public async Task Registration_CompetingDatedClaimsHaveOneWinner()
    {
        using var first = factory.CreateClient();
        using var second = factory.CreateClient();
        first.DefaultRequestHeaders.Add("X-Test-Role", "user");
        second.DefaultRequestHeaders.Add("X-Test-Role", "user");
        second.DefaultRequestHeaders.Add("X-Test-Subject", Guid.NewGuid().ToString());
        var request = new RegisterTransponderRequest("FF-87718", RegistrationStart, null);
        HttpResponseMessage[] responses;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            if (scope.ServiceProvider.GetRequiredService<TimingDbContext>().Database.IsNpgsql())
                responses = await Task.WhenAll(first.PostAsJsonAsync(PersonalRegistration, request), second.PostAsJsonAsync(PersonalRegistration, request));
            else
                responses = [await first.PostAsJsonAsync(PersonalRegistration, request), await second.PostAsJsonAsync(PersonalRegistration, request)];
        }
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task Registration_ConfirmationCannotBeReusedForDifferentDates()
    {
        using var client = factory.CreateClient();
        var original = new RegisterTransponderRequest("FF-87718", RegistrationStart, null);
        await AssertRegistrationSaved(client, original);
        var shorter = original with { OwnedTo = RegistrationStart.AddDays(3) };
        var proposal = (await (await client.PostAsJsonAsync(PersonalRegistration, shorter)).Content.ReadFromJsonAsync<RegisterTransponderResult>())!;

        var changed = shorter with { OwnedTo = RegistrationStart.AddDays(2), ConfirmationToken = proposal.ConfirmationToken };
        var result = (await (await client.PostAsJsonAsync(PersonalRegistration, changed)).Content.ReadFromJsonAsync<RegisterTransponderResult>())!;

        Assert.True(result.RequiresConfirmation);
        Assert.NotEqual(proposal.ConfirmationToken, result.ConfirmationToken);
        Assert.Null(Assert.Single(await PersonalOwnerships(client)).OwnedTo);
    }

    [Fact]
    public async Task Registration_ConfirmationRechecksOtherOwnersBeforeSaving()
    {
        using var client = factory.CreateClient();
        var original = new RegisterTransponderRequest("FF-87718", RegistrationStart, null);
        await AssertRegistrationSaved(client, original);
        var shorter = original with { OwnedTo = RegistrationStart.AddDays(3) };
        var proposal = (await (await client.PostAsJsonAsync(PersonalRegistration, shorter)).Content.ReadFromJsonAsync<RegisterTransponderResult>())!;
        var ownership = Assert.Single(await PersonalOwnerships(client));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/timing/admin/ownerships/{ownership.Id}",
            new SaveTransponderOwnershipRequest(ownership.TransponderId, Guid.NewGuid(), original.OwnedFrom, null))).StatusCode);

        var response = await client.PostAsJsonAsync(PersonalRegistration, shorter with { ConfirmationToken = proposal.ConfirmationToken });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var transponder = Assert.Single((await client.GetFromJsonAsync<TransponderPage>("/api/timing/transponders"))!.Items);
        var after = transponder.Ownership!;
        Assert.NotEqual(TestAuthenticationHandler.DefaultOwnerId, after.OwnerId);
        Assert.Null(after.OwnedTo);
    }

    private static async Task AssertRegistrationSaved(HttpClient client, RegisterTransponderRequest request)
    {
        var response = await client.PostAsJsonAsync(PersonalRegistration, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<RegisterTransponderResult>())!.RequiresConfirmation);
    }

    private static async Task<IReadOnlyList<TransponderOwnershipDTO>> PersonalOwnerships(HttpClient client) =>
        (await client.GetFromJsonAsync<TransponderPage>("/api/timing/my/transponders"))!.Items.Select(row => row.Ownership!).ToList();
}
