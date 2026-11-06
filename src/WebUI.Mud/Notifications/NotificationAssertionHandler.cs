using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using VeloTime.Module.Notifications.Interface.Identity;

namespace VeloTime.WebUI.Mud.Notifications;

public sealed class NotificationAssertionOptions
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string KeyId { get; set; } = "";
    public string PrivateKeyPem { get; set; } = "";
    public int LifetimeSeconds { get; set; } = 120;

    public bool IsValid()
    {
        try
        {
            using var key = RSA.Create();
            key.ImportFromPem(PrivateKeyPem);
            _ = NotificationSessionAssertion.Sign(key, KeyId, Issuer, Audience, "test", "test",
                "test", "test", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5), LifetimeSeconds);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or CryptographicException) { return false; }
    }
}

public sealed class NotificationAuthenticationException(int status, string code) : Exception
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

// Runs AFTER Duende's user-token handler, so the hash binds the actual forwarded token.
public sealed class NotificationAssertionHandler(
    IHttpContextAccessor accessor,
    IOptionsMonitor<OpenIdConnectOptions> oidc,
    IOptions<NotificationAssertionOptions> signing,
    IConfiguration configuration) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Remove(NotificationSessionAssertion.HeaderName);
        var http = accessor.HttpContext ?? throw new InvalidOperationException("Notifications require a BFF request.");
        var ticket = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!ticket.Succeeded)
        {
            request.Headers.Authorization = null;
            return await base.SendAsync(request, ct);
        }
        var token = request.Headers.Authorization is { Scheme: "Bearer" } auth ? auth.Parameter : null;
        if (string.IsNullOrWhiteSpace(token))
            throw new NotificationAuthenticationException(401, "notifications.authentication_required");
        var options = oidc.Get("VeloTimeOIDC");
        var trust = await options.ConfigurationManager!.GetConfigurationAsync(ct);
        var parameters = options.TokenValidationParameters.Clone();
        parameters.ValidIssuer = trust.Issuer;
        parameters.ValidIssuers = null;
        parameters.IssuerSigningKeys = trust.SigningKeys;
        parameters.ValidateIssuerSigningKey = true;
        parameters.ValidateIssuer = true;
        parameters.ValidateAudience = true;
        parameters.ValidAudience = configuration["OIDC_AUDIENCE"];
        parameters.ValidAudiences = null;
        parameters.ValidateLifetime = true;
        parameters.RequireSignedTokens = true;
        var validated = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);
        if (!validated.IsValid || validated.SecurityToken is not JsonWebToken jwt ||
            string.IsNullOrWhiteSpace(jwt.Issuer) || string.IsNullOrWhiteSpace(jwt.Subject))
            throw new NotificationAuthenticationException(401, "notifications.invalid_identity");
        if (!StringComparer.Ordinal.Equals(ticket.Principal!.FindFirst("iss")?.Value, jwt.Issuer) ||
            !StringComparer.Ordinal.Equals(ticket.Principal.FindFirst("sub")?.Value, jwt.Subject))
            throw new NotificationAuthenticationException(401, "notifications.invalid_identity");
        var properties = ticket.Properties!;
        if (!properties.Items.TryGetValue(NotificationSessionAssertion.TicketLoginIdProperty, out var login) ||
            string.IsNullOrWhiteSpace(login) || properties.ExpiresUtc is not { } expiry)
            throw new NotificationAuthenticationException(403, "notifications.login_session_required");
        using var key = RSA.Create();
        key.ImportFromPem(signing.Value.PrivateKeyPem);
        request.Headers.Add(NotificationSessionAssertion.HeaderName,
            NotificationSessionAssertion.Sign(key, signing.Value.KeyId, signing.Value.Issuer,
                signing.Value.Audience, jwt.Issuer, jwt.Subject, login, token,
                DateTimeOffset.UtcNow, expiry, signing.Value.LifetimeSeconds));
        return await base.SendAsync(request, ct);
    }
}
