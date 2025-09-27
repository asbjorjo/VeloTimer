using Microsoft.IdentityModel.Tokens;
using NetEscapades.AspNetCore.SecurityHeaders.Infrastructure;

namespace VeloTime.WebUI.Server;

internal static class HostingExtensions
{
    private static IWebHostEnvironment? _env;
    public static WebApplication ConfigureServices(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        _env = builder.Environment;

        services.AddSecurityHeaderPolicies()
            .SetPolicySelector((PolicySelectorContext ctx) =>
            {
                return SecurityHeadersDefinitions.GetHeaderPolicyCollection(_env.IsDevelopment(),
                  configuration["OpenIDConnectSettings:Authority"]!);
            });

        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryDefaults.HeaderName;
            options.Cookie.Name = AntiforgeryDefaults.CookieName;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        services.AddHttpClient();
        services.AddOptions();

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie()
        .AddOpenIdConnect(options =>
        {
            configuration.GetSection("OpenIDConnectSettings").Bind(options);

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
        });

        services.AddControllersWithViews(options =>
             options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

        services.AddRazorPages().AddMvcOptions(options =>
        {
            //var policy = new AuthorizationPolicyBuilder()
            //    .RequireAuthenticatedUser()
            //    .Build();
            //options.Filters.Add(new AuthorizeFilter(policy));
        });

        return builder.Build();
    }
    public static WebApplication ConfigurePipeline(this WebApplication app)
    {
        if (_env!.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseWebAssemblyDebugging();
        }
        else
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseSecurityHeaders();

        app.UseHttpsRedirection();
        app.UseBlazorFrameworkFiles();
        app.UseStaticFiles();

        app.UseRouting();

        app.UseNoUnauthorizedRedirect("/api");

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapRazorPages();
        app.MapControllers();
        app.MapNotFound("/api/{**segment}");
        app.MapFallbackToPage("/_Host");

        return app;
    }
}