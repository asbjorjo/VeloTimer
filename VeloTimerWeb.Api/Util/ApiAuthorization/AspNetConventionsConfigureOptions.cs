using Duende.IdentityServer.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace VeloTimerWeb.Api.Util.ApiAuthorization
{
    internal class AspNetConventionsConfigureOptions : IConfigureOptions<IdentityServerOptions>
    {
        public void Configure(IdentityServerOptions options)
        {
            options.Events.RaiseErrorEvents = true;
            options.Events.RaiseInformationEvents = true;
            options.Events.RaiseFailureEvents = true;
            options.Events.RaiseSuccessEvents = true;
            options.Authentication.CookieAuthenticationScheme = IdentityConstants.ApplicationScheme;
            options.UserInteraction.ErrorUrl = "/Home";
        }
    }
}
