using Duende.AccessTokenManagement;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using System.Text.Json;
using VeloTime.Module.Common;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace Microsoft.Extensions.Hosting;

public static class HostingExtensions
{

    public static IHostApplicationBuilder AddModuleAuthentication(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        var env = builder.Environment;

        var audience = configuration["OIDC_AUDIENCE"];
        var validAudience = !string.IsNullOrWhiteSpace(audience)
            && audience == audience.Trim()
            && Uri.TryCreate(audience, UriKind.Absolute, out var audienceUri)
            && (audienceUri.Scheme == Uri.UriSchemeHttps || audienceUri.Scheme == Uri.UriSchemeHttp)
            && string.IsNullOrEmpty(audienceUri.UserInfo)
            && string.IsNullOrEmpty(audienceUri.Query)
            && string.IsNullOrEmpty(audienceUri.Fragment);

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Validate(_ => validAudience, "OIDC_AUDIENCE must be explicitly configured as an absolute HTTP(S) application base URL without credentials, query, or fragment.")
            .ValidateOnStart();

        var authority = configuration.GetConnectionString("keycloak");

        if (authority == null && env.IsDevelopment())
        {
            authority = $"https+http://keycloak/realms/velotime";
        }

        services.AddAdministrationAuthorization();
        services.AddAuthentication()
            .AddKeycloakJwtBearer(
                serviceName: "keycloak",
                realm: "velotime",
                options =>
                {
                    if (env.IsDevelopment())
                    {
                        options.RequireHttpsMetadata = false;
                    } else
                    {
                        options.Authority = authority;
                    }
                    options.TokenValidationParameters.ValidAudience = audience;
                    options.TokenValidationParameters.IgnoreTrailingSlashWhenValidatingAudience = false;
                    options.TokenValidationParameters.ValidIssuer = authority;
               });

        return builder;
    }
        
    public static IHostApplicationBuilder AddModuleCache(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        var env = builder.Environment;

        builder.AddRedisDistributedCache(connectionName: "cache");
        services.AddMemoryCache();

        services.AddFusionCache()
            .WithDefaultEntryOptions(new FusionCacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(5),
                MemoryCacheDuration = TimeSpan.FromMinutes(1),
            })
            .WithSerializer(new FusionCacheSystemTextJsonSerializer(options: new JsonSerializerOptions()
            {
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
            }))
            .WithRegisteredDistributedCache()
            .WithBackplane(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<RedisBackplane>>();
                var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
                var redisConfig = new RedisBackplaneOptions()
                {
                    Configuration = multiplexer.Configuration
                };
                return new RedisBackplane(redisConfig, logger);
            })
            .AsHybridCache();
        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.AddFusionCacheInstrumentation();
            })
            .WithMetrics(metrics =>
            {
                metrics.AddFusionCacheInstrumentation();
            });

        return builder;
    }

    public static IHostApplicationBuilder AddModuleIdentity(this IHostApplicationBuilder builder, string clientId, List<string> scopes = null!)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        var env = builder.Environment;

        var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");

        var authority = configuration.GetConnectionString("keycloak");

        if (authority == null && env.IsDevelopment())
        {
            authority = $"https+http://keycloak/realms/velotime";
        }

        if (authority == null && string.IsNullOrEmpty(clientSecret))
        {
            throw new InvalidOperationException("Authority and Client Secret must be provided either through configuration or environment variables.");
        }

        scopes ??= new List<string>();

        //services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        //    .AddKeycloakOpenIdConnect(
        //        serviceName: "keycloak",
        //        realm: "velotime",
        //        options =>
        //        {
        //            options.ClientId = clientId;
        //            options.ClientSecret = clientSecret;

        //            if (env.IsDevelopment())
        //            {
        //                options.RequireHttpsMetadata = false;
        //            }
        //            else
        //            {
        //                options.Authority = configuration.GetConnectionString("keycloak");
        //            }
        //        });

        services.AddClientCredentialsTokenManagement()
                .AddClient(clientId, options =>
                {
                    options.TokenEndpoint = new Uri($"{authority}/protocol/openid-connect/token");

                    options.ClientId = ClientId.Parse(clientId);
                    options.ClientSecret = ClientSecret.Parse(clientSecret!);

                    //options.Resource = Resource.Parse("https://api.veloti.me");
                    options.Scope = Scope.ParseOrDefault(string.Join(" ", scopes));

                    //options.Parameters.Add("audience", "https://api.veloti.me");
                });

        return builder;
    }

    public static void AddModuleStorage<TContext>(this IHostApplicationBuilder builder, string connectionName) where TContext : BaseDbContext
        => builder.Services.AddDbContext<TContext>(options =>
        {
            options.UseSnakeCaseNamingConvention();
            options.UseNpgsql(builder.Configuration.GetConnectionString(connectionName), pg_options =>
            {
                pg_options.MigrationsHistoryTable("__ef_migrations_history", "ef");
            });
        });
}
