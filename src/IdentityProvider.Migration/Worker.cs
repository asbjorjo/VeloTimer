using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpeniddictServer.Data;
using System.Globalization;
using static OpenIddict.Abstractions.OpenIddictConstants;

public class Worker(
    IServiceProvider serviceProvider,
    IHostApplicationLifetime hostApplicationLifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await context.Database.MigrateAsync(cancellationToken);

        await RegisterApplicationsAsync(scope.ServiceProvider);
        await RegisterScopesAsync(scope.ServiceProvider);

        hostApplicationLifetime.StopApplication();
    }

    private static async Task RegisterApplicationsAsync(IServiceProvider provider)
    {
        var manager = provider.GetRequiredService<IOpenIddictApplicationManager>();

        var dd = await manager.FindByClientIdAsync("velotime-webui");

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
                        new Uri("https://localhost:7144/signout-callback-oidc"),
                        new Uri("https://localhost:64265/signout-callback-oidc")
                    },
                RedirectUris =
                    {
                        new Uri("https://localhost:5001/signin-oidc"),
                        new Uri("https://localhost:7144/signin-oidc"),
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
    private static async Task RegisterScopesAsync(IServiceProvider provider)
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
