using System.Security.Claims;
using System.Text.Json;
using Duende.AccessTokenManagement.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.WebUI.Mud.Services;

public class AuthDebugService(
    IHostEnvironment environment,
    IHttpContextAccessor httpContextAccessor,
    AuthenticationStateProvider authenticationStateProvider,
    IUserTokenManager tokenManager) : IAuthDebugService
{
    public async Task<AuthDebugView?> GetAccessTokenInfoAsync(CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment())
        {
            return null;
        }

        var user = await GetUserAsync();
        if (user.Identity?.IsAuthenticated != true)
        {
            return new AuthDebugView();
        }

        var result = await tokenManager.GetAccessTokenAsync(user, ct: cancellationToken);
        if (!result.Succeeded)
        {
            return new AuthDebugView();
        }

        return Describe(result.Token.AccessToken.ToString());
    }

    public static AuthDebugView Describe(string accessToken)
    {
        var handler = new JsonWebTokenHandler();
        if (!handler.CanReadToken(accessToken))
        {
            return new AuthDebugView { HasAccessToken = true };
        }

        // Decoded for display only; signature was validated by Keycloak/OIDC on issuance.
        var token = handler.ReadJsonWebToken(accessToken);
        return new AuthDebugView
        {
            HasAccessToken = true,
            Audiences = token.Audiences.ToArray(),
            RealmRoles = ReadRealmRoles(token),
            Issuer = token.Issuer,
            AuthorizedParty = token.TryGetPayloadValue<string>("azp", out var azp) ? azp : null,
            Scope = token.TryGetPayloadValue<string>("scope", out var scope) ? scope : null,
            ExpiresAt = token.ValidTo == DateTime.MinValue ? null : new DateTimeOffset(token.ValidTo, TimeSpan.Zero)
        };
    }

    private static string[] ReadRealmRoles(JsonWebToken token)
    {
        if (!token.TryGetPayloadValue<JsonElement>("realm_access", out var realmAccess) ||
            realmAccess.ValueKind != JsonValueKind.Object ||
            !realmAccess.TryGetProperty("roles", out var roles) ||
            roles.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return roles.EnumerateArray()
            .Where(role => role.ValueKind == JsonValueKind.String)
            .Select(role => role.GetString()!)
            .ToArray();
    }

    private async Task<ClaimsPrincipal> GetUserAsync()
    {
        // HttpContext is unavailable inside interactive server circuits.
        if (httpContextAccessor.HttpContext?.User is { Identity.IsAuthenticated: true } user)
        {
            return user;
        }

        return (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
    }
}
