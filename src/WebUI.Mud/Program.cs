using Microsoft.AspNetCore.Authentication;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MudBlazor.Extensions;
using MudBlazor.Services;
using VeloTime.Module.Facilities.Interface.Client;
using VeloTime.Module.Statistics.Interface.Client;
using VeloTime.Module.Timing.Interface.Client;
using VeloTime.WebUI.Mud;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Components;
using VeloTime.WebUI.Mud.Services;
using Yarp.ReverseProxy.Transforms;

const string VELOTIME_OIDC_SCHEME = "VeloTimeOIDC";

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddAuthentication(VELOTIME_OIDC_SCHEME)
    .AddKeycloakOpenIdConnect(
        serviceName: "keycloak",
        realm: "velotime",
        authenticationScheme: VELOTIME_OIDC_SCHEME,
        options =>
        {
            options.ClientId = "velotime.webui";
            options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.Scope.Add("velotime:api");

            options.SaveTokens = true;
            if (builder.Environment.IsDevelopment())
            {
                options.RequireHttpsMetadata = false;
            }
        })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);

builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
builder.Services.AddCascadingAuthenticationState();

builder.Services.ConfigureCookieOidc(CookieAuthenticationDefaults.AuthenticationScheme, VELOTIME_OIDC_SCHEME);

builder.Services.AddAuthorization();

builder.Services.AddMemoryCache();

// Add MudBlazor services
builder.Services.AddMudServices();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    //.AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<AccessTokenHandler>();

builder.Services.AddHttpForwarder();

builder.Services
    .AddFacilitiesClient()
    .AddHttpMessageHandler<AccessTokenHandler>();
builder.Services
    .AddStatisticsClient()
    .AddHttpMessageHandler<AccessTokenHandler>();
builder.Services
    .AddTimingClient()
    .AddHttpMessageHandler<AccessTokenHandler>();

builder.Services.AddScoped<IFacilityService, FacilityService>();
builder.Services.AddScoped<IStatisticsService, StatisticsService>();
builder.Services.AddScoped<ITimingService, TimingService>();

//builder.Services.AddScoped<IdentityUserAccessor>();
//builder.Services.AddScoped<IdentityRedirectManager>();
//builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

//builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    //app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    //.AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(VeloTime.WebUI.Mud.Client._Imports).Assembly);

// Add additional endpoints required by the Identity /Account Razor components.
//app.MapAdditionalIdentityEndpoints();

app.MapGroup("/authentication").MapLoginAndLogout();

app.Run();
