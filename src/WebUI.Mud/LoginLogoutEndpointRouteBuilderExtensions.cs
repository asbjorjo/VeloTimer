using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using VeloTime.WebUI.Mud;

namespace Microsoft.AspNetCore.Routing;

internal static class LoginLogoutEndpointRouteBuilderExtensions
{
    internal static IEndpointConventionBuilder MapLoginAndLogout(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("");

        group.MapGet("/login", (HttpContext http, string? returnUrl) =>
                TypedResults.Challenge(GetAuthProperties(http, returnUrl)))
            .AllowAnonymous();

        group.MapGet("/changepass", (HttpContext http, string? returnUrl) => {
            var props = GetAuthProperties(http, returnUrl);
            props.SetParameter("kc_action", "UPDATE_PASSWORD");
            return TypedResults.Challenge(props);
        });

        group.MapGet("/addprovider/{provider}", (HttpContext http, string? returnUrl, string provider) => {
            var props = GetAuthProperties(http, returnUrl);
            props.SetParameter("kc_action", $"idp_link:{provider}");
            return TypedResults.Challenge(props);
        });

        // Sign out of the Cookie and OIDC handlers. If you do not sign out with the OIDC handler,
        // the user will automatically be signed back in the next time they visit a page that requires authentication
        // without being able to choose another account.
        group.MapPost("/logout", (HttpContext http, [FromForm] string? returnUrl) =>
            TypedResults.SignOut(GetAuthProperties(http, returnUrl),
                [CookieAuthenticationDefaults.AuthenticationScheme, "VeloTimeOIDC"]));

        return group;
    }

    // Prevent open redirects: only same-origin paths survive, anything else returns to the app root.
    internal static AuthenticationProperties GetAuthProperties(HttpContext http, string? returnUrl) =>
        new() { RedirectUri = LocalReturnUrl.Normalize(returnUrl, http) };
}
