using OpenIddict.Abstractions;
using OpeniddictServer.Data;
using System.Globalization;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace OpeniddictServer;

public class Worker : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public Worker(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync(cancellationToken);

        await RegisterApplicationsAsync(scope.ServiceProvider);
        await RegisterScopesAsync(scope.ServiceProvider);

        static async Task RegisterApplicationsAsync(IServiceProvider provider)
        {
            var manager = provider.GetRequiredService<IOpenIddictApplicationManager>();

            //var dd = await manager.FindByClientIdAsync("velotime-webui");

            //await manager.DeleteAsync(dd);

            // OIDC Code flow confidential client
            if (await manager.FindByClientIdAsync("velotime-webui") is null)
            {
                await manager.CreateAsync(new OpenIddictApplicationDescriptor
                {
                    ClientId = "velotime-webui",
                    ConsentType = ConsentTypes.Implicit,
                    DisplayName = "VeloTime WebUI",
                    DisplayNames =
                    {
                        [CultureInfo.GetCultureInfo("en-GB")] = "VeloTime web user interface"
                    },
                    PostLogoutRedirectUris =
                    {
                        new Uri("https://localhost:5001/signout-callback-oidc"),
                        new Uri("https://localhost:64265/signout-callback-oidc")
                    },
                    RedirectUris =
                    {
                        new Uri("https://localhost:5001/signin-oidc"),
                        new Uri("https://localhost:64265/signin-oidc"),
                    },
                    ClientSecret = "oidc-pkce-confidential_secret",
                    Permissions =
                    {
                        Permissions.Endpoints.Authorization,
                        Permissions.Endpoints.EndSession,
                        Permissions.Endpoints.Token,
                        Permissions.Endpoints.Revocation,
                        Permissions.GrantTypes.AuthorizationCode,
                        Permissions.GrantTypes.RefreshToken,
                        Permissions.ResponseTypes.Code,
                        Permissions.Scopes.Email,
                        Permissions.Scopes.Profile,
                        Permissions.Scopes.Roles,
                        Permissions.Prefixes.Scope + "veloTimeApi"
                    },
                    Requirements =
                    {
                        Requirements.Features.ProofKeyForCodeExchange
                    }
                });
            }

            if (await manager.FindByClientIdAsync("rs_veloTimeApi") == null)
            {
                await manager.CreateAsync(new OpenIddictApplicationDescriptor
                {
                    ClientId = "rs_veloTimeApi",
                    ClientSecret = "velotime-api-secret",
                    Permissions =
                    {
                        Permissions.Endpoints.Introspection
                    }
                });
            }
        }

        static async Task RegisterScopesAsync(IServiceProvider provider)
        {
            var manager = provider.GetRequiredService<IOpenIddictScopeManager>();

            if (await manager.FindByNameAsync("veloTimeApi") is null)
            {
                await manager.CreateAsync(new OpenIddictScopeDescriptor
                {
                    DisplayName = "VeloTime API access",
                    DisplayNames =
                    {
                        [CultureInfo.GetCultureInfo("en-GB")] = "VeloTime API access"
                    },
                    Name = "veloTimeApi",
                    Resources =
                    {
                        "rs_veloTimeApi"
                    }
                });
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
