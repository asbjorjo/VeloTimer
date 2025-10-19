using OpenIddict.Validation.AspNetCore;
using Serilog;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Timing;

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

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        });

        builder.Services.AddOpenIddict()
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

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("veloTimeApiPolicy", policyUser =>
            {
                //policyUser.RequireClaim("scope", "veloTimeApi");
                policyUser.RequireAuthenticatedUser();
            });
        });

        // Add services to the container.
        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();

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

        builder.AddModuleTiming();

        services.AddControllers();

        return builder.Build();
    }

    public static WebApplication ConfigurePipeline(this WebApplication app)
    {
        app.UseSerilogRequestLogging();

        if (_env!.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.MapOpenApi();
        }

        app.UseCors("AllowAllOrigins");

        app.UseHttpsRedirection();
        app.UseAuthorization();

        app.MapControllers();

        return app;
    }
}