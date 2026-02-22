using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Interceptor;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Timing.Interface.Messages;

internal static class StartupExtensions
{
    internal static IHostApplicationBuilder AddServices(this IHostApplicationBuilder builder)
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
            mbb.Consume<TimingSampleComplete>(x => x
                .Topic("velotime-timing-test")
                .SubscriptionName("statistics")
                .WithConsumer<TimingSampleHandler>()
                .Instances(1)
                .EnableSession(s => {
                    //s.MaxConcurrentSessions(1);
                }));
            mbb.Consume<EntryCreated>(x => x
                .Topic("velotime-statistics-test")
                .SubscriptionName("statistics")
                .WithConsumer<EntryCreatedHandler>()
                .WhenUndeclaredMessageTypeArrives(opts =>
                {
                    opts.Fail = false;
                    opts.Log = true;
                })
                .Instances(1)
                .EnableSession(s =>
                {
                    //s.MaxConcurrentSessions(1);
                }));
            mbb.Consume<SampleComplete>(x => x
                .Topic("velotime-statistics-test")
                .SubscriptionName("statistics")
                .WithConsumer<SampleCompleteHandler>()
                .Instances(1)
                .EnableSession(s =>
                {
                    //s.MaxConcurrentSessions(1);
                }));
            mbb.Produce<EntryCreated>(x => x
                .DefaultTopic("velotime-statistics-test")
                .WithModifier((message, sbMessage) =>
                {
                    sbMessage.SessionId = message.TransponderId.ToString();
                }));
            mbb.Produce<SampleComplete>(x => x
                .DefaultTopic("velotime-statistics-test")
                .WithModifier((message, sbMessage) =>
                {
                    sbMessage.SessionId = message.TransponderId.ToString();
                }));
            mbb.AddServicesFromAssemblyContaining<TimingSampleHandler>();
        });

        services.AddTransient(typeof(IConsumerInterceptor<>), typeof(ActivityInterceptor<>));

        services.AddFacilitiesClient();

        builder.AddModuleStatistics();

        return builder;
    }
}
