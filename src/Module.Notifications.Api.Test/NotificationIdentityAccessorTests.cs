using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using VeloTime.Module.Notifications.Api.Identity;
using VeloTime.Module.Notifications.Interface;
using VeloTime.Module.Notifications.Service;

namespace VeloTime.Module.Notifications.Api.Test;

public sealed class NotificationIdentityAccessorTests
{
    [Fact]
    public void IdentityUsesExactIssuerSubjectRolesAndOptionalNonblankSid()
    {
        var (accessor, _) = Accessor(
        [
            new("iss", "https://issuer.example/realm"),
            new("sub", "exact-subject"),
            new("role", "RaceOfficial"),
            new("roles", "TimingVolunteer"),
            new("role", "RaceOfficial"),
            new("sid", "keycloak-sso-session")
        ]);

        var identity = accessor.GetCurrent();

        Assert.NotNull(identity);
        Assert.Equal("https://issuer.example/realm", identity.Issuer);
        Assert.Equal("exact-subject", identity.Subject);
        Assert.Equal("keycloak-sso-session", identity.LoginSessionId);
        Assert.Equal(["RaceOfficial", "TimingVolunteer"], identity.Roles);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingOrBlankSidLeavesAuthenticatedIdentityValid(string? sid)
    {
        var claims = new List<Claim>
        {
            new("iss", "https://issuer.example/realm"),
            new("sub", "exact-subject"),
            new("role", "RaceOfficial")
        };
        if (sid is not null) claims.Add(new("sid", sid));
        var (accessor, _) = Accessor(claims);

        var identity = accessor.GetCurrent();

        Assert.NotNull(identity);
        Assert.Equal("https://issuer.example/realm", identity.Issuer);
        Assert.Equal("exact-subject", identity.Subject);
        Assert.Null(identity.LoginSessionId);
        Assert.Equal(["RaceOfficial"], identity.Roles);
    }

    [Theory]
    [InlineData("iss")]
    [InlineData("sub")]
    [InlineData("sid")]
    public void DuplicateIdentityClaimsAreRejected(string duplicateType)
    {
        var claims = new List<Claim>
        {
            new("iss", "https://issuer.example/realm"),
            new("sub", "exact-subject"),
            new("sid", "keycloak-sso-session")
        };
        claims.Add(new(duplicateType, "second-value"));
        var (accessor, _) = Accessor(claims);

        var error = Assert.Throws<NotificationException>(() => accessor.GetCurrent());

        Assert.Equal(403, error.StatusCode);
        Assert.Equal(NotificationErrorCodes.InvalidIdentity, error.Code);
    }

    [Theory]
    [InlineData("iss")]
    [InlineData("sub")]
    public void MissingOrBlankRequiredIdentityClaimsAreRejected(string missingOrBlankType)
    {
        var claims = new List<Claim>
        {
            new("iss", "https://issuer.example/realm"),
            new("sub", "exact-subject")
        };
        claims.RemoveAll(claim => claim.Type == missingOrBlankType);
        claims.Add(new(missingOrBlankType, " "));
        var (accessor, _) = Accessor(claims);

        var error = Assert.Throws<NotificationException>(() => accessor.GetCurrent());

        Assert.Equal(403, error.StatusCode);
        Assert.Equal(NotificationErrorCodes.InvalidIdentity, error.Code);
    }

    [Fact]
    public void AnonymousRequestHasNoReaderIdentity()
    {
        var (accessor, _) = Accessor([]);

        Assert.Null(accessor.GetCurrent());
    }

    private static (NotificationReaderIdentityAccessor Accessor, DefaultHttpContext Context) Accessor(
        IEnumerable<Claim> claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims,
                claims.Any() ? "validated-jwt-test" : null))
        };
        return (new NotificationReaderIdentityAccessor(new HttpContextAccessor { HttpContext = context }), context);
    }
}
