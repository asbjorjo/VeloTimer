using Microsoft.AspNetCore.Authentication;
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

        services.AddOptions<CookieAuthenticationOptions>(cookieScheme).Configure<ITicketStore>((cookieOptions, ticketStore) =>
        {
            cookieOptions.EventsType = typeof(CookieEvents);
            cookieOptions.SessionStore = ticketStore;
        });

        services.AddOptions<OpenIdConnectOptions>(oidcScheme).Configure(oidcOptions =>
        {
            oidcOptions.GetClaimsFromUserInfoEndpoint = true;
            oidcOptions.MapInboundClaims = false;
            // Keep the validated id_token "iss" claim. The framework default
            // ClaimActions delete it before the ticket is persisted, and the
            // WebUI cache partitions personal data by validated issuer/subject.
            // This only removes that delete action; no action maps "iss" from
            // the userinfo response, so the persisted issuer stays the one the
            // token handler validated.
            oidcOptions.ClaimActions.Remove("iss");
            oidcOptions.ClaimActions.MapUniqueJsonKey("realm_access", "realm_access");

            // Request a refresh_token.
            oidcOptions.Scope.Add(OpenIdConnectScope.OfflineAccess);

            oidcOptions.EventsType = typeof(OidcEvents);

            // Store the refresh_token.
            oidcOptions.SaveTokens = true;
        });

        return services;
    }
}