using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using VeloTime.Module.Statistics.Endpoints;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Module.Statistics;

public static class StartupExtensions
{
    public static IHostApplicationBuilder AddModuleStatistics(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        var env = builder.Environment;

        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource("VeloTime.Module.Statistics"))
            .WithMetrics(metrics => metrics.AddMeter("VeloTime.Module.Statistics"));

        services.AddSlimMessageBus(mbb =>
        {
            mbb.Consume<TimingSampleComplete>(x => x
                .Topic("velotime-timing-test")
                .SubscriptionName("statistics")
                .WithConsumer<TimingSampleHandler>()
                .Instances(1)
                .EnableSession(s => { 
                    //s.MaxConcurrentSessions(1); 
                }));
            mbb.Consume<SampleComplete>(x => x
                .Topic("velotime-statistics-test")
                .SubscriptionName("statistics")
                .WithConsumer<SampleCompleteHandler>()
                .Instances(1)
                .EnableSession(s => { 
                    //s.MaxConcurrentSessions(1); 
                }));
            mbb.Produce<SampleComplete>(x => x
                .DefaultTopic("velotime-statistics-test")
                .WithModifier((message, sbMessage) =>
                {
                    sbMessage.SessionId = message.TransponderId.ToString();
                }));
            mbb.AddServicesFromAssemblyContaining<TimingSampleHandler>();
        });

        services.AddDbContext<StatisticsDbContext>(options =>
        {
            options.UseNpgsql(configuration.GetConnectionString("StatisticsDbConnection"));
            options.UseSnakeCaseNamingConvention();
        });

        services.AddTransient<StatisticsService>();
        services.AddSingleton<Metrics>();

        services.AddPagination();

        return builder;
    }

    public static void UseModuleStatistics(this WebApplication app)
    {
        var statistics = app.MapGroup("/api/statistics").WithTags(["Statistics"]);

        statistics.MapSampleEndpoints();

        if (app.Environment.IsDevelopment())
        {
            app.ApplyMigrations<StatisticsDbContext>();
        }
    }
}
