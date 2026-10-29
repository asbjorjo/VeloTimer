using System.Net;
using System.Net.Http.Json;
using VeloTime.WebUI.Mud.Client.ViewModel;
using VeloTime.WebUI.Mud.Services;
using VeloTime.WebUI.Mud.Client.Services;
using Facilities = VeloTime.Module.Facilities.Client;
using Statistics = VeloTime.Module.Statistics.Client;
using Timing = VeloTime.Module.Timing.Client;
using Keycloak = VeloTime.WebUI.Keycloak;
using TimingContracts = VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.Platform.Test;

public sealed class NoContentClientCallTests
{
    internal static ITransponderOwnerService UnusedOwners() =>
        PlatformServiceTests.TestProxy.Create<ITransponderOwnerService>((method, _) => throw new NotSupportedException(method.Name));
    [Theory]
    [InlineData("facilities")]
    [InlineData("timing")]
    [InlineData("statistics")]
    [InlineData("keycloak")]
    public async Task ExecuteNoContent_AcceptsEntireSuccessRange(string client)
    {
        for (var status = 200; status <= 299; status++)
        {
            var exception = ApiException(client, status);
            await AdministrationClientCall.ExecuteNoContent(() => Task.FromException(exception));
        }

        await AdministrationClientCall.ExecuteNoContent(() => Task.CompletedTask);
    }

    [Theory]
    [InlineData("facilities")]
    [InlineData("timing")]
    [InlineData("statistics")]
    [InlineData("keycloak")]
    public async Task ExecuteNoContent_PropagatesNonSuccessUnchanged(string client)
    {
        foreach (var status in new[] { 199, 300, 400, 401, 403, 404, 409, 422, 500, 503 })
        {
            var exception = ApiException(client, status);
            var actual = await Record.ExceptionAsync(() =>
                AdministrationClientCall.ExecuteNoContent(() => Task.FromException(exception)));
            Assert.Same(exception, actual);
        }
    }

    [Fact]
    public async Task ExecuteNoContent_PropagatesCancellationAndUnrelatedFailures()
    {
        foreach (var exception in new Exception[]
        {
            new OperationCanceledException(),
            new HttpRequestException("Transport failure"),
            new HttpRequestException("Not a generated-client status mismatch", null, HttpStatusCode.OK),
            new InvalidOperationException("Mapping failure")
        })
        {
            var actual = await Record.ExceptionAsync(() =>
                AdministrationClientCall.ExecuteNoContent(() => Task.FromException(exception)));
            Assert.Same(exception, actual);
        }
    }

    [Theory]
    [InlineData(200)]
    [InlineData(202)]
    [InlineData(204)]
    [InlineData(299)]
    public async Task NoBodyMutations_AcceptSuccessAcrossAllAdapters(int status)
    {
        using var handler = new StatusResponseHandler(status);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://upstream/") };
        var timing = new Timing.TimingClient(http);
        var facilities = new Facilities.FacilitiesClient(http);
        var statistics = new Statistics.StatisticsClient(http);
        var transponders = new TransponderManagementService(timing, UnusedOwners());
        var infrastructure = new InfrastructureAdministrationService(facilities, timing);
        var definitions = new StatisticsAdministrationService(statistics);
        var id = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var request = new TimingContracts.SaveTransponderOwnershipRequest(id, id, DateTimeOffset.UtcNow, null);

        await transponders.SaveOwnershipAsync(null, request, token);
        await transponders.SaveOwnershipAsync(id, request, token);
        await transponders.EndOwnershipAsync(id, false, token);
        await transponders.EndOwnershipAsync(id, true, token);
        await transponders.DeleteOwnershipAsync(id, false, token);
        await transponders.DeleteOwnershipAsync(id, true, token);
        await transponders.DeleteTransponderAsync(id, token);
        await infrastructure.DeleteFacilityAsync(id, token);
        await infrastructure.DeleteLayoutAsync(id, id, token);
        await infrastructure.DeleteInstallationAsync(id, token);
        await infrastructure.DeleteTimingPointAsync(id, id, token);
        await definitions.DeleteItemAsync(id, token);
        await definitions.DeleteConfigurationAsync(id, token);
        await new TimingService(timing, facilities).UpdateInstallationAsync(new InstallationView
        {
            Id = id, TimingSystem = "X2", TimingPoints = []
        }, token);
        await new ProfileService(new Keycloak.KeycloakClient(http)).DeleteLinkedAccountAsync("provider", token);

        Assert.Equal(15, handler.Calls);
        Assert.True(handler.AllRequestsCancelable);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(422)]
    public async Task NoBodyMutations_PreserveDomainErrorDetails(int status)
    {
        using var http = new HttpClient(new StatusResponseHandler(status, """{"detail":"Keep this failure detail."}"""))
        {
            BaseAddress = new Uri("http://upstream/")
        };
        var timing = new Timing.TimingClient(http);
        var id = Guid.NewGuid();
        Func<Task>[] actions =
        [
            () => new TransponderManagementService(timing, UnusedOwners()).SaveOwnershipAsync(id, new(id, id, DateTimeOffset.UtcNow, null)),
            () => new InfrastructureAdministrationService(new Facilities.FacilitiesClient(http), timing).DeleteFacilityAsync(id),
            () => new StatisticsAdministrationService(new Statistics.StatisticsClient(http)).DeleteItemAsync(id)
        ];
        foreach (var action in actions)
        {
            var exception = await Assert.ThrowsAsync<HttpRequestException>(action);
            Assert.Equal((HttpStatusCode)status, exception.StatusCode);
            Assert.Contains("Keep this failure detail.", exception.Message);
        }
    }

    [Theory]
    [InlineData(200, "")]
    [InlineData(200, "not-json")]
    [InlineData(202, "")]
    [InlineData(204, "")]
    public async Task RequiredBodyMutations_StillRejectMissingOrInvalidDtos(int status, string body)
    {
        using var http = new HttpClient(new StatusResponseHandler(status, body))
        {
            BaseAddress = new Uri("http://upstream/")
        };
        var timing = new Timing.TimingClient(http);
        var transponders = new TransponderManagementService(timing, UnusedOwners());
        var infrastructure = new InfrastructureAdministrationService(new Facilities.FacilitiesClient(http), timing);
        var definitions = new StatisticsAdministrationService(new Statistics.StatisticsClient(http));
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<Timing.ApiException>(() => transponders.SaveTransponderAsync(id, "Label"));
        await Assert.ThrowsAsync<Timing.ApiException>(() => transponders.RegisterAsync("FF-87718", false));
        await Assert.ThrowsAsync<Timing.ApiException>(() => transponders.RegisterOwnershipAsync(new("FF-87718", DateTimeOffset.UtcNow, null), false));
        await Assert.ThrowsAsync<Facilities.ApiException>(() => infrastructure.SetTimingLinkAsync(id, id));
        await Assert.ThrowsAsync<Facilities.ApiException>(() => infrastructure.SaveFacilityAsync(null, new("Track")));
        await Assert.ThrowsAsync<Statistics.ApiException>(() => definitions.SaveItemAsync(null, new("Lap", "Description", 250)));
    }

    private static Exception ApiException(string client, int status) => client switch
    {
        "facilities" => new Facilities.ApiException("Unexpected status", status, "", new Dictionary<string, IEnumerable<string>>(), null!),
        "timing" => new Timing.ApiException("Unexpected status", status, "", new Dictionary<string, IEnumerable<string>>(), null!),
        "statistics" => new Statistics.ApiException("Unexpected status", status, "", new Dictionary<string, IEnumerable<string>>(), null!),
        "keycloak" => new Keycloak.ApiException("Unexpected status", status, "", new Dictionary<string, IEnumerable<string>>(), null!),
        _ => throw new ArgumentOutOfRangeException(nameof(client))
    };

    internal sealed class StatusResponseHandler(int status, string body = "") : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        internal bool AllRequestsCancelable { get; private set; } = true;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            AllRequestsCancelable &= cancellationToken.CanBeCanceled;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}

public sealed partial class WebUiBffTests
{
    [Theory]
    [InlineData(200, HttpStatusCode.NoContent)]
    [InlineData(201, HttpStatusCode.NoContent)]
    [InlineData(202, HttpStatusCode.NoContent)]
    [InlineData(204, HttpStatusCode.NoContent)]
    [InlineData(299, HttpStatusCode.NoContent)]
    [InlineData(409, HttpStatusCode.Conflict)]
    [InlineData(500, HttpStatusCode.BadGateway)]
    public async Task OwnershipUpdate_RealGeneratedClientPreservesBffResponse(int upstreamStatus, HttpStatusCode expected)
    {
        using var upstream = new HttpClient(new NoContentClientCallTests.StatusResponseHandler(upstreamStatus))
        {
            BaseAddress = new Uri("http://timing/")
        };
        await using var app = await CreateAppAsync(transponders: new TransponderManagementService(new Timing.TimingClient(upstream), NoContentClientCallTests.UnusedOwners()));
        using var client = CreateClient(app);
        await client.GetAsync("signin?role=timing-admin");

        using var response = await client.PutAsJsonAsync($"api/admin/timing/ownerships/{ResourceId}",
            new TimingContracts.SaveTransponderOwnershipRequest(ResourceId, ResourceId, DateTimeOffset.UtcNow, null));

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.NoContent)
            Assert.Empty(await response.Content.ReadAsStringAsync());
    }
}
