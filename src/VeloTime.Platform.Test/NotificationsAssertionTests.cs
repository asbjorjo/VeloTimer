using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using VeloTime.Module.Notifications.Interface.Identity;
using VeloTime.WebUI.Mud.Notifications;

namespace VeloTime.Platform.Test;

public sealed class NotificationsAssertionTests
{
    [Fact]
    public async Task AssertionBindsActualValidatedTokenIdentityAndTicketExpiry()
    {
        using var fixture = new Fixture();
        fixture.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(60);
        fixture.Properties.Items[NotificationSessionAssertion.TicketLoginIdProperty] = "trusted-login";
        await fixture.Send();
        var login = NotificationSessionAssertion.Verify(fixture.Captured!,
            new Dictionary<string, string> { ["ui"] = fixture.Signer.ExportRSAPublicKeyPem() },
            "webui", "notifications", Fixture.Issuer, "subject", fixture.AccessToken, DateTimeOffset.UtcNow);
        Assert.Equal("trusted-login", login);
        Assert.Throws<CryptographicException>(() => NotificationSessionAssertion.Verify(fixture.Captured!,
            new Dictionary<string, string> { ["ui"] = fixture.Signer.ExportRSAPublicKeyPem() },
            "webui", "notifications", Fixture.Issuer, "subject", "different-token", DateTimeOffset.UtcNow));
        var afterTicket = fixture.Properties.ExpiresUtc!.Value.AddSeconds(1);
        Assert.Throws<CryptographicException>(() => NotificationSessionAssertion.Verify(fixture.Captured!,
            new Dictionary<string, string> { ["ui"] = fixture.Signer.ExportRSAPublicKeyPem() },
            "webui", "notifications", Fixture.Issuer, "subject", fixture.AccessToken, afterTicket));
    }

    [Fact]
    public async Task MissingTrustedLoginFailsRatherThanInventingOrFallingBack()
    {
        using var fixture = new Fixture();
        var error = await Assert.ThrowsAsync<NotificationAuthenticationException>(() => fixture.Send());
        Assert.Equal(403, error.Status);
        Assert.Null(fixture.Captured);
    }

    [Theory]
    [InlineData("bad-token")]
    [InlineData(null)]
    public async Task InvalidOrMissingForwardedTokenFails(string? token)
    {
        using var fixture = new Fixture();
        fixture.Properties.Items[NotificationSessionAssertion.TicketLoginIdProperty] = "trusted";
        var error = await Assert.ThrowsAsync<NotificationAuthenticationException>(() => fixture.Send(token, replace: true));
        Assert.Equal(401, error.Status);
    }

    [Fact]
    public async Task TokenMustMatchProtectedTicketIssuerAndSubject()
    {
        using var fixture = new Fixture();
        fixture.Properties.Items[NotificationSessionAssertion.TicketLoginIdProperty] = "trusted";
        fixture.Principal = new(new ClaimsIdentity([new Claim("iss", Fixture.Issuer), new Claim("sub", "another-user")], "Cookies"));
        var error = await Assert.ThrowsAsync<NotificationAuthenticationException>(() => fixture.Send());
        Assert.Equal(401, error.Status);
    }

    [Fact]
    public async Task AnonymousForwardsNeitherBearerNorBrowserAssertion()
    {
        using var fixture = new Fixture();
        fixture.Authenticated = false;
        await fixture.Send();
        Assert.Null(fixture.Captured);
        Assert.Null(fixture.CapturedBearer);
    }

    private sealed class Fixture : IDisposable
    {
        public const string Issuer = "https://identity.invalid/realms/velotime";
        public RSA Signer { get; } = RSA.Create(2048);
        private RSA JwtKey { get; } = RSA.Create(2048);
        public AuthenticationProperties Properties { get; } = new() { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) };
        public ClaimsPrincipal Principal { get; set; } = new(new ClaimsIdentity([new Claim("iss", Issuer), new Claim("sub", "subject")], "Cookies"));
        public bool Authenticated { get; set; } = true;
        public string AccessToken { get; }
        public string? Captured { get; private set; }
        public string? CapturedBearer { get; private set; }
        private readonly ServiceProvider _services;
        public Fixture()
        {
            AccessToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Issuer, Audience = "api", Subject = new ClaimsIdentity([new Claim("sub", "subject")]),
                Expires = DateTime.UtcNow.AddMinutes(5),
                SigningCredentials = new(new RsaSecurityKey(JwtKey), SecurityAlgorithms.RsaSha256)
            });
            var authentication = PlatformServiceTests.TestProxy.Create<IAuthenticationService>((method, _) =>
            {
                Assert.Equal(nameof(IAuthenticationService.AuthenticateAsync), method.Name);
                return Authenticated ? AuthenticateResult.Success(new(Principal, Properties, "Cookies")) : AuthenticateResult.NoResult();
            });
            _services = new ServiceCollection().AddSingleton(authentication).BuildServiceProvider();
        }
        public async Task Send(string? token = null, bool replace = false)
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            configuration.SigningKeys.Add(new RsaSecurityKey(JwtKey));
            var options = new OpenIdConnectOptions
            {
                ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration)
            };
            var monitor = PlatformServiceTests.TestProxy.Create<IOptionsMonitor<OpenIdConnectOptions>>((_, _) => options);
            var signing = Options.Create(new NotificationAssertionOptions
            {
                Issuer = "webui", Audience = "notifications", KeyId = "ui", PrivateKeyPem = Signer.ExportRSAPrivateKeyPem()
            });
            using var handler = new NotificationAssertionHandler(new HttpContextAccessor
                { HttpContext = new DefaultHttpContext { RequestServices = _services } }, monitor, signing,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["OIDC_AUDIENCE"] = "api" }).Build())
            {
                InnerHandler = new Capture(request =>
                {
                    Captured = request.Headers.TryGetValues(NotificationSessionAssertion.HeaderName, out var values) ? Assert.Single(values) : null;
                    CapturedBearer = request.Headers.Authorization?.Parameter;
                })
            };
            using var client = new HttpClient(handler);
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.invalid/api/notifications/active");
            request.Headers.Add(NotificationSessionAssertion.HeaderName, "browser-forged");
            var actual = replace ? token : AccessToken;
            if (actual is not null) request.Headers.Authorization = new("Bearer", actual);
            using var response = await client.SendAsync(request);
        }
        public void Dispose() { Signer.Dispose(); JwtKey.Dispose(); _services.Dispose(); }
        private sealed class Capture(Action<HttpRequestMessage> capture) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            { capture(request); return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)); }
        }
    }
}
