using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using VeloTime.Module.Timing.Handlers;
using VeloTime.Module.Timing.Interface.Messages;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing;

public static class StartupExtensions
{
    public static IHostApplicationBuilder AddModuleTiming(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        var env = builder.Environment;

        services.AddSlimMessageBus(mbb =>
        {
            mbb.Consume<PassingObserved>(x => x
                .Topic("velotime-agent-test")
                .SubscriptionName("timing")
                .WithConsumer<PassingObservedHandler>()
                .Instances(1)
                .EnableSession());
            mbb.Produce<PassingSaved>(x => x
                .DefaultTopic("velotime-timing")
                .WithModifier((message, sbMessage) => 
                { 
                    sbMessage.SessionId = message.TransponderId.ToString(); 
                }));
            mbb.AddServicesFromAssemblyContaining<PassingObservedHandler>();
        });

        services.AddDbContext<TimingDbContext>(options =>
        {
            options.UseNpgsql(configuration.GetConnectionString("TimingDbConnection"));
        });

        return builder;
    }
}
