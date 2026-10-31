using Duende.AccessTokenManagement;
using Duende.AccessTokenManagement.OpenIdConnect;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using MudBlazor.Extensions;
using MudBlazor.Services;
using StackExchange.Redis;
using VeloTime.Module.Common;
using VeloTime.WebUI.Keycloak;
using VeloTime.WebUI.Mud;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Components;
using VeloTime.WebUI.Mud.Endpoints;
using VeloTime.WebUI.Mud.Services;
using VeloTime.WebUI.Mud.Localization;
using Yarp.ReverseProxy.Transforms;
using VeloTime.Module.Profiles.Client;

const string VELOTIME_OIDC_SCHEME = "VeloTimeOIDC";

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
CultureResolver.Configure(builder);

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = VELOTIME_OIDC_SCHEME;
    })
    .AddKeycloakOpenIdConnect(
        serviceName: "keycloak",
        realm: "velotime",
        authenticationScheme: VELOTIME_OIDC_SCHEME,
        options =>
        {
            options.ClientId = "velotime.webui";
            options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.TokenValidationParameters.NameClaimType = "preferred_username";

            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("velotime");

            options.SaveTokens = true;
            if (builder.Environment.IsDevelopment())
            {
                options.RequireHttpsMetadata = false;
            }
            else
            {
                options.Authority = builder.Configuration.GetConnectionString("keycloak");
            }
        })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        options.AccessDeniedPath = "/access-denied");

builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
builder.Services.AddCascadingAuthenticationState();

builder.Services.ConfigureCookieOidc(CookieAuthenticationDefaults.AuthenticationScheme, VELOTIME_OIDC_SCHEME);

builder.Services.AddAdministrationAuthorization();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

//builder.Services.AddMemoryCache();
builder.AddModuleCache();

builder.Services.AddDataProtection()
    .SetApplicationName("VeloTime.WebUI");
builder.Services.AddOptions<KeyManagementOptions>()
    .Configure<IConnectionMultiplexer>((options, redis) =>
        options.XmlRepository = new RedisXmlRepository(() => redis.GetDatabase(), "velotime:webui:dataprotection-keys"));

builder.Services.AddSingleton<ITicketStore, DistributedTicketStore>();

// Add MudBlazor services
builder.Services.AddMudServices();
builder.Services.AddMudLocalization();
builder.Services.AddTransient<MudBlazor.MudLocalizer, VeloTime.WebUI.Mud.Client.Services.LocalizedMudLocalizer>();
builder.Services.AddScoped<UnsavedChangesTracker>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization(options => options.SerializeAllClaims = true);

builder.Services.AddHttpContextAccessor();

//builder.Services.AddScoped<AccessTokenHandler>();

//builder.Services.AddHttpForwarder();

builder.Services
    .AddOpenIdConnectAccessTokenManagement()
    .AddBlazorServerAccessTokenManagement<ServerSideTokenStore>();

builder.Services.AddTransient<CookieEvents>();
builder.Services.AddTransient<OidcEvents>();

builder.Services
    .AddHttpClient<IKeycloakClient, KeycloakClient>((provider, client) =>
    {
        client.BaseAddress = new System.Uri("https+http://keycloak/realms/");
    })
    .AddDefaultAccessTokenResiliency()
    .AddUserAccessTokenHandler();

builder.Services
    .AddHttpClient<VeloTime.WebUI.KeycloakAdmin.IKeycloakAdminClient, VeloTime.WebUI.KeycloakAdmin.KeycloakAdminClient>(client =>
        client.BaseAddress = new Uri("https+http://keycloak/"))
    .AddDefaultAccessTokenResiliency()
    .AddUserAccessTokenHandler();

builder.Services
    .AddFacilitiesClient()
    .AddDefaultAccessTokenResiliency()
    .AddUserAccessTokenHandler();
builder.Services
    .AddStatisticsClient()
    .AddDefaultAccessTokenResiliency()
    .AddUserAccessTokenHandler();
builder.Services
    .AddTimingClient()
    .AddDefaultAccessTokenResiliency()
    .AddUserAccessTokenHandler();
builder.Services
    .AddProfilesClient()
    .AddDefaultAccessTokenResiliency()
    .AddUserAccessTokenHandler();

builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IAuthDebugService, AuthDebugService>();
builder.Services.AddScoped<IFacilityService, FacilityService>();
builder.Services.AddScoped<IStatisticsService, StatisticsService>();
builder.Services.AddScoped<IStatisticsAdministrationService, StatisticsAdministrationService>();
builder.Services.AddScoped<ITransponderManagementService, TransponderManagementService>();
builder.Services.AddScoped<ITransponderOwnerService, TransponderOwnerService>();
builder.Services.AddScoped<ITransponderStatusService, TransponderStatusService>();
builder.Services.AddScoped<IInfrastructureAdministrationService, InfrastructureAdministrationService>();
builder.Services.AddScoped<ITimingService, TimingService>();

var app = builder.Build();

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.Use((context, next) =>
    {
        context.Request.Scheme = "https";
        return next(context);
    });
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseRequestLocalization();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(VeloTime.WebUI.Mud.Client._Imports).Assembly);

app.MapGroup("/authentication").MapLoginAndLogout();
app.MapBffEndpoints();
app.MapLanguageSelectionEndpoints();

app.Run();
