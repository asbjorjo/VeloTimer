using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using VeloTime.Module.Notifications.Api.Identity;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Interface.Identity;
using VeloTime.Module.Notifications.Service;

namespace VeloTime.Module.Notifications.Api.Test;

public sealed class NotificationIdentityAccessorTests
{
    [Fact]
    public void TrulyAnonymousRequestHasNoReaderIdentity_ButSuppliedCredentialCannotDowngrade()
    {
        using var key = RSA.Create(2048);
        var (accessor, _) = Accessor(key);
        Assert.Null(accessor.GetCurrent());

        var (_, context) = Accessor(key);
        context.Request.Headers["Authorization"] = "Bearer invalid";
        var credentialAccessor = new NotificationReaderIdentityAccessor(new HttpContextAccessor { HttpContext = context },
            Options.Create(AssertionOptions(key)), new TestClock(DateTimeOffset.UtcNow));
        var error = Assert.Throws<NotificationException>(() => credentialAccessor.GetCurrent());
        Assert.Equal(401, error.StatusCode);
        Assert.Equal(NotificationErrorCodes.AuthenticationRequired, error.Code);
    }

    [Fact]
    public void AuthenticatedRequestRequiresExactSignedApplicationLoginAssertion()
    {
        using var key = RSA.Create(2048);
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var token = "validated bearer access token";
        var login = NotificationSessionAssertion.NewApplicationLoginId();
        var (accessor, context) = Accessor(key, "https://issuer.example", "subject", "role-1", now);
        context.Request.Headers["Authorization"] = "Bearer " + token;

        var missing = Assert.Throws<NotificationException>(() => accessor.GetCurrent());
        Assert.Equal(403, missing.StatusCode);
        Assert.Equal(NotificationErrorCodes.LoginSessionRequired, missing.Code);

        context.Request.Headers[NotificationSessionAssertion.HeaderName] = NotificationSessionAssertion.Sign(
            key, "kid-1", "webui", "notifications-api", "https://issuer.example", "subject",
            login, token, now, now.AddMinutes(2));
        var identity = accessor.GetCurrent();
        Assert.NotNull(identity);
        Assert.Equal("https://issuer.example", identity.Issuer);
        Assert.Equal("subject", identity.Subject);
        Assert.Equal(["role-1"], identity.Roles);
        Assert.Equal(login, identity.LoginSessionId);
    }

    [Fact]
    public void TamperedAndExpiredAssertionsFailExplicitly()
    {
        using var key = RSA.Create(2048);
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var token = "validated bearer access token";
        var (accessor, context) = Accessor(key, "https://issuer.example", "subject", now: now);
        context.Request.Headers["Authorization"] = "Bearer " + token;
        var assertion = NotificationSessionAssertion.Sign(key, "kid-1", "webui", "notifications-api",
            "https://issuer.example", "subject", "login-1", token, now, now.AddMinutes(2));

        context.Request.Headers[NotificationSessionAssertion.HeaderName] = assertion + "tampered";
        var invalid = Assert.Throws<NotificationException>(() => accessor.GetCurrent());
        Assert.Equal(NotificationErrorCodes.LoginSessionRequired, invalid.Code);

        context.Request.Headers[NotificationSessionAssertion.HeaderName] = assertion;
        var expiredAccessor = new NotificationReaderIdentityAccessor(new HttpContextAccessor { HttpContext = context },
            Options.Create(AssertionOptions(key)), new TestClock(now.AddMinutes(2)));
        var expired = Assert.Throws<NotificationException>(() => expiredAccessor.GetCurrent());
        Assert.Equal(403, expired.StatusCode);
        Assert.Equal(NotificationErrorCodes.LoginSessionRequired, expired.Code);
    }

    [Fact]
    public void MissingOrDuplicateValidatedIdentityClaimsAreNotAccepted()
    {
        using var key = RSA.Create(2048);
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var (accessor, context) = Accessor(key, now: now);
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "subject"), new Claim("sub", "another-subject")], "test"));
        context.Request.Headers["Authorization"] = "Bearer token";
        context.Request.Headers[NotificationSessionAssertion.HeaderName] = "not-used";
        var error = Assert.Throws<NotificationException>(() => accessor.GetCurrent());
        Assert.Equal(403, error.StatusCode);
        Assert.Equal(NotificationErrorCodes.InvalidIdentity, error.Code);
    }

    private static (NotificationReaderIdentityAccessor Accessor, DefaultHttpContext Context) Accessor(
        RSA key, string? issuer = null, string? subject = null, string? role = null, DateTimeOffset? now = null)
    {
        var context = new DefaultHttpContext();
        if (issuer is not null || subject is not null || role is not null)
        {
            var claims = new List<Claim>();
            if (issuer is not null) claims.Add(new("iss", issuer));
            if (subject is not null) claims.Add(new("sub", subject));
            if (role is not null) claims.Add(new("role", role));
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "validated-test"));
        }

        var accessor = new NotificationReaderIdentityAccessor(new HttpContextAccessor { HttpContext = context },
            Options.Create(AssertionOptions(key)), new TestClock(now ?? DateTimeOffset.UtcNow));
        return (accessor, context);
    }

    private static NotificationSessionAssertionOptions AssertionOptions(RSA key) => new()
    {
        Issuer = "webui",
        Audience = "notifications-api",
        PublicKeys = new Dictionary<string, string> { ["kid-1"] = key.ExportSubjectPublicKeyInfoPem() },
        MaximumLifetimeSeconds = 120,
        ClockSkewSeconds = 0
    };

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
