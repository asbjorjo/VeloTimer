using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Microsoft.AspNetCore.Authorization;
using SlimMessageBus;
using VeloTime.Module.Common;
using VeloTime.Module.Common;
using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Statistics.Client;
using VeloTime.Module.Timing.Client;
using VeloTime.Users.Migration.Util;
using VeloTime.WebUI.Keycloak;
using VeloTime.WebUI.Mud.Client.ViewModel;
using VeloTime.WebUI.Mud.Services;

namespace VeloTime.Platform.Test;

public sealed class PlatformServiceTests
{
    [Fact]
    public async Task WebUiUserInfo_PropagatesRealmPermissionsToAuthenticationTicket()
    {
        const string scheme = "VeloTimeOIDC";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication().AddOpenIdConnect(scheme, options =>
        {
            options.ClientId = "velotime.webui";
            options.Authority = "https://keycloak.example/realms/velotime";
        });
        services.AddAdministrationAuthorization();
        var configuration = typeof(VeloTime.WebUI.Mud.OidcEvents).Assembly.GetType(
            "Microsoft.Extensions.DependencyInjection.CookieOidcServiceCollectionExtensions")!;
        configuration.GetMethod("ConfigureCookieOidc")!.Invoke(null,
            [services, CookieAuthenticationDefaults.AuthenticationScheme, scheme]);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>>().Get(scheme);
        using var userInfo = System.Text.Json.JsonDocument.Parse("""{"realm_access":{"roles":["timing-admin"]}}""");
        var identity = new ClaimsIdentity("oidc");
        foreach (var action in options.ClaimActions)
        {
            action.Run(userInfo.RootElement, identity, options.ClaimsIssuer ?? options.Authority!);
        }

        var principal = await provider.GetRequiredService<IClaimsTransformation>()
            .TransformAsync(new ClaimsPrincipal(identity));
        var ticket = new AuthenticationTicket(principal, CookieAuthenticationDefaults.AuthenticationScheme);
        var restored = TicketSerializer.Default.Deserialize(TicketSerializer.Default.Serialize(ticket))!;

        Assert.True(options.GetClaimsFromUserInfoEndpoint);
        Assert.True(AdministrationAuthorization.HasPermission(restored.Principal, AdministrationAuthorization.TimingPermission));
        Assert.False(AdministrationAuthorization.HasPermission(restored.Principal, AdministrationAuthorization.FacilitiesPermission));

        var clientServices = new ServiceCollection();
        clientServices.AddLogging();
        clientServices.AddAuthorizationCore(VeloTime.WebUI.Mud.Client.AdministrationPolicies.Configure);
        using var clientProvider = clientServices.BuildServiceProvider();
        var authorization = clientProvider.GetRequiredService<IAuthorizationService>();
        Assert.True((await authorization.AuthorizeAsync(restored.Principal, VeloTime.WebUI.Mud.Client.AdministrationPolicies.Timing)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(restored.Principal, VeloTime.WebUI.Mud.Client.AdministrationPolicies.Facilities)).Succeeded);
    }

    [Theory]
    [InlineData("velotime-admin", 6, System.Net.HttpStatusCode.OK, "Development", "https://development.veloti.me")]
    [InlineData("timing-admin", 3, System.Net.HttpStatusCode.OK, "Development", "https://development.veloti.me")]
    [InlineData("facilities-admin", 3, System.Net.HttpStatusCode.Forbidden, "Development", "https://development.veloti.me")]
    [InlineData("statistics-admin", 2, System.Net.HttpStatusCode.Forbidden, "Development", "https://development.veloti.me")]
    [InlineData("user", 0, System.Net.HttpStatusCode.Forbidden, "Development", "https://development.veloti.me")]
    [InlineData("timing-admin", 3, System.Net.HttpStatusCode.OK, "Production", "https://veloti.me")]
    [InlineData("timing-admin", 3, System.Net.HttpStatusCode.OK, "Staging", "https://preview.example")]
    public async Task ApiBearerAuthentication_PropagatesTokenRealmPermissions(string role, int permissionCount, System.Net.HttpStatusCode timingStatus, string environment, string audience)
    {
        const string issuer = "https://keycloak.example/realms/velotime";
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Configuration["ConnectionStrings:keycloak"] = issuer;
        builder.Configuration["OIDC_AUDIENCE"] = audience;
        builder.AddModuleAuthentication();
        builder.Services.Configure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
            Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Configuration = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration { Issuer = issuer };
                options.Configuration.SigningKeys.Add(key);
            });
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/permissions", (ClaimsPrincipal user) => user.FindAll(AdministrationAuthorization.PermissionClaimType)
            .Select(claim => claim.Value).ToArray()).RequireAuthorization();
        app.MapGet("/timing", () => "allowed").RequireAuthorization(AdministrationAuthorization.TimingPolicy);
        await app.StartAsync();
        using var client = app.GetTestClient();
        var descriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "test-user",
                ["realm_access"] = System.Text.Json.JsonSerializer.SerializeToElement(new { roles = new[] { role } })
            }
        };
        var tokenHandler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenHandler.CreateToken(descriptor));

        var response = await client.GetAsync("/permissions");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var permissions = System.Text.Json.JsonSerializer.Deserialize<string[]>(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(permissionCount, permissions.Length);
        Assert.Equal(timingStatus, (await client.GetAsync("/timing")).StatusCode);

        foreach (var invalidAudience in new[] { "untrusted-api", "velotime.api", "https://another-environment.example", audience + "/", null })
        {
            descriptor.Audience = invalidAudience;
            client.DefaultRequestHeaders.Authorization = new("Bearer", tokenHandler.CreateToken(descriptor));
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.GetAsync("/permissions")).StatusCode);
        }
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task ApiBearerAuthentication_RequiresExplicitValidAudience(string environment)
    {
        foreach (var audience in new[] { null, "", " ", "velotime.api", "ftp://example.com", "https://user:password@example.com", "https://example.com?query=value", "https://example.com#fragment", " https://example.com " })
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                EnvironmentName = environment,
                DisableDefaults = true
            });
            builder.Configuration["OIDC_AUDIENCE"] = audience;
            builder.Configuration["ConnectionStrings:keycloak"] = "https://keycloak.example/realms/velotime";
            builder.AddModuleAuthentication();
            using var host = builder.Build();

            var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
            Assert.Contains("OIDC_AUDIENCE", exception.Message);
        }
    }

    [Theory]
    [InlineData("velotime-admin", true, true, true, true, true, 6)]
    [InlineData("facilities-admin", true, false, false, true, true, 3)]
    [InlineData("timing-admin", false, true, false, true, true, 3)]
    [InlineData("statistics-admin", false, false, true, false, true, 2)]
    [InlineData("user", false, false, false, false, false, 0)]
    public async Task AdministrationPolicies_UsePermissionClaimsOnly(string realmRole, bool facilities, bool timing, bool statistics, bool linking, bool references, int expectedPermissionCount)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAdministrationAuthorization();
        using var provider = services.BuildServiceProvider();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("realm_access", System.Text.Json.JsonSerializer.Serialize(new { roles = new[] { realmRole } }))
        ], "test"));
        var transformation = provider.GetRequiredService<IClaimsTransformation>();
        await transformation.TransformAsync(principal);
        await transformation.TransformAsync(principal);

        Assert.Empty(principal.FindAll(ClaimTypes.Role));
        Assert.Equal(expectedPermissionCount, principal.FindAll(AdministrationAuthorization.PermissionClaimType).Count());
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        Assert.Equal(facilities, (await authorization.AuthorizeAsync(principal, AdministrationAuthorization.FacilitiesPolicy)).Succeeded);
        Assert.Equal(timing, (await authorization.AuthorizeAsync(principal, AdministrationAuthorization.TimingPolicy)).Succeeded);
        Assert.Equal(statistics, (await authorization.AuthorizeAsync(principal, AdministrationAuthorization.StatisticsPolicy)).Succeeded);
        Assert.Equal(linking, (await authorization.AuthorizeAsync(principal, AdministrationAuthorization.TimingLinkPolicy)).Succeeded);
        Assert.Equal(references, (await authorization.AuthorizeAsync(principal, AdministrationAuthorization.ReferencePolicy)).Succeeded);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("null")]
    [InlineData("{\"roles\":\"velotime-admin\"}")]
    [InlineData("{\"roles\":[null,1,{}]}")]
    public async Task PermissionClaims_IgnoreMalformedRealmAccess(string realmAccess)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("realm_access", realmAccess)], "test"));
        await new RealmPermissionClaimsTransformation().TransformAsync(principal);
        Assert.Empty(principal.FindAll(AdministrationAuthorization.PermissionClaimType));
    }

    [Fact]
    public async Task PermissionPolicies_RejectUnauthenticatedIdentity()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAdministrationAuthorization();
        using var provider = services.BuildServiceProvider();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(AdministrationAuthorization.PermissionClaimType, AdministrationAuthorization.GlobalPermission)
        ]));
        Assert.False((await provider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(principal, AdministrationAuthorization.TimingPolicy)).Succeeded);
    }

    [Theory]
    [InlineData("InstallationDetails", "installation_details")]
    [InlineData("Version2Id", "version2_id")]
    [InlineData("", "")]
    public void ToSnakeCase_ConvertsNames(string input, string expected)
    {
        Assert.Equal(expected, input.ToSnakeCase());
    }

    [Fact]
    public void SnakeCaseModel_ConvertsTableColumnsKeysAndIndexes()
    {
        var builder = new ModelBuilder();
        builder.Entity<SnakeCaseEntity>(entity =>
        {
            entity.HasKey(item => item.Id).HasName("PK_SnakeCaseEntity");
            entity.HasIndex(item => item.CreatedAt).HasDatabaseName("IX_SnakeCaseEntity_CreatedAt");
        });

        builder.SnakeCaseModel();

        var entityType = Assert.IsAssignableFrom<IEntityType>(builder.Model.FindEntityType(typeof(SnakeCaseEntity)));
        Assert.Equal("snake_case_entity", entityType.GetTableName());
        Assert.Equal("created_at", entityType.FindProperty(nameof(SnakeCaseEntity.CreatedAt))!.GetColumnName());
        Assert.Equal("pk_snake_case_entity", entityType.FindPrimaryKey()!.GetName());
        Assert.Equal("ix_snake_case_entity_created_at", entityType.GetIndexes().Single().GetDatabaseName());
    }

    [Fact]
    public async Task ServiceDefaults_MapsHealthAndAlivenessEndpointsInDevelopment()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.AddDefaultHealthChecks();
        await using var app = builder.Build();
        app.MapDefaultEndpoints();
        await app.StartAsync();
        using var client = app.GetTestClient();

        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/alive")).StatusCode);
        await app.StopAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenTelemetry_StartsWithSignalSpecificExporters(bool registerExportersFirst)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4317";
        builder.Logging.AddFilter(_ => false);
        builder.Services.ConfigureAll<OtlpExporterOptions>(options => options.TimeoutMilliseconds = 10);

        if (!registerExportersFirst)
        {
            builder.ConfigureOpenTelemetry();
        }

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddOtlpExporter())
            .WithMetrics(metrics => metrics.AddOtlpExporter());
        builder.Logging.AddOpenTelemetry(logging => logging.AddOtlpExporter());

        if (registerExportersFirst)
        {
            builder.ConfigureOpenTelemetry();
        }

        using var host = builder.Build();
        await host.StartAsync();

        Assert.NotNull(host.Services.GetRequiredService<TracerProvider>());
        Assert.NotNull(host.Services.GetRequiredService<MeterProvider>());
        Assert.NotNull(host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("OpenTelemetryTest"));
        var loggingOptions = host.Services.GetRequiredService<IOptions<OpenTelemetryLoggerOptions>>().Value;
        Assert.True(loggingOptions.IncludeFormattedMessage);
        Assert.True(loggingOptions.IncludeScopes);

        await host.StopAsync();
    }

    [Fact]
    public async Task OpenTelemetry_ExportsAllSignalsWithoutAutoInstrumentation()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4318";
        builder.ConfigureOpenTelemetry();
        using var handler = new TelemetryHttpHandler();
        builder.Services.ConfigureAll<OtlpExporterOptions>(options =>
        {
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
            options.HttpClientFactory = () => new HttpClient(handler, disposeHandler: false);
        });
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource("OpenTelemetryTest"))
            .WithMetrics(metrics => metrics.AddMeter("OpenTelemetryTest"));
        using (var host = builder.Build())
        {
            await host.StartAsync();

            using var source = new ActivitySource("OpenTelemetryTest");
            using (var activity = source.StartActivity("test"))
            {
                Assert.NotNull(activity);
            }
            using var meter = new System.Diagnostics.Metrics.Meter("OpenTelemetryTest");
            meter.CreateCounter<int>("test.counter").Add(1);
            host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("OpenTelemetryTest").LogInformation("Telemetry test");

            Assert.True(host.Services.GetRequiredService<TracerProvider>().ForceFlush());
            Assert.True(host.Services.GetRequiredService<MeterProvider>().ForceFlush());
            await host.StopAsync();
        }

        Assert.Contains("/v1/traces", handler.Paths);
        Assert.Contains("/v1/metrics", handler.Paths);
        Assert.Contains("/v1/logs", handler.Paths);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task OpenTelemetry_DoesNotAddExportersWithoutEndpoint(string? endpoint)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint;
        builder.Logging.AddFilter(_ => false);
        builder.ConfigureOpenTelemetry();
        builder.Services.AddOpenTelemetry().UseOtlpExporter();
        builder.Services.ConfigureAll<OtlpExporterOptions>(options => options.TimeoutMilliseconds = 10);
        using var host = builder.Build();

        await host.StartAsync();
        Assert.NotNull(host.Services.GetRequiredService<TracerProvider>());
        Assert.NotNull(host.Services.GetRequiredService<MeterProvider>());
        await host.StopAsync();
    }

    private sealed class TelemetryHttpHandler : HttpMessageHandler
    {
        public System.Collections.Concurrent.ConcurrentBag<string> Paths { get; } = [];

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([])
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Send(request, cancellationToken));
    }

    [Fact]
    public void TelemetryAddLink_UsesDiagnosticIdHeader()
    {
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var diagnosticId = $"00-{traceId}-{spanId}-01";
        var context = TestProxy.Create<IConsumerContext>((method, _) => method.Name == "get_Headers"
            ? new Dictionary<string, object> { ["Diagnostic-Id"] = diagnosticId }
            : null);
        var activity = new Activity("consumer");

        Assert.Same(activity, activity.AddLink(context));
        Assert.Contains(activity.Links, link => link.Context.TraceId == traceId && link.Context.SpanId == spanId);
    }

    [Fact]
    public async Task WebUiAuthenticationEndpoints_NormalizeRedirectsAndSetChallengeActions()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        builder.Services.AddAuthentication(options => options.DefaultChallengeScheme = TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(CookieAuthenticationDefaults.AuthenticationScheme, _ => { })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("VeloTimeOIDC", _ => { });
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAntiforgery();
        app.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Text(antiforgery.GetAndStoreTokens(context).RequestToken!));
        app.MapLoginAndLogout();
        await app.StartAsync();
        using var client = app.GetTestClient();

        var login = await client.GetAsync("/login?returnUrl=https%3A%2F%2Foutside.example%2Fprofile%3Ftab%3Dlinked");
        Assert.Equal("/profile?tab=linked", login.Headers.GetValues("X-Test-Redirect").Single());

        var password = await client.GetAsync("/changepass?returnUrl=profile");
        Assert.Equal("/profile", password.Headers.GetValues("X-Test-Redirect").Single());
        Assert.Equal("UPDATE_PASSWORD", password.Headers.GetValues("X-Test-Action").Single());

        var provider = await client.GetAsync("/addprovider/google?returnUrl=%2Fsettings");
        Assert.Equal("/settings", provider.Headers.GetValues("X-Test-Redirect").Single());
        Assert.Equal("idp_link:google", provider.Headers.GetValues("X-Test-Action").Single());

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["returnUrl"] = "https://outside.example/account"
        });
        var csrfResponse = await client.GetAsync("/csrf");
        var csrfToken = await csrfResponse.Content.ReadAsStringAsync();
        var cookies = csrfResponse.Headers.GetValues("Set-Cookie").Select(cookie => cookie.Split(';', 2)[0]);
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies));
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrfToken);
        TestAuthHandler.LastSignOutRedirect = null;
        var logout = await client.PostAsync("/logout", form);
        Assert.True(TestAuthHandler.LastSignOutRedirect == "/account", $"Status={logout.StatusCode}; redirect={TestAuthHandler.LastSignOutRedirect ?? "<none>"}; body={await logout.Content.ReadAsStringAsync()}");
        Assert.NotEqual(System.Net.HttpStatusCode.NotFound, logout.StatusCode);
        await app.StopAsync();
    }

    [Fact]
    public async Task FacilityService_MapsAndOrdersFacilitiesLayoutsAndInstallations()
    {
        var facilityId = Guid.NewGuid();
        var facility = new FacilityDTO { Id = facilityId, Name = "North track", Layouts = [Guid.NewGuid()] };
        var facilities = TestProxy.Create<IFacilitiesClient>((method, _) => method.Name switch
        {
            nameof(IFacilitiesClient.FacilityAllAsync) => new List<FacilityDTO>
            {
                new() { Id = Guid.NewGuid(), Name = "Zulu", Layouts = [] },
                facility
            },
            nameof(IFacilitiesClient.Facility2Async) => facility,
            nameof(IFacilitiesClient.LayoutsAsync) => LayoutWithId(facilityId, (Guid)_[0]!),
            nameof(IFacilitiesClient.LayoutsAllAsync) => new List<CourseLayoutDTO> { CreateLayout(facilityId) },
            _ => throw new NotSupportedException(method.Name)
        });
        var installation = CreateInstallation(facilityId);
        var timing = TestProxy.Create<ITimingClient>((method, _) => method.Name switch
        {
            nameof(ITimingClient.InstallationsAllAsync) => new List<Module.Timing.Client.InstallationDTO> { installation },
            _ => throw new NotSupportedException(method.Name)
        });
        using var cacheHost = new WebUiCacheTestHost(facilities);
        var service = new FacilityService(timing, cacheHost.Cache);

        var allFacilities = (await service.GetFacilitiesAsync(CancellationToken.None)).ToList();
        var layoutDetail = await service.GetCourseLayoutDetailAsync(Guid.NewGuid());
        var layouts = (await service.GetFacilityLayoutsAsync(facilityId, CancellationToken.None)).ToList();
        var installations = (await service.GetInstallationsAsync(facilityId, CancellationToken.None)).ToList();

        Assert.Equal("North track", allFacilities[0].Name);
        Assert.Equal(1, layoutDetail.Segments.First().Order);
        Assert.Equal(Guid.Empty, layoutDetail.Segments.Single(segment => segment.Order == 2).StartPoint.TimingPoint);
        Assert.Equal("Start - Finish", layouts.Single().Segments.First().Name);
        Assert.Equal("North track", installations.Single().FacilityName);
    }

    [Fact]
    public async Task TimingService_MapsInstallationDetailsAndUpdatePayload()
    {
        var facilityId = Guid.NewGuid();
        var installationId = Guid.NewGuid();
        var timingPointId = Guid.NewGuid();
        Module.Timing.Client.InstallationDTO? updated = null;
        var facilities = TestProxy.Create<IFacilitiesClient>((method, _) => method.Name switch
        {
            nameof(IFacilitiesClient.Facility2Async) => new FacilityDTO { Id = facilityId, Name = "Arena", Layouts = [] },
            nameof(IFacilitiesClient.FacilityAllAsync) => new List<FacilityDTO> { new() { Id = facilityId, Name = "Arena", Layouts = [] } },
            _ => throw new NotSupportedException(method.Name)
        });
        var apiInstallation = CreateInstallation(facilityId);
        apiInstallation.Id = installationId;
        apiInstallation.TimingPoints = [new Module.Timing.Client.TimingPointDTO { Id = timingPointId, Description = "Finish", SystemId = "loop-1" }];
        var timing = TestProxy.Create<ITimingClient>((method, args) => method.Name switch
        {
            nameof(ITimingClient.InstallationsGETAsync) => apiInstallation,
            nameof(ITimingClient.InstallationsAllAsync) => new List<Module.Timing.Client.InstallationDTO> { apiInstallation },
            nameof(ITimingClient.InstallationsPUTAsync) => RecordUpdate(args, value => updated = value),
            _ => throw new NotSupportedException(method.Name)
        });
        using var cacheHost = new WebUiCacheTestHost(facilities, timing);
        var service = new TimingService(timing, cacheHost.Cache);

        var detail = await service.GetInstallationAsync(installationId);
        var installations = (await service.GetInstallationsAsync(CancellationToken.None)).ToList();
        await service.UpdateInstallationAsync(new InstallationView
        {
            Id = installationId,
            FacilityId = facilityId,
            AgentId = "agent-1",
            TimingSystem = "X2",
            Description = "Edited",
            TimingPoints = [new TimingPointView { Id = timingPointId, Description = "Finish", SystemId = "loop-1" }]
        });

        Assert.Equal("Arena", detail.FacilityName);
        Assert.Equal("Finish", Assert.Single(detail.TimingPoints).Description);
        Assert.Equal("Arena", installations.Single().FacilityName);
        Assert.Equal("Edited", updated!.Description);
        Assert.Equal(timingPointId, Assert.Single(updated.TimingPoints).Id);
    }

    [Fact]
    public async Task StatisticsService_MapsSamplesAndCachesCoursePointNames()
    {
        var startId = Guid.NewGuid();
        var endId = Guid.NewGuid();
        var statistics = TestProxy.Create<IStatisticsClient>((method, _) => method.Name == nameof(IStatisticsClient.SampleAsync)
            ? new GetSamplesResponse
            {
                Samples = [
                    new Module.Statistics.Client.SampleDTO
                    {
                        Time = DateTimeOffset.UtcNow,
                        TransponderId = Guid.NewGuid(),
                        CoursePointStartId = startId,
                        CoursePointEndId = endId,
                        Distance = 600,
                        Duration = "00:00:30",
                        Speed = 72
                    },
                    new Module.Statistics.Client.SampleDTO
                    {
                        Time = DateTimeOffset.UtcNow,
                        TransponderId = Guid.NewGuid(),
                        CoursePointStartId = startId,
                        CoursePointEndId = endId,
                        Distance = 500,
                        Duration = "00:00:25",
                        Speed = 72
                    }
                ]
            }
            : throw new NotSupportedException(method.Name));
        var facility = TestProxy.Create<IFacilitiesClient>((method, args) =>
        {
            if (method.Name != nameof(IFacilitiesClient.CoursepointAsync))
            {
                throw new NotSupportedException(method.Name);
            }

            var coursePointId = args[0] is Guid id ? id : throw new InvalidOperationException("Missing course point ID.");
            return new CoursePointDTO { Id = coursePointId, Name = coursePointId == startId ? "Start" : "Finish" };
        });
        using var cache = new WebUiCacheTestHost(facility);
        var service = new StatisticsService(statistics, cache.Cache);

        var result = (await service.GetSamplesAsync(null, true, 10)).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal("Start", result[0].StartPoint);
        Assert.Equal("Finish", result[0].EndPoint);
        Assert.Equal(TimeSpan.FromSeconds(30), result[0].Duration);
        Assert.Equal(2, ((TestProxy)(object)facility).Calls.Count);
    }

    [Fact]
    public async Task ProfileService_MapsLinkedAccountsAndDelegatesLinkActions()
    {
        var keycloak = TestProxy.Create<IKeycloakClient>((method, _) => method.Name switch
        {
            nameof(IKeycloakClient.GetAccountAsync) => new AccountRepresentation { Username = "rider" },
            nameof(IKeycloakClient.GetLinkedAccountsAsync) => new List<LinkedAccountRepresentation>
            {
                new() { Connected = true, Social = true, DisplayName = "Rider Account", ProviderName = "provider" }
            },
            nameof(IKeycloakClient.BuildLinkingUriAsync) => new AccountLinkUriRepresentation { AccountLinkUri = "https://identity/link" },
            nameof(IKeycloakClient.DeleteLinkedProviderAsync) => null,
            _ => throw new NotSupportedException(method.Name)
        });
        using var cacheHost = new WebUiCacheTestHost(keycloak);
        var service = new ProfileService(keycloak, cacheHost.Cache);

        Assert.Equal("rider", await service.GetUsernameAsync(CancellationToken.None));
        var linked = Assert.Single(await service.GetLinkedAccountsAsync(CancellationToken.None));
        Assert.True(linked.IsConnected);
        Assert.True(linked.IsSocial);
        Assert.Equal("Rider Account", linked.DisplayName);
        Assert.Equal("https://identity/link", await service.GetLinkAccountAsync("/profile", "provider"));
        await service.DeleteLinkedAccountAsync("provider");

        var calls = ((TestProxy)(object)keycloak).Calls;
        Assert.Contains(calls, call => call.Method.Name == nameof(IKeycloakClient.DeleteLinkedProviderAsync)
            && Equals(call.Arguments[0], "velotime") && Equals(call.Arguments[1], "provider"));
    }

    private static CourseLayoutDTO LayoutWithId(Guid facilityId, Guid id)
    {
        var layout = CreateLayout(facilityId);
        layout.Id = id;
        return layout;
    }

    private static CourseLayoutDTO CreateLayout(Guid facilityId)
    {
        return new CourseLayoutDTO
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            Segments = [
                new SegmentDTO
                {
                    Id = Guid.NewGuid(), Order = 2, Length = 25,
                    Start = new CoursePointDTO { Id = Guid.NewGuid(), Name = "Finish" },
                    End = new CoursePointDTO { Id = Guid.NewGuid(), Name = "Start", TimingPointId = Guid.NewGuid() }
                },
                new SegmentDTO
                {
                    Id = Guid.NewGuid(), Order = 1, Length = 50,
                    Start = new CoursePointDTO { Id = Guid.NewGuid(), Name = "Start", TimingPointId = Guid.NewGuid() },
                    End = new CoursePointDTO { Id = Guid.NewGuid(), Name = "Finish", TimingPointId = Guid.NewGuid() }
                }
            ]
        };
    }

    private static Module.Timing.Client.InstallationDTO CreateInstallation(Guid facilityId)
    {
        return new Module.Timing.Client.InstallationDTO
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            AgentId = "agent-1",
            Description = "Installation",
            TimingSystem = new Module.Timing.Client.TimingSystemDTO { Name = "X2" },
            TimingPoints = []
        };
    }

    private static object? RecordUpdate(object?[] args, Action<Module.Timing.Client.InstallationDTO> update)
    {
        update((Module.Timing.Client.InstallationDTO)args[1]!);
        return null;
    }

    private sealed class SnakeCaseEntity
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class TestProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = (_, _) => throw new NotSupportedException();
        public List<ProxyCall> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var method = targetMethod ?? throw new InvalidOperationException("Missing proxy method.");
            var arguments = args ?? [];
            Calls.Add(new ProxyCall(method, arguments));
            var result = Handler(method, arguments);
            if (method.ReturnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = method.ReturnType.GetGenericArguments()[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(resultType)
                    .Invoke(null, [result]);
            }

            return result;
        }

        public static T Create<T>(Func<MethodInfo, object?[], object?> handler) where T : class
        {
            var instance = DispatchProxy.Create<T, TestProxy>();
            ((TestProxy)(object)instance).Handler = handler;
            return instance;
        }
    }

    public sealed record ProxyCall(MethodInfo Method, object?[] Arguments);

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder), IAuthenticationSignOutHandler
    {
        public const string SchemeName = "TestAuth";
        public static string? LastSignOutRedirect { get; set; }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.Headers["X-Test-Redirect"] = properties.RedirectUri ?? string.Empty;
            Response.Headers["X-Test-Action"] = properties.GetParameter<string>("kc_action") ?? string.Empty;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(AuthenticationProperties? properties)
        {
            LastSignOutRedirect = properties?.RedirectUri;
            return Task.CompletedTask;
        }
    }
}
