using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using VeloTime.WebUI.Mud;

namespace Microsoft.Extensions.DependencyInjection;

internal static partial class CookieOidcServiceCollectionExtensions
{
    public static IServiceCollection ConfigureCookieOidc(this IServiceCollection services, string cookieScheme, string oidcScheme)
    {
        //services.AddSingleton<CookieOidcRefresher>();

        //services.AddOptions<CookieAuthenticationOptions>(cookieScheme).Configure<CookieOidcRefresher>((cookieOptions, refresher) =>
        //{
        //    cookieOptions.Events.OnValidatePrincipal = context => refresher.ValidateOrRefreshCookieAsync(context, oidcScheme);
        //});

        services.AddOptions<CookieAuthenticationOptions>(cookieScheme).Configure(cookieOptions =>
        {
            cookieOptions.EventsType = typeof(CookieEvents);
        });

        services.AddOptions<OpenIdConnectOptions>(oidcScheme).Configure(oidcOptions =>
        {
            oidcOptions.GetClaimsFromUserInfoEndpoint = true;
            oidcOptions.MapInboundClaims = false;

            // Request a refresh_token.
            oidcOptions.Scope.Add(OpenIdConnectScope.OfflineAccess);

            oidcOptions.EventsType = typeof(OidcEvents);

            // Store the refresh_token.
            oidcOptions.SaveTokens = true;
        });

        return services;
    }
}