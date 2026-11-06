using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VeloTime.Module.Timing.Api.Test;

public sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TimingTest";
    public static readonly Guid DefaultOwnerId = Guid.Parse("00000000-0000-0000-0000-000000000123");

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.ContainsKey("X-Test-Anonymous")) return Task.FromResult(AuthenticateResult.NoResult());
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", Request.Headers["X-Test-Subject"].FirstOrDefault() ?? DefaultOwnerId.ToString()),
            new Claim("realm_access", System.Text.Json.JsonSerializer.Serialize(new
            {
                roles = new[] { Request.Headers["X-Test-Role"].FirstOrDefault() ?? "timing-admin" }
            }))
        ], SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}