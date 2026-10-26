using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Interface.Data;
using TimingContracts = VeloTime.Module.Timing.Interface.Data;
using FacilitiesContracts = VeloTime.Module.Facilities.Interface.Data;
using VeloTime.WebUI.Mud;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;
using VeloTime.WebUI.Mud.Endpoints;

namespace VeloTime.Platform.Test;

public sealed class WebUiBffTests
{
    private static readonly Guid ResourceId = Guid.Parse("fa886b15-035b-43fe-984a-04c8ce566122");

    [Theory]
    [InlineData("velotime-admin", true, true, true)]
    [InlineData("facilities-admin", true, false, true)]
    [InlineData("timing-admin", false, true, true)]
    [InlineData("statistics-admin", false, false, false)]
    [InlineData("user", false, false, false)]
    public async Task InfrastructureAdministration_EnforcesWriteAndScopedLinkRoles(string role, bool facilities, bool timing, bool linking)
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        await client.GetAsync($"signin?role={role}");
        Assert.Equal(facilities ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("api/admin/facilities", new FacilitiesContracts.SaveFacilityRequest("Track"))).StatusCode);
        Assert.Equal(timing ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("api/admin/timing/installations", new TimingContracts.SaveInstallationRequest(ResourceId, "agent", "Unknown", ""))).StatusCode);
        Assert.Equal(linking ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync($"api/admin/coursepoints/{ResourceId}/timing-link", new FacilitiesContracts.SetCoursePointTimingLinkRequest(null))).StatusCode);
    }

    [Fact]
    public async Task InfrastructureHttpService_ForwardsEveryManagementOperation()
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        await client.GetAsync("signin?role=velotime-admin");
        var service = new InfrastructureAdministrationHttpService(client);
        Assert.Empty(await service.GetFacilitiesAsync());
        Assert.Empty(await service.GetLayoutsAsync(ResourceId));
        Assert.Empty(await service.GetInstallationsAsync());
        Assert.Equal(ResourceId, (await service.SaveFacilityAsync(null, new("Track"))).Id);
        Assert.Equal("Renamed", (await service.SaveFacilityAsync(ResourceId, new("Renamed"))).Name);
        var startId = Guid.NewGuid(); var endId = Guid.NewGuid();
        var layout = new FacilitiesContracts.SaveCourseLayoutRequest([new(startId, "Start"), new(endId, "End")], [new(Guid.NewGuid(), startId, endId, 250)]);
        Assert.Equal(ResourceId, (await service.SaveLayoutAsync(ResourceId, null, layout)).Id);
        Assert.Equal(ResourceId, (await service.SaveLayoutAsync(ResourceId, ResourceId, layout)).FacilityId);
        await service.SetTimingLinkAsync(startId, null);
        await service.DeleteLayoutAsync(ResourceId, ResourceId);
        await service.DeleteFacilityAsync(ResourceId);
        var installation = new TimingContracts.SaveInstallationRequest(ResourceId, "agent", "MyLaps_X2", "Finish");
        Assert.Equal("agent", (await service.SaveInstallationAsync(null, installation)).AgentId);
        Assert.Equal("Finish", (await service.SaveInstallationAsync(ResourceId, installation)).Description);
        Assert.Equal("loop", (await service.SaveTimingPointAsync(ResourceId, null, new("loop", "Finish"))).SystemId);
        Assert.Equal(ResourceId, (await service.SaveTimingPointAsync(ResourceId, ResourceId, new("loop", "Finish"))).Id);
        await service.DeleteTimingPointAsync(ResourceId, ResourceId);
        await service.DeleteInstallationAsync(ResourceId);
        var proxy = (PlatformServiceTests.TestProxy)(object)app.Services.GetRequiredService<IInfrastructureAdministrationService>();
        Assert.Equal(16, proxy.Calls.Count);
    }

    [Fact]
    public async Task InfrastructureMutations_RejectMissingAntiforgery()
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app, antiforgery: false);
        await client.GetAsync("signin?role=velotime-admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("api/admin/facilities", new FacilitiesContracts.SaveFacilityRequest("Track"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"api/admin/coursepoints/{ResourceId}/timing-link", new FacilitiesContracts.SetCoursePointTimingLinkRequest(null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"api/admin/timing/installations/{ResourceId}/points", new TimingContracts.SaveTimingPointRequest("loop", "Finish"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"api/admin/facilities/{ResourceId}")).StatusCode);
        Assert.Empty(((PlatformServiceTests.TestProxy)(object)app.Services.GetRequiredService<IInfrastructureAdministrationService>()).Calls);
    }

    [Fact]
    public async Task InfrastructureDependencyFailures_PreserveSafeServiceUnavailableMessage()
    {
        var service = PlatformServiceTests.TestProxy.Create<IInfrastructureAdministrationService>((_, _) =>
            throw new HttpRequestException("Dependencies could not be verified. No changes were saved.", null, HttpStatusCode.ServiceUnavailable));
        await using var app = await CreateAppAsync(infrastructure: service);
        using var client = CreateClient(app);
        await client.GetAsync("signin?role=velotime-admin");
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => new InfrastructureAdministrationHttpService(client).DeleteFacilityAsync(ResourceId));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Contains("No changes were saved", exception.Message);
    }

    [Theory]
    [InlineData("timing-admin", HttpStatusCode.OK)]
    [InlineData("velotime-admin", HttpStatusCode.OK)]
    [InlineData("statistics-admin", HttpStatusCode.Forbidden)]
    [InlineData("facilities-admin", HttpStatusCode.Forbidden)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    public async Task TransponderAdministration_EnforcesTimingRole(string role, HttpStatusCode status)
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        await client.GetAsync($"signin?role={role}");
        Assert.Equal(status, (await client.GetAsync("api/admin/timing/transponders")).StatusCode);
    }

    [Fact]
    public async Task TransponderHttpService_RoutesSelfAndAdminMutationsSeparately()
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        await client.GetAsync("signin?role=user");
        var service = new TransponderManagementHttpService(client);
        Assert.Empty(await service.GetTranspondersAsync(false));
        await service.RegisterAsync("FF-87718", false);
        await service.EndOwnershipAsync(ResourceId, false);
        await service.DeleteOwnershipAsync(ResourceId, false);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("api/admin/timing/ownerships",
            new TimingContracts.SaveTransponderOwnershipRequest(ResourceId, ResourceId, DateTimeOffset.UtcNow, null))).StatusCode);
        var proxy = (PlatformServiceTests.TestProxy)(object)app.Services.GetRequiredService<ITransponderManagementService>();
        Assert.Equal(4, proxy.Calls.Count);
        Assert.All(proxy.Calls, call => Assert.Equal(false, call.Arguments[call.Method.Name == nameof(ITransponderManagementService.GetTranspondersAsync) ? 0 : 1]));

        await client.GetAsync("signin?role=timing-admin");
        await service.GetTranspondersAsync(true);
        await service.RegisterAsync("FF-87718", true);
        await service.SaveOwnershipAsync(null, new(ResourceId, ResourceId, DateTimeOffset.UtcNow, null));
        await service.SaveOwnershipAsync(ResourceId, new(ResourceId, ResourceId, DateTimeOffset.UtcNow.AddDays(-1), null));
        await service.EndOwnershipAsync(ResourceId, true);
        await service.DeleteOwnershipAsync(ResourceId, true);
        Assert.Equal(10, proxy.Calls.Count);
    }

    [Theory]
    [InlineData("statistics-admin", HttpStatusCode.OK)]
    [InlineData("velotime-admin", HttpStatusCode.OK)]
    [InlineData("timing-admin", HttpStatusCode.Forbidden)]
    [InlineData("facilities-admin", HttpStatusCode.Forbidden)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    public async Task StatisticsAdministration_EnforcesRoleInBff(string role, HttpStatusCode status)
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        await client.GetAsync($"signin?role={role}");
        Assert.Equal(status, (await client.GetAsync("api/admin/statistics/items")).StatusCode);
        Assert.Equal(status, (await client.GetAsync("api/admin/statistics/configurations")).StatusCode);
    }

    [Fact]
    public async Task StatisticsAdministration_RejectsMissingAntiforgeryAndPreservesConflicts()
    {
        var service = PlatformServiceTests.TestProxy.Create<IStatisticsAdministrationService>((_, _) =>
            throw new HttpRequestException("This item is referenced by 2 configurations.", null, HttpStatusCode.Conflict));
        await using var app = await CreateAppAsync(administration: service);
        using var unprotected = CreateClient(app, antiforgery: false);
        await unprotected.GetAsync("signin?role=statistics-admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await unprotected.DeleteAsync($"api/admin/statistics/items/{ResourceId}")).StatusCode);

        using var client = CreateClient(app);
        await client.GetAsync("signin?role=statistics-admin");
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => new StatisticsAdministrationHttpService(client).DeleteItemAsync(ResourceId));
        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Contains("2 configurations", exception.Message);
    }

    [Fact]
    public async Task StatisticsAdministrationHttpService_ForwardsMutationsAndContracts()
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        await client.GetAsync("signin?role=statistics-admin");
        var service = new StatisticsAdministrationHttpService(client);
        Assert.Empty(await service.GetItemsAsync());
        Assert.Empty(await service.GetConfigurationsAsync());
        await service.SaveItemAsync(null, new("Lap", "", 250));
        await service.SaveItemAsync(ResourceId, new("Lap", "Updated", 250));
        var configuration = new SaveStatisticsConfigurationRequest(ResourceId, StatisticsConfigurationType.Simple, ResourceId, ResourceId, null, null);
        await service.SaveConfigurationAsync(null, configuration);
        await service.SaveConfigurationAsync(ResourceId, configuration);
        await service.DeleteConfigurationAsync(ResourceId);
        await service.DeleteItemAsync(ResourceId);
        var proxy = (PlatformServiceTests.TestProxy)(object)app.Services.GetRequiredService<IStatisticsAdministrationService>();
        Assert.Equal(8, proxy.Calls.Count);
    }

    [Fact]
    public async Task StatisticsEndpoint_ForwardsPagination()
    {
        var cursor = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsService>((_, arguments) =>
        {
            Assert.Equal(cursor, arguments[0]);
            Assert.Equal(true, arguments[1]);
            Assert.Equal(25, arguments[2]);
            return new[] { new SampleView { TransponderLabel = "rider", StartPoint = "Start", EndPoint = "Finish" } };
        });
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(statistics);
        await using var app = builder.Build();
        app.MapGroup("/api").MapStatisticsEndpoints();
        await app.StartAsync();
        using var client = app.GetTestClient();

        var response = await client.GetAsync($"/api/statistics/samples?cursor={Uri.EscapeDataString(cursor.ToString("O"))}&isNextPage=true&pageSize=25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("rider", Assert.Single((await response.Content.ReadFromJsonAsync<SampleView[]>())!).TransponderLabel);
    }

    [Theory]
    [InlineData("facilities")]
    [InlineData("facilities/fa886b15-035b-43fe-984a-04c8ce566122/layouts")]
    [InlineData("layouts/fa886b15-035b-43fe-984a-04c8ce566122")]
    [InlineData("statistics/samples")]
    public async Task PublicReads_DoNotRequireLogin(string path)
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        using var response = await client.GetAsync("api/" + path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("timing/installations")]
    [InlineData("timing/installations/fa886b15-035b-43fe-984a-04c8ce566122")]
    [InlineData("facilities/fa886b15-035b-43fe-984a-04c8ce566122/installations")]
    [InlineData("profile/username")]
    [InlineData("profile/accounts")]
    [InlineData("antiforgery")]
    [InlineData("admin/statistics/items")]
    [InlineData("admin/statistics/configurations")]
    [InlineData("transponders")]
    [InlineData("admin/timing/transponders")]
    [InlineData("admin/lookups/facilities")]
    [InlineData("admin/lookups/installations")]
    public async Task ProtectedReads_Return401WithoutRedirect(string path)
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        using var response = await client.GetAsync("api/" + path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("PUT", "timing/installations/fa886b15-035b-43fe-984a-04c8ce566122")]
    [InlineData("POST", "profile/accounts/google/link")]
    [InlineData("DELETE", "profile/accounts/google")]
    [InlineData("DELETE", "ownerships/fa886b15-035b-43fe-984a-04c8ce566122")]
    [InlineData("POST", "ownerships/fa886b15-035b-43fe-984a-04c8ce566122/end")]
    public async Task Mutations_RejectMissingAntiforgeryToken(string method, string path)
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app, antiforgery: false);
        await client.GetAsync("signin?role=timing-admin");
        using var request = new HttpRequestMessage(new HttpMethod(method), "api/" + path);
        request.Content = method == "PUT" ? JsonContent.Create(new InstallationView { Id = ResourceId })
            : method == "POST" ? JsonContent.Create("/profile") : null;
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task HttpServices_ReadAndMutateThroughCookieAuthenticatedBff()
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        await client.GetAsync("signin?role=timing-admin");
        var timing = new TimingHttpService(client);
        var profile = new ProfileHttpService(client);
        var facilities = new FacilityHttpService(client);

        Assert.Equal(ResourceId, Assert.Single(await timing.GetInstallationsAsync(CancellationToken.None)).Id);
        Assert.Equal(ResourceId, (await timing.GetInstallationAsync(ResourceId, CancellationToken.None)).Id);
        Assert.Single(await facilities.GetInstallationsAsync(ResourceId, CancellationToken.None));
        await timing.UpdateInstallationAsync(new InstallationView { Id = ResourceId, Description = "Edited" });
        Assert.Equal("rider", await profile.GetUsernameAsync());
        Assert.Empty(await profile.GetLinkedAccountsAsync());
        Assert.Equal("https://identity.example/link", await profile.GetLinkAccountAsync("/profile", "google"));
        await profile.DeleteLinkedAccountAsync("google");

        var calls = ((PlatformServiceTests.TestProxy)(object)app.Services.GetRequiredService<ITimingService>()).Calls;
        Assert.Contains(calls, call => call.Method.Name == nameof(ITimingService.UpdateInstallationAsync)
            && ((InstallationView)call.Arguments[0]!).Description == "Edited");
        var profileCalls = ((PlatformServiceTests.TestProxy)(object)app.Services.GetRequiredService<IProfileService>()).Calls;
        Assert.Contains(profileCalls, call => call.Method.Name == nameof(IProfileService.GetLinkAccountAsync)
            && Equals(call.Arguments[0], "http://localhost/profile"));
        Assert.Contains(profileCalls, call => call.Method.Name == nameof(IProfileService.DeleteLinkedAccountAsync));
        using var response = await client.GetAsync("api/profile/username");
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Update_RejectsMismatchedRouteId()
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        await client.GetAsync("signin?role=timing-admin");
        using var response = await client.PutAsJsonAsync($"api/timing/installations/{ResourceId}", new InstallationView { Id = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var service = (PlatformServiceTests.TestProxy)(object)app.Services.GetRequiredService<ITimingService>();
        Assert.Empty(service.Calls);
    }

    [Theory]
    [InlineData("https://outside.example/profile")]
    [InlineData("//outside.example/profile")]
    [InlineData("\\outside.example/profile")]
    [InlineData("javascript:alert(1)")]
    public async Task ProfileLink_RejectsExternalRedirects(string redirect)
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        await client.GetAsync("signin");
        using var response = await client.PostAsJsonAsync("api/profile/accounts/google/link", redirect);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=-1")]
    [InlineData("cursor=invalid")]
    [InlineData("isNextPage=invalid")]
    public async Task StatisticsEndpoint_RejectsInvalidPagination(string query)
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        using var response = await client.GetAsync("api/statistics/samples?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StatisticsHttpService_ForwardsPaginationAndReturnsSamples()
    {
        var cursor = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsService>((_, arguments) =>
        {
            Assert.Equal(cursor, arguments[0]);
            Assert.Equal(true, arguments[1]);
            Assert.Equal(17, arguments[2]);
            Assert.IsType<CancellationToken>(arguments[3]);
            return Array.Empty<SampleView>();
        });
        await using var app = await CreateAppAsync(statistics);
        using var client = CreateClient(app);
        Assert.Empty(await new StatisticsHttpService(client).GetSamplesAsync(cursor, true, 17));
    }

    [Theory]
    [InlineData(404, 404)]
    [InlineData(403, 403)]
    [InlineData(500, 502)]
    public async Task UpstreamFailures_ReturnSanitizedErrors(int upstreamStatus, int expectedStatus)
    {
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsService>((_, _) =>
            throw new VeloTime.Module.Statistics.Client.ApiException("secret", upstreamStatus, "secret upstream body", null!, null!));
        await using var app = await CreateAppAsync(statistics);
        using var client = CreateClient(app);
        using var response = await client.GetAsync("api/statistics/samples");
        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
        await Assert.ThrowsAsync<HttpRequestException>(() => new StatisticsHttpService(client).GetSamplesAsync(null, false, 10));
    }

    [Fact]
    public async Task AntiforgeryHandler_RejectsOffOriginRequests()
    {
        using var handler = new BffAntiforgeryHandler(new Uri("https://webui.example/")) { InnerHandler = new HttpClientHandler() };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DeleteAsync("https://outside.example/api/profile/accounts/google"));
    }

    [Fact]
    public async Task InstallationUpdate_RejectsOrdinaryUser()
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        await client.GetAsync("signin?role=user");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"api/timing/installations/{ResourceId}", new InstallationView { Id = ResourceId })).StatusCode);
    }

    [Fact]
    public async Task AuthDebug_RequiresAuthentication()
    {
        await using var app = await CreateAppAsync(authDebug: PlatformServiceTests.TestProxy.Create<IAuthDebugService>((_, _) => new AuthDebugView()));
        using var client = CreateClient(app);
        using var response = await client.GetAsync("api/debug/auth/token");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        var service = (PlatformServiceTests.TestProxy)(object)app.Services.GetRequiredService<IAuthDebugService>();
        Assert.Empty(service.Calls);
    }

    [Fact]
    public async Task AuthDebug_ReturnsNotFoundWhenUnavailable()
    {
        await using var app = await CreateAppAsync();
        using var client = CreateClient(app);
        await client.GetAsync("signin");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("api/debug/auth/token")).StatusCode);
        Assert.Null(await new AuthDebugHttpService(client).GetAccessTokenInfoAsync());
    }

    [Fact]
    public async Task AuthDebug_ReturnsTokenDetails()
    {
        var view = new AuthDebugView { HasAccessToken = true, Audiences = ["velotime.api"], RealmRoles = ["timing-admin"] };
        await using var app = await CreateAppAsync(authDebug: PlatformServiceTests.TestProxy.Create<IAuthDebugService>((_, _) => view));
        using var client = CreateClient(app);
        await client.GetAsync("signin");
        var result = await new AuthDebugHttpService(client).GetAccessTokenInfoAsync();
        Assert.NotNull(result);
        Assert.Equal(["velotime.api"], result.Audiences);
        Assert.Equal(["timing-admin"], result.RealmRoles);
    }

    [Fact]
    public void AuthDebugService_DescribesAccessTokenClaims()
    {
        var token = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler().CreateToken("""
            {"iss":"https://keycloak/realms/velotime","aud":["velotime.api","account"],"azp":"velotime.webui",
             "scope":"openid velotime:all","exp":1893456000,"realm_access":{"roles":["velotime-admin","offline_access"]}}
            """);
        var view = VeloTime.WebUI.Mud.Services.AuthDebugService.Describe(token);
        Assert.True(view.HasAccessToken);
        Assert.Equal(["velotime.api", "account"], view.Audiences);
        Assert.Equal(["velotime-admin", "offline_access"], view.RealmRoles);
        Assert.Equal("https://keycloak/realms/velotime", view.Issuer);
        Assert.Equal("velotime.webui", view.AuthorizedParty);
        Assert.Equal("openid velotime:all", view.Scope);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1893456000), view.ExpiresAt);
    }

    private static async Task<WebApplication> CreateAppAsync(IStatisticsService? statistics = null, IStatisticsAdministrationService? administration = null, IInfrastructureAdministrationService? infrastructure = null, IAuthDebugService? authDebug = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Critical);
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options => options.EventsType = typeof(TestCookieEvents));
        builder.Services.AddTransient<TestCookieEvents>();
        builder.Services.AddAdministrationAuthorization();
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        builder.Services.AddSingleton(statistics ?? PlatformServiceTests.TestProxy.Create<IStatisticsService>((_, _) => Array.Empty<SampleView>()));
        builder.Services.AddSingleton(administration ?? PlatformServiceTests.TestProxy.Create<IStatisticsAdministrationService>((method, _) => method.Name switch
        {
            nameof(IStatisticsAdministrationService.GetItemsAsync) => (IReadOnlyList<StatisticsItemDTO>)Array.Empty<StatisticsItemDTO>(),
            nameof(IStatisticsAdministrationService.GetConfigurationsAsync) => (IReadOnlyList<StatisticsConfigurationDTO>)Array.Empty<StatisticsConfigurationDTO>(),
            _ => null
        }));
        builder.Services.AddSingleton(PlatformServiceTests.TestProxy.Create<ITransponderManagementService>((method, _) => method.Name switch
        {
            nameof(ITransponderManagementService.GetTranspondersAsync) => (IReadOnlyList<TimingContracts.TransponderDTO>)Array.Empty<TimingContracts.TransponderDTO>(),
            _ => null
        }));
        builder.Services.AddSingleton(infrastructure ?? PlatformServiceTests.TestProxy.Create<IInfrastructureAdministrationService>((method, arguments) => method.Name switch
        {
            nameof(IInfrastructureAdministrationService.GetFacilitiesAsync) => (IReadOnlyList<FacilitiesContracts.FacilityDTO>)Array.Empty<FacilitiesContracts.FacilityDTO>(),
            nameof(IInfrastructureAdministrationService.GetLayoutsAsync) => (IReadOnlyList<FacilitiesContracts.CourseLayoutDTO>)Array.Empty<FacilitiesContracts.CourseLayoutDTO>(),
            nameof(IInfrastructureAdministrationService.GetInstallationsAsync) => (IReadOnlyList<TimingContracts.InstallationDTO>)Array.Empty<TimingContracts.InstallationDTO>(),
            nameof(IInfrastructureAdministrationService.SaveFacilityAsync) => new FacilitiesContracts.FacilityDTO { Id = (Guid?)arguments[0] ?? ResourceId, Name = ((FacilitiesContracts.SaveFacilityRequest)arguments[1]!).Name },
            nameof(IInfrastructureAdministrationService.SaveLayoutAsync) => new FacilitiesContracts.CourseLayoutDTO { Id = (Guid?)arguments[1] ?? ResourceId, FacilityId = (Guid)arguments[0]!, Segments = [] },
            nameof(IInfrastructureAdministrationService.SaveInstallationAsync) => new TimingContracts.InstallationDTO
            {
                Id = (Guid?)arguments[0] ?? ResourceId, AgentId = ((TimingContracts.SaveInstallationRequest)arguments[1]!).AgentId,
                FacilityId = ((TimingContracts.SaveInstallationRequest)arguments[1]!).FacilityId ?? Guid.Empty,
                Description = ((TimingContracts.SaveInstallationRequest)arguments[1]!).Description,
                TimingSystem = new TimingContracts.TimingSystemDTO { Name = ((TimingContracts.SaveInstallationRequest)arguments[1]!).TimingSystem }
            },
            nameof(IInfrastructureAdministrationService.SaveTimingPointAsync) => new TimingContracts.TimingPointDTO
            {
                Id = (Guid?)arguments[1] ?? ResourceId, SystemId = ((TimingContracts.SaveTimingPointRequest)arguments[2]!).SystemId,
                Description = ((TimingContracts.SaveTimingPointRequest)arguments[2]!).Description
            },
            _ => null
        }));
        builder.Services.AddSingleton(PlatformServiceTests.TestProxy.Create<IFacilityService>((method, _) => method.Name switch
        {
            nameof(IFacilityService.GetFacilitiesAsync) => Array.Empty<FacilityView>(),
            nameof(IFacilityService.GetFacilityLayoutsAsync) => Array.Empty<CourseLayoutView>(),
            nameof(IFacilityService.GetCourseLayoutDetailAsync) => new CourseLayoutDetailView { Id = ResourceId, Segments = [] },
            nameof(IFacilityService.GetInstallationsAsync) => new[] { new InstallationView { Id = ResourceId } },
            _ => throw new NotSupportedException(method.Name)
        }));
        builder.Services.AddSingleton(PlatformServiceTests.TestProxy.Create<ITimingService>((method, _) => method.Name switch
        {
            nameof(ITimingService.GetInstallationsAsync) => new[] { new InstallationView { Id = ResourceId } },
            nameof(ITimingService.GetInstallationAsync) => new InstallationView { Id = ResourceId },
            nameof(ITimingService.UpdateInstallationAsync) => null,
            _ => throw new NotSupportedException(method.Name)
        }));
        builder.Services.AddSingleton(PlatformServiceTests.TestProxy.Create<IProfileService>((method, _) => method.Name switch
        {
            nameof(IProfileService.GetUsernameAsync) => "rider",
            nameof(IProfileService.GetLinkedAccountsAsync) => Array.Empty<LinkedAccountView>(),
            nameof(IProfileService.GetLinkAccountAsync) => "https://identity.example/link",
            nameof(IProfileService.DeleteLinkedAccountAsync) => null,
            _ => throw new NotSupportedException(method.Name)
        }));
        builder.Services.AddSingleton(authDebug ?? PlatformServiceTests.TestProxy.Create<IAuthDebugService>((_, _) => null));
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapGet("/signin", async (HttpContext context) =>
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "rider"), new Claim(ClaimTypes.Name, "rider")], "Cookies");
            if (context.Request.Query.TryGetValue("role", out var role))
                identity.AddClaim(new Claim("realm_access", System.Text.Json.JsonSerializer.Serialize(new { roles = new[] { role.ToString() } })));
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Results.NoContent();
        });
        app.MapBffEndpoints();
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app, bool antiforgery = true)
    {
        var origin = new Uri("http://localhost/");
        var cookies = new CookieHandler { InnerHandler = app.GetTestServer().CreateHandler() };
        HttpMessageHandler handler = antiforgery ? new BffAntiforgeryHandler(origin) { InnerHandler = cookies } : cookies;
        return new HttpClient(handler) { BaseAddress = origin };
    }

    private sealed class CookieHandler : DelegatingHandler
    {
        private readonly CookieContainer cookies = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cookieHeader = cookies.GetCookieHeader(request.RequestUri!);
            if (!string.IsNullOrEmpty(cookieHeader))
            {
                request.Headers.Add("Cookie", cookieHeader);
            }
            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                foreach (var cookie in values)
                {
                    cookies.SetCookies(request.RequestUri!, cookie);
                }
            }

            return response;
        }
    }

    private sealed class TestCookieEvents() : CookieEvents(null!)
    {
        public override Task ValidatePrincipal(CookieValidatePrincipalContext context) => Task.CompletedTask;
    }
}