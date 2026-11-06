using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Identity;
using VeloTime.Module.Notifications.Service;

namespace VeloTime.Module.Notifications.Api.Identity;

public sealed class NotificationReaderIdentityAccessor(IHttpContextAccessor accessor) : INotificationReaderIdentityAccessor
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
        current = new(issuer, subject, roles, OptionalExactClaim(context.User, "sid"));
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

    private static string? OptionalExactClaim(ClaimsPrincipal user, string type)
    {
        var claims = user.FindAll(type).ToArray();
        if (claims.Length > 1)
            throw new NotificationException(403, NotificationErrorCodes.InvalidIdentity);
        return claims.Length == 1 && !string.IsNullOrWhiteSpace(claims[0].Value) ? claims[0].Value : null;
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
