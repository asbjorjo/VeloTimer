using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Identity;
using VeloTime.Module.Notifications.Service;

namespace VeloTime.Module.Notifications.Api.Identity;

public sealed class NotificationReaderIdentityAccessor(IHttpContextAccessor accessor,
    IOptions<NotificationSessionAssertionOptions> options, TimeProvider clock) : INotificationReaderIdentityAccessor
{
    private NotificationReaderIdentity? current;
    private bool resolved;

    public NotificationReaderIdentity? GetCurrent()
    {
        if (resolved) return current;
        var context = accessor.HttpContext ?? throw new InvalidOperationException("HTTP context required.");
        if (context.User.Identity?.IsAuthenticated != true)
        {
            // Authenticate middleware must already have rejected any supplied invalid credential.
            if (context.Request.Headers.ContainsKey("Authorization"))
                throw new NotificationException(401, "notifications.authentication_required");
            resolved = true;
            return null;
        }
        var issuer = ExactClaim(context.User, "iss");
        var subject = ExactClaim(context.User, "sub");
        var roles = Roles(context.User);
        var assertion = context.Request.Headers[NotificationSessionAssertion.HeaderName];
        if (assertion.Count != 1 || string.IsNullOrWhiteSpace(assertion[0]))
            throw new NotificationException(403, NotificationErrorCodes.LoginSessionRequired);
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw new NotificationException(403, NotificationErrorCodes.InvalidIdentity);
        var config = options.Value;
        string login;
        try
        {
            login = NotificationSessionAssertion.Verify(assertion[0]!, config.PublicKeys, config.Issuer, config.Audience,
                issuer, subject, authorization[7..], clock.GetUtcNow(), config.MaximumLifetimeSeconds, config.ClockSkewSeconds);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or JsonException or ArgumentException)
        {
            throw new NotificationException(403, NotificationErrorCodes.LoginSessionRequired);
        }
        current = new(issuer, subject, roles, login);
        NotificationValidation.Identity(current);
        resolved = true;
        return current;
    }

    private static string ExactClaim(ClaimsPrincipal user, string type)
    {
        var claims = user.FindAll(type).ToArray();
        if (claims.Length != 1 || string.IsNullOrWhiteSpace(claims[0].Value))
            throw new NotificationException(403, NotificationErrorCodes.InvalidIdentity);
        return claims[0].Value;
    }

    private static string[] Roles(ClaimsPrincipal user)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var claim in user.Claims.Where(c => c.Type is "role" or "roles" || c.Type == ClaimTypes.Role))
            result.Add(claim.Value);
        try
        {
            foreach (var claim in user.FindAll("realm_access"))
            {
                using var doc = JsonDocument.Parse(claim.Value);
                AddRoles(doc.RootElement, result);
            }
            foreach (var claim in user.FindAll("resource_access"))
            {
                using var doc = JsonDocument.Parse(claim.Value);
                foreach (var client in doc.RootElement.EnumerateObject()) AddRoles(client.Value, result);
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            throw new NotificationException(403, NotificationErrorCodes.InvalidIdentity);
        }
        if (result.Any(string.IsNullOrWhiteSpace))
            throw new NotificationException(403, NotificationErrorCodes.InvalidIdentity);
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    private static void AddRoles(JsonElement element, HashSet<string> roles)
    {
        if (!element.TryGetProperty("roles", out var values)) return;
        foreach (var value in values.EnumerateArray())
            roles.Add(value.GetString() ?? throw new JsonException());
    }
}
