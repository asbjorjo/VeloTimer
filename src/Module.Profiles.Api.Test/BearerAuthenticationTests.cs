using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace VeloTime.Module.Profiles.Api.Test;

public sealed class BearerAuthenticationTests
{
    private const string Issuer = "https://issuer.example/realm";
    private static readonly SymmetricSecurityKey Key = new(
        "profiles-contract-test-signing-key-not-for-production-2026"u8.ToArray());

    [Theory]
    [InlineData("valid", HttpStatusCode.OK)]
    [InlineData("missing-subject", HttpStatusCode.Forbidden)]
    [InlineData("wrong-issuer", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-audience", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-signature", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    public async Task ValidatedJwt_IsRequired_AndRawSubjectIsNotMappedAway(string scenario, HttpStatusCode expected)
    {
        using var root = new ProfilesApiFactory { UseRealBearer = true };
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                // Supply local signing metadata, not a bypass for token validation.
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                    new OpenIdConnectConfiguration { Issuer = Issuer, SigningKeys = { Key } });
                options.TokenValidationParameters.ValidIssuer = Issuer;
                options.TokenValidationParameters.ValidAudience = "https://build.invalid";
                options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
            })));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var options = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.False(options.MapInboundClaims);
        var signingKey = scenario == "wrong-signature"
            ? new SymmetricSecurityKey("different-contract-signing-key-that-is-at-least-32-bytes"u8.ToArray())
            : Key;
        var token = new JwtSecurityToken(
            issuer: scenario == "wrong-issuer" ? "https://untrusted.example" : Issuer,
            audience: scenario == "wrong-audience" ? "https://other.invalid" : "https://build.invalid",
            claims: scenario == "missing-subject" ? [] : [new Claim("sub", "CaseSensitiveSubject")],
            notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: DateTime.UtcNow.AddMinutes(scenario == "expired" ? -1 : 5),
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            new JwtSecurityTokenHandler().WriteToken(token));
        using var response = await client.GetAsync("/api/profiles/me/preferences");
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            var call = Assert.Single(root.Preferences.Calls);
            Assert.Equal(Issuer, call.Issuer);
            Assert.Equal("CaseSensitiveSubject", call.Subject);
        }
        else
        {
            Assert.Empty(root.Preferences.Calls);
        }
    }
}
