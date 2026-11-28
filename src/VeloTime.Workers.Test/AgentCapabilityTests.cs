using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VeloTime.Agent;
using VeloTime.Agent.Capabilities;
using VeloTime.Agent.Handler;
using VeloTime.Agent.Interface;
using VeloTime.Agent.Interface.Messages.Control;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Agent.Service;
using VeloTime.Module.Timing.Handlers;
using IMasterMessageBus = SlimMessageBus.Host.IMasterMessageBus;
using MessageBusBase = SlimMessageBus.Host.MessageBusBase;
using System.Reflection;

namespace VeloTime.Workers.Test;

public sealed class AgentCapabilityTests
{
    private static HostApplicationBuilder AgentBuilder(string agentId = "agent-a")
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VELOTIME_AGENT"] = agentId,
            ["ConnectionStrings:AgentDb"] = "Host=localhost;Database=agent;Username=test;Password=test",
            ["ConnectionStrings:MessageBus"] = "Endpoint=sb://localhost/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA=="
        });
        return builder;
    }

    [Fact]
    public async Task Agent_ConsumesEveryCommandFromItsOwnControlSubscriptionOnly()
    {
        var builder = AgentBuilder("agent-a");
        builder.ConfigureAgent(agentType: "Test");
        await using var provider = builder.Services.BuildServiceProvider();
        var bus = Assert.IsAssignableFrom<MessageBusBase>(provider.GetRequiredService<IMasterMessageBus>());

        Assert.DoesNotContain(bus.Settings.Consumers, consumer => consumer.Path == "velotime-agent");
        var control = bus.Settings.Consumers.Where(consumer => consumer.Path == AgentControl.Topic).ToList();
        Assert.Equal(
            [typeof(PauseAgentCommand), typeof(ReportCapabilitiesCommandV1), typeof(ResumeAgentCommand)],
            control.Select(consumer => consumer.MessageType).OrderBy(type => type.Name));
        foreach (var consumer in control)
        {
            // Subscription named after the agent, with a correlation filter on the AgentId header the platform sets.
            Assert.Contains(consumer.Properties.Values, value => Equals(value, "agent-a"));
            var rules = consumer.Properties.Values.OfType<System.Collections.IDictionary>().SelectMany(Values).ToList();
            var rule = Assert.Single(rules, value => value?.GetType().Name == "SubscriptionCorrelationRule");
            var properties = Assert.IsAssignableFrom<IDictionary<string, object>>(rule!.GetType()
                .GetProperty("ApplicationProperties", BindingFlags.Public | BindingFlags.Instance)!.GetValue(rule));
            Assert.Equal("agent-a", Assert.Single(properties, pair => pair.Key == AgentControl.AgentIdHeader).Value);
        }
        Assert.Contains(bus.Settings.Producers, producer => producer.MessageType == typeof(AgentCapabilitiesV1) &&
            producer.DefaultPath == "velotime-agent");
        Assert.Equal("Test", provider.GetRequiredService<IOptions<AgentOptions>>().Value.AgentType);
        Assert.Contains(builder.Services, descriptor => descriptor.ServiceType == typeof(IHostedService));
    }

    private static IEnumerable<object?> Values(System.Collections.IDictionary dictionary)
    {
        foreach (System.Collections.DictionaryEntry entry in dictionary) yield return entry.Value;
    }

    [Fact]
    public async Task CapabilityReporter_AdvertisesPauseResumeOnlyWithAControlService()
    {
        var without = AgentBuilder();
        without.ConfigureAgent(agentType: "Test");
        await using (var provider = without.Services.BuildServiceProvider())
        {
            var report = await provider.GetRequiredService<CapabilityReporter>().BuildReportAsync(CancellationToken.None);
            Assert.Empty(report.Capabilities);
            Assert.Equal(("Test", AgentControl.ProtocolVersion), (report.AgentType, report.ControlProtocolVersion));
            Assert.False(string.IsNullOrWhiteSpace(report.AgentVersion));
            Assert.Equal(DateTimeKind.Utc, report.ReportedAtUtc.Kind);
        }

        var with = AgentBuilder();
        with.ConfigureAgent();
        with.Services.AddSingleton<IControlService, NoopControl>();
        await using (var provider = with.Services.BuildServiceProvider())
        {
            var report = await provider.GetRequiredService<CapabilityReporter>().BuildReportAsync(CancellationToken.None);
            Assert.Equal(AgentCapabilities.PauseResume, Assert.Single(report.Capabilities).Name);
            Assert.Equal("Unknown", report.AgentType);
        }
    }

    [Fact]
    public async Task CapabilityReporter_CombinesProvidersAndHandlerRequestsAReport()
    {
        var builder = AgentBuilder();
        builder.Configuration["Agent:AgentType"] = "Configured";
        builder.Configuration["Agent:AgentVersion"] = "9.9.9";
        builder.ConfigureAgent();
        var replay = new FixedProvider(new AgentCapability(AgentCapabilities.PassingReplay,
            new PassingReplayCapability(PassingReplayModes.AgentStore, null, TimeSpan.FromDays(30), false)));
        builder.Services.AddSingleton<IAgentCapabilityProvider>(replay);
        await using var provider = builder.Services.BuildServiceProvider();
        var reporter = provider.GetRequiredService<CapabilityReporter>();

        var report = await reporter.BuildReportAsync(CancellationToken.None);
        Assert.Equal(("Configured", "9.9.9"), (report.AgentType, report.AgentVersion));
        var capability = Assert.Single(report.Capabilities);
        Assert.Equal(PassingReplayModes.AgentStore, capability.PassingReplay!.Mode);

        var handler = new ReportCapabilitiesHandler(reporter, provider.GetRequiredService<IOptions<AgentOptions>>(),
            NullLogger<ReportCapabilitiesHandler>.Instance);
        await handler.OnHandle(new ReportCapabilitiesCommandV1(Guid.NewGuid(), "agent-a"), CancellationToken.None);
    }

    [Fact]
    public async Task TimingProcessor_ConsumesCapabilityReportsOnInfrastructureSubscription()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:MessageBus"] = "Endpoint=sb://localhost/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==";
        builder.Configuration["ConnectionStrings:velotimedb"] = "Host=localhost;Database=timing;Username=test;Password=test";
        builder.Configuration["ConnectionStrings:cache"] = "localhost:6379";
        var startup = typeof(AgentCapabilitiesHandler).Assembly.GetType("StartupExtensions")!;
        startup.GetMethod("AddServices", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, [builder]);
        await using var provider = builder.Services.BuildServiceProvider();
        var bus = Assert.IsAssignableFrom<MessageBusBase>(provider.GetRequiredService<IMasterMessageBus>());

        Assert.Single(bus.Settings.Consumers, consumer => consumer.Path == "velotime-agent" &&
            consumer.MessageType == typeof(AgentCapabilitiesV1) && consumer.ConsumerType == typeof(AgentCapabilitiesHandler) &&
            consumer.Properties.Values.Contains("infrastructure"));
        // Both Timing hosts dispatch the shared outbox, so both can send commands to agents.
        Assert.Contains(bus.Settings.Producers, producer => producer.MessageType == typeof(ReportCapabilitiesCommandV1) &&
            producer.DefaultPath == AgentControl.Topic);
    }

    private sealed class NoopControl : IControlService
    {
        public Task PauseAgent() => Task.CompletedTask;
        public Task ResumeAgent() => Task.CompletedTask;
    }

    private sealed class FixedProvider(params AgentCapability[] capabilities) : IAgentCapabilityProvider
    {
        public ValueTask<IReadOnlyList<AgentCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<AgentCapability>>(capabilities);

        public event EventHandler? CapabilityChanged { add { } remove { } }
    }
}
