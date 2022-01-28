using Azure.Identity;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.ApiAuthorization.IdentityServer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography.X509Certificates;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models.Identity;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api.Configuration
{
    public static class Identity
    {
        public static IServiceCollection AddIdentityServices(this IServiceCollection services,
                                                             IConfiguration configuration,
                                                             IHostEnvironment environment)
        {
            var azureId = new DefaultAzureCredential();

            services.AddDbContext<VeloIdentityDbContext>(options =>
            {
                options.UseSqlServer(configuration.GetConnectionString("Azure"));
            });

            var dpBuilder = services.AddDataProtection();
            dpBuilder.PersistKeysToDbContext<VeloIdentityDbContext>();

            if (environment.IsDevelopment())
            {
                dpBuilder.SetApplicationName("veloti.me-development");
                dpBuilder.SetDefaultKeyLifetime(TimeSpan.FromDays(7));
            }
            else
            {
                dpBuilder.SetApplicationName("veloti.me");
                dpBuilder.ProtectKeysWithAzureKeyVault(new Uri(new Uri(configuration["VAULT"]), "keys/dataprotection"), azureId);
            }

            services.AddDefaultIdentity<User>(options =>
            {
            })
                .AddUserManager<VeloTimeUserManager<User>>()
                .AddRoles<Role>()
                .AddEntityFrameworkStores<VeloIdentityDbContext>();

            if (environment.IsProduction())
            {
                services.Configure<JwtBearerOptions>(
                    IdentityServerJwtConstants.IdentityServerJwtBearerScheme,
                    options =>
                    {
                        options.Authority = configuration["VELOTIMER_API_URL"];
                        options.TokenValidationParameters.ValidIssuers = new[]
                        {
                            "https://veloti.me",
                            "https://www.veloti.me",
                            "https://velotime.azurewebsites.net",
                            "https://velotime-github-ci.azurewebsites.net",
                            "https://velotime-noe.azurewebsites.net",
                            "https://velotime-noe-github-ci.azurewebsites.net"
                        };
                    });
            }
            else if (environment.IsStaging())
            {
                services.Configure<JwtBearerOptions>(
                    IdentityServerJwtConstants.IdentityServerJwtBearerScheme,
                    options =>
                    {
                        options.Authority = configuration["VELOTIMER_API_URL"];
                        options.TokenValidationParameters.ValidIssuers = new[]
                        {
                            "https://velotime-dev.azurewebsites.net"
                        };
                    });
            }


            var identitybuilder = services.AddIdentityServer(options =>
            {
                options.Events.RaiseErrorEvents = true;
                options.Events.RaiseInformationEvents = true;
                options.Events.RaiseFailureEvents = true;
                options.Events.RaiseSuccessEvents = true;

                // see https://identityserver4.readthedocs.io/en/latest/topics/resources.html
                options.EmitStaticAudienceClaim = true;

                options.LicenseKey = configuration["LicenseKeys:IdentityServer"];

                //if (Environment.IsProduction())
                //    options.IssuerUri = "https://veloti.me";
            })
                .AddModifiedApiAuthorization<User, VeloIdentityDbContext>(options =>
                {
                    options.Clients.Add(new Duende.IdentityServer.Models.Client
                    {
                        ClientId = "WebApi.Loader",
                        AllowedGrantTypes = { GrantType.ClientCredentials },
                        ClientSecrets = { new Duende.IdentityServer.Models.Secret("secret".Sha256()) },
                        AllowedScopes = { "VeloTimer.Api", "VeloTimer.ApiAPI" }
                    });
                });

            if (environment.IsDevelopment())
            {
                Console.WriteLine("Using development key.");
                identitybuilder.AddDeveloperSigningCredential();
            }
            else
            {
                string key;

                if (environment.IsStaging())
                {
                    key = configuration["tokensiging"];
                }
                else
                {
                    key = configuration["tokensigning"];
                }

                var pfxBytes = Convert.FromBase64String(key);

                //// Create the certificate.
                var cert = new X509Certificate2(pfxBytes);

                identitybuilder
                    .AddSigningCredential(cert);
            }

            services.AddTransient<IProfileService, ProfileService>();
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Remove("role");

            services.AddAuthentication()
                .AddIdentityServerJwt()

                .AddStrava(options =>
                {
                    options.ClientId = configuration["Authentication:Strava:ClientId"];
                    options.ClientSecret = configuration["Authentication:Strava:ClientSecret"];
                })
                .AddFacebook(options =>
                {
                    options.AppId = configuration["Authentication:Facebook:AppId"];
                    options.AppSecret = configuration["Authentication:Facebook:AppSecret"];
                })
                .AddGoogle(options =>
                {
                    options.ClientId = configuration["Authentication:Google:ClientId"];
                    options.ClientSecret = configuration["Authentication:Google:ClientSecret"];
                });

            services.AddAuthorization();


            return services;
        }

        public static IApplicationBuilder UseIdentityServices(this IApplicationBuilder builder)
        {
            builder.UseIdentityServer();
            builder.UseAuthentication();
            builder.UseAuthorization();

            return builder;
        }
    }
}
