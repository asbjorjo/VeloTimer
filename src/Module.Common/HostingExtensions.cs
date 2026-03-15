using Duende.AccessTokenManagement;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using System.Runtime.CompilerServices;
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
                        options.Authority = configuration.GetConnectionString("keycloak");
                    }
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

    public static IHostApplicationBuilder AddModuleIdentity(this IHostApplicationBuilder builder, string clientId)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        var env = builder.Environment;

        var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");

        var authority = env.IsDevelopment() ? configuration.GetConnectionString("keycloak") : $"https+http://keycloak/realms/velotime";

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
                options.TokenEndpoint = new System.Uri($"{authority}/protocol/openid-connect/token");

                options.ClientId = ClientId.Parse(clientId);
                options.ClientSecret = ClientSecret.Parse(clientSecret!);
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
