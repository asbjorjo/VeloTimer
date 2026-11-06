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
    public async Task UnknownKeyRefreshesOidcConfigurationAndAcceptsTokenAfterRefresh()
    {
        using var fixture = new Fixture();
        fixture.Properties.Items[NotificationSessionAssertion.TicketLoginIdProperty] = "trusted";
        using var rotatedKey = RSA.Create(2048);
        var oldConfiguration = Fixture.Configuration(fixture.JwtKey, "old");
        var rotatedConfiguration = Fixture.Configuration(rotatedKey, "rotated");
        var retriever = new RotatingConfigurationRetriever(oldConfiguration, rotatedConfiguration);
        var manager = new ConfigurationManager<OpenIdConnectConfiguration>(
            "https://identity.invalid/.well-known/openid-configuration", retriever);
        manager.RefreshInterval = TimeSpan.FromSeconds(1);
        fixture.ConfigurationManager = manager;

        // Prime old metadata and let the manager's refresh throttle elapse.
        Assert.Equal("old", Assert.Single((await manager.GetConfigurationAsync(CancellationToken.None)).SigningKeys).KeyId);
        await Task.Delay(TimeSpan.FromSeconds(1.1));
        var token = Fixture.CreateToken(rotatedKey, "rotated");
        var firstAttempt = await Assert.ThrowsAsync<NotificationAuthenticationException>(
            () => fixture.Send(token, replace: true));
        Assert.Equal(401, firstAttempt.Status);

        // The metadata manager refreshes asynchronously; do not accept the token
        // until its new signing key is actually present in trusted configuration.
        OpenIdConnectConfiguration refreshed;
        for (var attempt = 0; ; attempt++)
        {
            refreshed = await manager.GetConfigurationAsync(CancellationToken.None);
            if (refreshed.SigningKeys.Any(key => key.KeyId == "rotated")) break;
            Assert.True(attempt < 20, "OIDC configuration did not refresh to the rotated signing key.");
            await Task.Delay(50);
        }
        Assert.True(retriever.Calls >= 2);

        await fixture.Send(token, replace: true);

        Assert.Equal(token, fixture.CapturedBearer);
        Assert.Equal("trusted", NotificationSessionAssertion.Verify(fixture.Captured!,
            new Dictionary<string, string> { ["ui"] = fixture.Signer.ExportRSAPublicKeyPem() },
            "webui", "notifications", Fixture.Issuer, "subject", token, DateTimeOffset.UtcNow));

        // A forged signature with the now-known key id must still fail validation.
        using var attackerKey = RSA.Create(2048);
        var invalidToken = Fixture.CreateToken(attackerKey, "rotated");
        var error = await Assert.ThrowsAsync<NotificationAuthenticationException>(
            () => fixture.Send(invalidToken, replace: true));
        Assert.Equal(401, error.Status);
        Assert.Null(fixture.Captured);
    }

    [Fact]
    public async Task NonRefreshableOidcConfigurationManagerFailsExplicitly()
    {
        using var fixture = new Fixture();
        fixture.ConfigurationManager = new NonRefreshableConfigurationManager(
            Fixture.Configuration(fixture.JwtKey, "old"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Send());
        Assert.Contains("refresh-capable", error.Message);
        Assert.Null(fixture.Captured);
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
        public RSA JwtKey { get; } = RSA.Create(2048);
        public AuthenticationProperties Properties { get; } = new() { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) };
        public ClaimsPrincipal Principal { get; set; } = new(new ClaimsIdentity([new Claim("iss", Issuer), new Claim("sub", "subject")], "Cookies"));
        public bool Authenticated { get; set; } = true;
        public string AccessToken { get; }
        public IConfigurationManager<OpenIdConnectConfiguration>? ConfigurationManager { get; set; }
        public string? Captured { get; private set; }
        public string? CapturedBearer { get; private set; }
        private readonly ServiceProvider _services;
        public Fixture()
        {
            AccessToken = CreateToken(JwtKey, "old");
            var authentication = PlatformServiceTests.TestProxy.Create<IAuthenticationService>((method, _) =>
            {
                Assert.Equal(nameof(IAuthenticationService.AuthenticateAsync), method.Name);
                return Authenticated ? AuthenticateResult.Success(new(Principal, Properties, "Cookies")) : AuthenticateResult.NoResult();
            });
            _services = new ServiceCollection().AddSingleton(authentication).BuildServiceProvider();
        }
        public async Task Send(string? token = null, bool replace = false)
        {
            var options = new OpenIdConnectOptions
            {
                ConfigurationManager = ConfigurationManager ?? new ConfigurationManager<OpenIdConnectConfiguration>(
                    "https://identity.invalid/.well-known/openid-configuration",
                    new RotatingConfigurationRetriever(Configuration(JwtKey, "old")))
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
            Captured = null; CapturedBearer = null;
            request.Headers.Add(NotificationSessionAssertion.HeaderName, "browser-forged");
            var actual = replace ? token : AccessToken;
            if (actual is not null) request.Headers.Authorization = new("Bearer", actual);
            using var response = await client.SendAsync(request);
        }
        public static OpenIdConnectConfiguration Configuration(RSA key, string keyId) => new()
        {
            Issuer = Issuer,
            SigningKeys = { new RsaSecurityKey(key) { KeyId = keyId } }
        };
        public static string CreateToken(RSA key, string keyId) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer, Audience = "api", Subject = new ClaimsIdentity([new Claim("sub", "subject")]),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new(new RsaSecurityKey(key) { KeyId = keyId }, SecurityAlgorithms.RsaSha256)
        });
        public void Dispose() { Signer.Dispose(); JwtKey.Dispose(); _services.Dispose(); }
        private sealed class Capture(Action<HttpRequestMessage> capture) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            { capture(request); return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)); }
        }
    }

    private sealed class RotatingConfigurationRetriever(
        OpenIdConnectConfiguration initial,
        OpenIdConnectConfiguration? rotated = null) : IConfigurationRetriever<OpenIdConnectConfiguration>
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(
            string address, IDocumentRetriever documentRetriever, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var calls = Interlocked.Increment(ref _calls);
            return Task.FromResult(calls == 1 || rotated is null ? initial : rotated);
        }
    }

    private sealed class NonRefreshableConfigurationManager(OpenIdConnectConfiguration configuration)
        : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancellationToken) =>
            Task.FromResult(configuration);
        public void RequestRefresh() { }
    }
}
