using System.Text.Json;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using IMasterMessageBus = SlimMessageBus.Host.IMasterMessageBus;
using SlimMessageBus.Host.Outbox.PostgreSql.Configuration;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Statistics.Api;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Api.Test;

public sealed class StatisticsValidityThresholdsMessagingTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingConnectionFailsExplicitlyEvenInDevelopment(string? connectionString)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Development" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MessageBus"] = connectionString
        });
        Assert.Contains("ConnectionStrings:MessageBus is required",
            Assert.Throws<InvalidOperationException>(() => builder.AddStatisticsValidityThresholdsMessaging()).Message);
    }

    [Fact]
    public void RegistrationRetainsDurableOutboxAndSerializesBothScopes()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:MessageBus"] = "Endpoint=sb://localhost/";
        builder.Services.AddDbContext<StatisticsDbContext>(options => options.UseNpgsql("Host=localhost;Database=unused"));
        builder.AddStatisticsValidityThresholdsMessaging();
        using var host = builder.Build(); // Never start: no database or broker contact.
        var settings = host.Services.GetRequiredService<PostgreSqlOutboxSettings>();
        Assert.Equal("statistics", settings.PostgreSqlSettings.DatabaseSchemaName);
        Assert.False(settings.MessageCleanup.Enabled);
        Assert.True(settings.MaintainSequence);
        Assert.Equal(1, settings.PollBatchSize);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.PollIdleSleep);
        Assert.Equal(int.MaxValue, settings.MaxDeliveryAttempts);
        Assert.Equal("velotime-statistics", MessagingExtensions.ValidityThresholdsTopic);
        Assert.Contains(builder.Services, service => service.ServiceType == typeof(IHostedService)
            && service.ImplementationType?.Name == "StatisticsValidityThresholdsOutboxStartup");
        var serializer = host.Services.GetRequiredService<JsonMessageSerializer>();
        foreach (var configId in new Guid?[] { null, Guid.NewGuid() })
        {
            var message = new StatisticsValidityThresholdsChangedV1(Guid.NewGuid(), Guid.NewGuid(), configId);
            var json = JsonSerializer.Serialize(message, serializer.Options);
            Assert.Equal(message, JsonSerializer.Deserialize<StatisticsValidityThresholdsChangedV1>(json, serializer.Options));
        }
    }

    [Fact]
    public async Task ExplicitStartupPropagatesOutboxInitializationFailure()
    {
        var bus = DispatchProxy.Create<IMasterMessageBus, LifecycleBus>();
        var recorder = (LifecycleBus)bus;
        var failure = new InvalidOperationException("injected outbox initialization failure");
        recorder.StartFailure = failure;
        var startup = CreateStartup(bus);

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            startup.StartAsync(CancellationToken.None)));
        Assert.Equal(["Start"], recorder.Calls);
    }

    [Fact]
    public async Task ExplicitStartupAndShutdownAwaitBusLifecycle()
    {
        var bus = DispatchProxy.Create<IMasterMessageBus, LifecycleBus>();
        var recorder = (LifecycleBus)bus;
        recorder.StartCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startup = CreateStartup(bus);
        var starting = startup.StartAsync(CancellationToken.None);
        Assert.False(starting.IsCompleted);
        recorder.StartCompletion.SetResult();
        await starting;
        await startup.StopAsync(CancellationToken.None);
        Assert.Equal(["Start", "Stop"], recorder.Calls);
    }

    private static IHostedService CreateStartup(IMasterMessageBus bus)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:MessageBus"] = "Endpoint=sb://localhost/";
        builder.AddStatisticsValidityThresholdsMessaging();
        var registration = Assert.Single(builder.Services, service => service.ServiceType == typeof(IHostedService)
            && service.ImplementationType?.Name == "StatisticsValidityThresholdsOutboxStartup");
        return (IHostedService)Activator.CreateInstance(registration.ImplementationType!, bus)!;
    }

    public class LifecycleBus : DispatchProxy
    {
        public List<string> Calls { get; } = [];
        public Exception? StartFailure { get; set; }
        public TaskCompletionSource? StartCompletion { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod!.Name);
            return targetMethod.Name switch
            {
                "Start" when StartFailure is { } failure => Task.FromException(failure),
                "Start" => StartCompletion?.Task ?? Task.CompletedTask,
                "Stop" => Task.CompletedTask,
                _ => throw new NotSupportedException(targetMethod.Name)
            };
        }
    }
}
