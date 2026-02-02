using OpenIddict.Validation.AspNetCore;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Npgsql;
using Serilog;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Bootstrap;
using VeloTime.Module.Facilities;
using VeloTime.Module.Statistics;
using VeloTime.Module.Timing;
using SlimMessageBus.Host.Interceptor;
using VeloTime.Module.Common;

namespace VeloTime.Api;

internal static class StartupExtensions
{
    private static IWebHostEnvironment? _env;
    public static WebApplication ConfigureServices(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        _env = builder.Environment;

        // Open up security restrictions to allow this to work
        // Not recommended in production
        var deploySwaggerUI = builder.Configuration.GetValue<bool>("DeploySwaggerUI");
        var isDev = _env.IsDevelopment();

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(_env.ApplicationName))
            .WithTracing(tracing => tracing
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                .AddSource("Azure.Messaging.ServiceBus")
                .AddSource("VeloTime.*")
            )
            .WithMetrics(metrics => metrics
                .AddHttpClientInstrumentation()
            )
            .UseOtlpExporter();

        builder.Logging.AddOpenTelemetry(options =>
        {
            options.IncludeScopes = true;
            options.IncludeFormattedMessage = true;
        });

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        });

        services.AddOpenIddict()
            .AddValidation(options =>
            {
                // Note: the validation handler uses OpenID Connect discovery
                // to retrieve the address of the introspection endpoint.
                options.SetIssuer("https://localhost:44318");
                options.AddAudiences("rs_veloTimeApi");

                // Configure the validation handler to use introspection and register the client
                // credentials used when communicating with the remote introspection endpoint.
                options.UseIntrospection()
                        .SetClientId("rs_veloTimeApi")
                        .SetClientSecret("velotime-api-secret");

                // disable access token encyption for this
                options.UseAspNetCore();

                // Register the System.Net.Http integration.
                options.UseSystemNetHttp();

                // Register the ASP.NET Core host.
                options.UseAspNetCore();
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("veloTimeApiPolicy", policyUser =>
            {
                //policyUser.RequireClaim("scope", "veloTimeApi");
                policyUser.RequireAuthenticatedUser();
            });
        });

        // Add services to the container.
        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        services.AddOpenApi("v1", options =>
        {
            options.AddOperationTransformer<PaginationOperationTransformer>();
        });

        services.AddCors(options =>
        {
            options.AddPolicy("AllowAllOrigins",
                builder =>
                {
                    builder
                        .AllowCredentials()
                        .WithOrigins("https://localhost:5001")
                        .SetIsOriginAllowedToAllowWildcardSubdomains()
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
        });

        services.AddTransient(typeof(IConsumerInterceptor<>), typeof(ActivityInterceptor<>));
        services.AddSlimMessageBus(mbb =>
        {
            mbb.WithProviderServiceBus(options =>
            {
                options.ConnectionString = configuration.GetConnectionString("MessageBus");
                options.MaxConcurrentSessions = 10;
                options.SessionIdleTimeout = TimeSpan.FromSeconds(5);
            });
            mbb.AddJsonSerializer();
        });

        builder.AddModuleFacilities();
        builder.AddModuleStatistics();
        builder.AddModuleTiming();

        if (isDev)
        {
            builder.AddBootstrap();
        }

        services.AddControllers();

        services.AddOutputCache();

        return builder.Build();
    }

    public static WebApplication ConfigurePipeline(this WebApplication app)
    {
        //app.UseSerilogRequestLogging();

        if (_env!.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.MapOpenApi();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", "v1");
            });
        }
        else
        {
            app.UseHttpsRedirection();
        }

        app.UseCors("AllowAllOrigins");

        app.UseAuthorization();

        app.UseModuleTiming();
        app.UseModuleFacilities();
        app.UseModuleStatistics();

        app.MapControllers();

        app.UseOutputCache();

        return app;
    }
}