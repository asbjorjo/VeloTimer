using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string AuthenticationSchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.ContainsKey("X-Test-Anonymous"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Request.Headers["X-Test-Subject"].FirstOrDefault() ?? "test-user"),
            new(ClaimTypes.Name, "Test User")
        };
        if (Request.Headers.TryGetValue("X-Test-Scope", out var scope) && scope == "Statistics.Write")
        {
            claims.Add(new Claim("scope", "Statistics.Write"));
        }

        if (Request.Headers.TryGetValue("X-Test-Role", out var role))
        {
            claims.Add(new Claim("realm_access", System.Text.Json.JsonSerializer.Serialize(new
            {
                roles = role.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            })));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationSchemeName));
        var ticket = new AuthenticationTicket(principal, AuthenticationSchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
