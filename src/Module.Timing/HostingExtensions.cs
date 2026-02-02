using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Module.Timing.Client;
using VeloTime.Module.Timing.Endpoints;
using VeloTime.Module.Timing.Handlers;
using VeloTime.Module.Timing.Interface.Client;
using VeloTime.Module.Timing.Interface.Messages;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing;

public static class StartupExtensions
{
    public static IHostApplicationBuilder AddModuleTiming(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        var env = builder.Environment;

        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource("VeloTime.Module.Timing"))
            .WithMetrics(metrics => metrics.AddMeter("VeloTime.Module.Timing"));

        services.AddSlimMessageBus(mbb =>
        {
            mbb.WithHeaderModifier((headers, message) =>
            {
                headers["MessageName"] = message.GetType().Name;
            });
            mbb.Consume<PassingEvent>(x => x
                .Topic("velotime-agent-test")
                .SubscriptionName("timing")
                .WithConsumer<PassingObservedHandler>()
                .Instances(1)
                .EnableSession(s =>
                {
                    //s.MaxConcurrentSessions(1);
                }));
            mbb.Produce<PassingSaved>(x => x
                .DefaultTopic("velotime-timing-test")
                .WithModifier((message, sbMessage) => 
                { 
                    sbMessage.SessionId = message.TransponderId.ToString(); 
                }));
            mbb.Produce<TimingSampleComplete>(x => x
                .DefaultTopic("velotime-timing-test")
                .WithModifier((message, sbMessage) =>
                {
                    sbMessage.SessionId = message.TransponderId.ToString();
                }));
            mbb.Consume<InstallationLayoutEvent>(x => x
                .Topic("velotime-agent-test")
                .SubscriptionName("infrastructure")
                .WithConsumer<InstallationLayoutHandler>()
                .Instances(1));
            mbb.AddServicesFromAssemblyContaining<PassingObservedHandler>();
        });

        services.AddDbContext<TimingDbContext>(options =>
        {
            options.UseNpgsql(configuration.GetConnectionString("TimingDbConnection"));
            options.UseSnakeCaseNamingConvention();
        });

        services.AddMemoryCache();
        services.AddScoped<InstallationService>();
        services.AddScoped<ITimingClient, TimingClient>();

        services.AddSingleton<Metrics>();

        return builder;
    }

    public static void UseModuleTiming(this WebApplication app)
    {
        var timing = app.MapGroup("/api/timing")
            .WithTags(["Timing"]);

        timing.MapInstallationEndpoints();
        timing.MapSampleEndpoints();

        if (app.Environment.IsDevelopment())
        {
            app.ApplyMigrations<TimingDbContext>();
        }
    }
}
