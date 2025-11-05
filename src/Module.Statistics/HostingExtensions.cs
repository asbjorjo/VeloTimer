using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using VeloTime.Module.Timing.Handlers;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Module.Timing;

public static class StartupExtensions
{
    public static IHostApplicationBuilder AddModuleStatistics(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        var env = builder.Environment;

        services.AddSlimMessageBus(mbb =>
        {
            mbb.Consume<PassingSaved>(x => x
                .Topic("velotime-agent-test")
                .SubscriptionName("statistics")
                .WithConsumer<PassingSavedHandler>()
                .Instances(1)
                .EnableSession());
            mbb.AddServicesFromAssemblyContaining<PassingSavedHandler>();
        });

        return builder;
    }
}
