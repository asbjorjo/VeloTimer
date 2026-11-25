using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SlimMessageBus;
using SlimMessageBus.Host;
using Assert = Xunit.Assert;
using MessageBusBase = SlimMessageBus.Host.MessageBusBase;
using IMasterMessageBus = SlimMessageBus.Host.IMasterMessageBus;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Timing.Interface.Messages;

namespace VeloTime.Workers.Test;

public sealed class StatisticsProcessorRegistrationTests
{
    [Fact]
    public async Task TimingSubscriptionRegistersBothContractsAndOwnershipDependencies()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:MessageBus"] =
            "Endpoint=sb://localhost/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==";
        builder.Configuration["ConnectionStrings:keycloak"] = "https://identity.test.invalid/realms/velotime";
        builder.Configuration["ConnectionStrings:velotimedb"] = "Host=localhost;Database=statistics;Username=test;Password=test";
        builder.Configuration["ConnectionStrings:cache"] = "localhost:6379";
        var startup = typeof(TransponderOwnershipChangedHandler).Assembly.GetType("StartupExtensions")!;
        startup.GetMethod("AddServices", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [builder]);
        var mbb = (MessageBusBuilder)builder.Services.Single(x => x.ServiceType == typeof(MessageBusBuilder)).ImplementationInstance!;
        mbb.AutoStartConsumersEnabled(false);
        await using var provider = builder.Services.BuildServiceProvider();
        var bus = Assert.IsAssignableFrom<MessageBusBase>(provider.GetRequiredService<IMasterMessageBus>());
        var timingConsumers = bus.Settings.Consumers.Where(consumer => consumer.Path == "velotime-timing").ToList();
        Assert.Equal(2, timingConsumers.Count);
        Assert.Contains(timingConsumers, consumer => consumer.MessageType == typeof(TimingSampleComplete) &&
            consumer.ConsumerType == typeof(TimingSampleHandler));
        Assert.Contains(timingConsumers, consumer => consumer.MessageType == typeof(TransponderOwnershipChanged) &&
            consumer.ConsumerType == typeof(TransponderOwnershipChangedHandler));
        Assert.Contains(builder.Services, descriptor => descriptor.ServiceType == typeof(ITransponderOwnershipReader) &&
            descriptor.ImplementationType == typeof(TransponderOwnershipReader));
        Assert.Contains(builder.Services, descriptor => descriptor.ServiceType == typeof(StatisticsOwnershipService));
        Assert.Contains(builder.Services, descriptor => descriptor.ServiceType == typeof(StatisticsHeadRelease) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.DoesNotContain(builder.Services, descriptor =>
            descriptor.ImplementationType?.Name.Contains("OutboxWorker") == true);
    }
}
