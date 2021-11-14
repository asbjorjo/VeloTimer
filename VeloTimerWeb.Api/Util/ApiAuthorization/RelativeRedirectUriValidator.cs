using IdentityServer4.Models;
using IdentityServer4.Validation;
using Microsoft.AspNetCore.ApiAuthorization.IdentityServer;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VeloTimerWeb.Api.Util.ApiAuthorization
{
    internal class RelativeRedirectUriValidator : StrictRedirectUriValidator
    {
        public RelativeRedirectUriValidator(IAbsoluteUrlFactory absoluteUrlFactory)
        {
            if (absoluteUrlFactory == null)
            {
                throw new ArgumentNullException(nameof(absoluteUrlFactory));
            }

            AbsoluteUrlFactory = absoluteUrlFactory;
        }

        public IAbsoluteUrlFactory AbsoluteUrlFactory { get; }

        public override Task<bool> IsRedirectUriValidAsync(string requestedUri, IdentityServer4.Models.Client client)
        {
            if (IsLocalSPA(client))
            {
                return ValidateRelativeUris(requestedUri, client.RedirectUris);
            }
            else
            {
                return base.IsRedirectUriValidAsync(requestedUri, client);
            }
        }

        public override Task<bool> IsPostLogoutRedirectUriValidAsync(string requestedUri, IdentityServer4.Models.Client client)
        {
            if (IsLocalSPA(client))
            {
                return ValidateRelativeUris(requestedUri, client.PostLogoutRedirectUris);
            }
            else
            {
                return base.IsPostLogoutRedirectUriValidAsync(requestedUri, client);
            }
        }

        private static bool IsLocalSPA(IdentityServer4.Models.Client client) =>
            client.Properties.TryGetValue(ApplicationProfilesPropertyNames.Profile, out var clientType) &&
            ApplicationProfiles.IdentityServerSPA == clientType;

        private Task<bool> ValidateRelativeUris(string requestedUri, IEnumerable<string> clientUris)
        {
            foreach (var url in clientUris)
            {
                if (Uri.IsWellFormedUriString(url, UriKind.Relative))
                {
                    var newUri = AbsoluteUrlFactory.GetAbsoluteUrl(url);
                    if (string.Equals(newUri, requestedUri, StringComparison.Ordinal))
                    {
                        return Task.FromResult(true);
                    }
                }
            }

            return Task.FromResult(false);
        }
    }
}
