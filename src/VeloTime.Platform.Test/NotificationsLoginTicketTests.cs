using System.Security.Claims;
using Duende.AccessTokenManagement.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using VeloTime.Module.Notifications.Interface.Identity;
using VeloTime.WebUI.Mud;

namespace VeloTime.Platform.Test;

public sealed class NotificationsLoginTicketTests
{
    [Fact]
    public async Task FreshOidcLoginsReplaceIdWhileTicketRenewalRetainsItWithoutClaimExposure()
    {
        var store = PlatformServiceTests.TestProxy.Create<IUserTokenStore>((_, _) => null);
        var events = new OidcEvents(store);
        var properties = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) };
        properties.Items[NotificationSessionAssertion.TicketLoginIdProperty] = "old-login";
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "rider")], "Cookies"));
        var scheme = new AuthenticationScheme("oidc", null, typeof(OpenIdConnectHandler));
        var context = new TokenValidatedContext(new DefaultHttpContext(), scheme, new OpenIdConnectOptions(), principal, properties)
        {
            TokenEndpointResponse = new OpenIdConnectMessage
            {
                AccessToken = "access", TokenType = "Bearer", RefreshToken = "refresh", Scope = "openid",
                IdToken = "id-token", ExpiresIn = "3600"
            }
        };
        await events.TokenValidated(context);
        var first = properties.Items[NotificationSessionAssertion.TicketLoginIdProperty];
        Assert.NotEqual("old-login", first); Assert.Equal(64, first!.Length);
        var renewed = TicketSerializer.Default.Deserialize(TicketSerializer.Default.Serialize(new(principal, properties, "Cookies")));
        renewed.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2);
        Assert.Equal(first, renewed.Properties.Items[NotificationSessionAssertion.TicketLoginIdProperty]);
        Assert.DoesNotContain(principal.Claims, claim => claim.Value == first);
        await events.TokenValidated(context);
        Assert.NotEqual(first, properties.Items[NotificationSessionAssertion.TicketLoginIdProperty]);
    }
}
