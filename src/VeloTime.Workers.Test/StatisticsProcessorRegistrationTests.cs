using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SlimMessageBus;
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
        await using var provider = builder.Services.BuildServiceProvider();
        var bus = Assert.IsAssignableFrom<MessageBusBase>(provider.GetRequiredService<IMasterMessageBus>());
        var timingConsumers = bus.Settings.Consumers.Where(consumer => consumer.Path == "velotime-timing").ToList();
        // Phase 2 of the replay-safe plan: passing facts and ownership changes share the Timing subscription.
        Assert.Single(timingConsumers, consumer => consumer.MessageType == typeof(PassingRecordedV1) &&
            consumer.ConsumerType == typeof(PassingRecordedHandler));
        // Statistics no longer messages itself (SampleComplete/EntryCreated were removed).
        Assert.DoesNotContain(bus.Settings.Consumers, consumer => consumer.Path == "velotime-statistics"
            && consumer.MessageType != typeof(VeloTime.Module.Statistics.Interface.Messages.StatisticsValidityThresholdsChangedV1));
        Assert.Contains(builder.Services, descriptor => descriptor.ServiceType == typeof(StatisticsRecomputeService));
        Assert.Single(timingConsumers, consumer => consumer.MessageType == typeof(TransponderOwnershipChanged) &&
            consumer.ConsumerType == typeof(TransponderOwnershipChangedHandler));
        Assert.Contains(builder.Services, descriptor => descriptor.ServiceType == typeof(ITransponderOwnershipReader) &&
            descriptor.ImplementationType == typeof(TransponderOwnershipReader));
        Assert.Contains(builder.Services, descriptor => descriptor.ServiceType == typeof(StatisticsOwnershipService));
    }
}
