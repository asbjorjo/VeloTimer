using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Interceptor;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Module.Common;
using VeloTime.Module.Timing;
using VeloTime.Module.Timing.Handlers;
using VeloTime.Module.Timing.Interface.Messages;
using VeloTime.Module.Timing.Service;

public static class StartupExtensions
{
    public static IHostApplicationBuilder AddServices(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        var env = builder.Environment;

        services.AddSlimMessageBus(mbb =>
        {
            mbb.WithProviderServiceBus(options =>
            {
                options.ConnectionString = configuration.GetConnectionString("MessageBus");
                options.MaxConcurrentSessions = 10;
                options.SessionIdleTimeout = TimeSpan.FromSeconds(5);
            });
            mbb.AddJsonSerializer();
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

        services.AddTransient(typeof(IConsumerInterceptor<>), typeof(ActivityInterceptor<>));
        services.AddScoped<IAgentService, AgentService>();

        builder.AddModuleTiming();

        return builder;
    }
}
