using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
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

//builder.Services.AddAuthentication(options =>
//    {
//        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
//        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
//    })
builder.Services.AddAuthentication(VELOTIME_OIDC_SCHEME)
    .AddOpenIdConnect(VELOTIME_OIDC_SCHEME, options =>
    {
        builder.Configuration.GetSection("OpenIDConnectSettings").Bind(options);

        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.ResponseType = OpenIdConnectResponseType.Code;

        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "name",
            RoleClaimType = "role"
        };
        options.Scope.Add("veloTimeApi");
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);

builder.Services.ConfigureCookieOidc(CookieAuthenticationDefaults.AuthenticationScheme, VELOTIME_OIDC_SCHEME);

builder.Services.AddAuthorization();

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddMemoryCache();

// Add MudBlazor services
builder.Services.AddMudServices();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<AccessTokenHandler>();

builder.Services.AddHttpForwarder();

builder.Services
    .AddHttpClient<IFacitiliesClient, HttpFacilitiesClient>()
    .AddHttpMessageHandler<AccessTokenHandler>();
builder.Services
    .AddHttpClient<IStatisticsClient, HttpStatisticsClient>()
    .AddHttpMessageHandler<AccessTokenHandler>();
builder.Services
    .AddHttpClient<ITimingClient, TimingHttpClient>()
    .AddHttpMessageHandler<AccessTokenHandler>();

builder.Services.AddScoped<IFacilityService, FacilityService>();
builder.Services.AddScoped<IStatisticsService, StatisticsService>();
builder.Services.AddScoped<ITimingService, TimingService>();

//builder.Services.AddScoped<IdentityUserAccessor>();
//builder.Services.AddScoped<IdentityRedirectManager>();
//builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();


//builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
//    .AddEntityFrameworkStores<ApplicationDbContext>()
//    .AddSignInManager()
//    .AddDefaultTokenProviders();

//builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.UseMigrationsEndPoint();
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
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(VeloTime.WebUI.Mud.Client._Imports).Assembly);

// Add additional endpoints required by the Identity /Account Razor components.
//app.MapAdditionalIdentityEndpoints();

//app.MapForwarder("/api/statistics/sample", "https://localhost:7289", transformBuilder =>
//{
//    transformBuilder.AddRequestTransform(async context =>
//    {
//        var accessToken = await context.HttpContext.GetTokenAsync("access_token");
//        context.ProxyRequest.Headers.Authorization =
//            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
//    });
//}).RequireAuthorization();

app.MapGroup("/authentication").MapLoginAndLogout();

app.MapGet("/api/statistics/samples", ([FromServices] IStatisticsService statistics) =>
{
    return statistics.GetSamplesAsync(null, true, 20);
});
app.MapGet("/api/facilities", ([FromServices] IFacilityService facilities) =>
{
    return facilities.GetFacilitiesAsync();
});

app.MapGet("/api/facilities/{id}/installations", (Guid id, [FromServices] IFacilityService facilities) =>
{
    return facilities.GetInstallationsAsync(id);
});

app.MapGet("/api/facilities/{id}/layouts", (Guid id, [FromServices] IFacilityService facilities) =>
{
    return facilities.GetFacilityLayoutsAsync(id);
});

app.MapGet("/api/layouts/{id}", (Guid id, [FromServices] IFacilityService facilities) =>
{
    return facilities.GetCourseLayoutDetailAsync(id);
});

app.MapGet("/api/timing/installations", ([FromServices] ITimingService timing) =>
{
    return timing.GetInstallationsAsync();
});

app.MapGet("/api/timing/installations/{id:guid}", (Guid id ,[FromServices] ITimingService timing) =>
{
    return timing.GetInstallationAsync(id);
});

app.Run();
