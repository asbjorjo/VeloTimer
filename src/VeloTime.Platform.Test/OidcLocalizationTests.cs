using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using VeloTime.WebUI.Mud;

namespace VeloTime.Platform.Test;

public sealed class OidcLocalizationTests
{
    [Theory]
    [InlineData("en", "en")]
    [InlineData("nb-NO", "nb-NO")]
    public async Task RedirectToIdentityProvider_ForwardsSupportedUiLocale(string culture, string expected)
    {
        var context = CreateContext(culture);

        await new OidcEvents(PlatformServiceTests.TestProxy.Create<Duende.AccessTokenManagement.OpenIdConnect.IUserTokenStore>(
            (_, _) => null)).RedirectToIdentityProvider(context);

        Assert.Equal(expected, context.ProtocolMessage.GetParameter("ui_locales"));
    }

    [Theory]
    [InlineData("nn-NO")]
    [InlineData("fr-FR")]
    public async Task RedirectToIdentityProvider_DoesNotForwardUnsupportedUiLocale(string culture)
    {
        var context = CreateContext(culture);

        await new OidcEvents(PlatformServiceTests.TestProxy.Create<Duende.AccessTokenManagement.OpenIdConnect.IUserTokenStore>(
            (_, _) => null)).RedirectToIdentityProvider(context);

        Assert.Null(context.ProtocolMessage.GetParameter("ui_locales"));
    }

    private static RedirectContext CreateContext(string culture)
    {
        var http = new DefaultHttpContext();
        http.Features.Set<IRequestCultureFeature>(new RequestCultureFeature(
            new RequestCulture(CultureInfo.GetCultureInfo(culture)), provider: null));
        var options = new OpenIdConnectOptions
        {
            Configuration = new OpenIdConnectConfiguration
            {
                AuthorizationEndpoint = "https://identity.example/authorize"
            }
        };
        var scheme = new AuthenticationScheme("oidc", "oidc", typeof(OpenIdConnectHandler));
        var context = new RedirectContext(http, scheme, options, new AuthenticationProperties());
        typeof(RedirectContext).GetProperty(nameof(RedirectContext.ProtocolMessage))!
            .SetValue(context, new OpenIdConnectMessage());
        return context;
    }
}
