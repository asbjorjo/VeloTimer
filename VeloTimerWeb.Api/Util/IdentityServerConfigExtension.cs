using System;
using System.Collections.Generic;
using System.Linq;
using IdentityServer4.Configuration;
using IdentityServer4.EntityFramework.Interfaces;
using IdentityServer4.Hosting;
using IdentityServer4.Models;
using IdentityServer4.Stores;
using IdentityServer4.Validation;
using Microsoft.AspNetCore.ApiAuthorization.IdentityServer;
using Microsoft.AspNetCore.ApiAuthorization.IdentityServer.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VeloTimerWeb.Api.Util.ApiAuthorization;

namespace VeloTimerWeb.Api.Util
{
    public static class IdentityServerConfigExtension
    {
        public static IIdentityServerBuilder AddModifiedApiAuthorization<TUser, TContext>(
            this IIdentityServerBuilder builder,
            Action<ApiAuthorizationOptions> configure)
                where TUser : class
                where TContext : DbContext, IPersistedGrantDbContext
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            builder.AddAspNetIdentity<TUser>()
                .AddOperationalStore<TContext>()
                .ConfigureReplacedServices()
                .AddIdentityResources()
                .AddApiResources()
                .AddClients();
 
            builder.Services.Configure(configure);

            return builder;
        }

        internal static IIdentityServerBuilder ConfigureReplacedServices(this IIdentityServerBuilder builder)
        {
            builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<IdentityServerOptions>, AspNetConventionsConfigureOptions>());
            builder.Services.TryAddSingleton<IAbsoluteUrlFactory, AbsoluteUrlFactory>();
            builder.Services.AddSingleton<IRedirectUriValidator, RelativeRedirectUriValidator>();
            builder.Services.AddSingleton<IClientRequestParametersProvider, DefaultClientRequestParametersProvider>();
            ReplaceEndSessionEndpoint(builder);

            return builder;
        }

        private static void ReplaceEndSessionEndpoint(IIdentityServerBuilder builder)
        {
            // We don't have a better way to replace the end session endpoint as far as we know other than looking the descriptor up
            // on the container and replacing the instance. This is due to the fact that we chain on AddIdentityServer which configures the
            // list of endpoints by default.
            var endSessionEndpointDescriptor = builder.Services
                            .Single(s => s.ImplementationInstance is Endpoint e &&
                                    string.Equals(e.Name, "Endsession", StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals("/connect/endsession", e.Path, StringComparison.OrdinalIgnoreCase));

            builder.Services.Remove(endSessionEndpointDescriptor);
            builder.AddEndpoint<AutoRedirectEndSessionEndpoint>("EndSession", "/connect/endsession");
        }
    }
}
