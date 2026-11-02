using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using SlimMessageBus;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using VeloTime.Agent;
using VeloTime.Agent.Model;
using VeloTime.Agent.Service;
using VeloTime.Agent.Storage;

using Assert = Xunit.Assert;

namespace VeloTime.Workers.Test;

public sealed class PostgreSqlOutboxFactAttribute : FactAttribute
{
    public PostgreSqlOutboxFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOTIME_TEST_OUTBOX_POSTGRES")))
            Skip = "Set VELOTIME_TEST_OUTBOX_POSTGRES to run real PostgreSQL outbox tests.";
    }
}

public sealed class AgentOutboxTests : IAsyncLifetime
{
    private readonly string databaseName = $"agent_outbox_{Guid.NewGuid():N}";
    private readonly List<IHost> hosts = [];
    private string adminConnection = "";
    private string connectionString = "";

    public async Task InitializeAsync()
    {
        adminConnection = Environment.GetEnvironmentVariable("VELOTIME_TEST_OUTBOX_POSTGRES") ?? "";
        if (string.IsNullOrWhiteSpace(adminConnection)) return;
        await Execute(adminConnection, $"CREATE DATABASE {databaseName}");
        connectionString = new NpgsqlConnectionStringBuilder(adminConnection) { Database = databaseName }.ConnectionString;
        await using var storage = new AgentDbContext(new DbContextOptionsBuilder<AgentDbContext>()
            .UseNpgsql(connectionString).UseSnakeCaseNamingConvention().Options);
        await storage.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var host in hosts)
        {
            await host.StopAsync();
            host.Dispose();
        }
        if (!string.IsNullOrWhiteSpace(connectionString))
            await Execute(adminConnection, $"DROP DATABASE {databaseName} WITH (FORCE)");
    }

    [PostgreSqlOutboxFact]
    public async Task LoopBatchBypassesOutboxAndThrowsDuringBrokerOutage()
    {
        var host = await StartHost(new TestSender());
        await using var scope = host.Services.CreateAsyncScope();
        var exception = await Assert.ThrowsAsync<ProducerMessageBusException>(() => scope.ServiceProvider.GetRequiredService<MessagingService>()
            .SendEventsAsync([Loop("first")]));
        Assert.IsType<ServiceBusException>(exception.InnerException);
        Assert.Equal(0, await Scalar("SELECT count(*)::int FROM agent.smb_outbox"));
    }

    private static LoopConfig Loop(string id) => new() { LoopId = id, Name = id, SystemId = "system", Order = 1 };

    private async Task<IHost> StartHost(TestSender sender)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VELOTIME_AGENT"] = "test-agent",
            ["ConnectionStrings:AgentDb"] = connectionString,
            ["ConnectionStrings:MessageBus"] = "Endpoint=sb://localhost/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==",
            ["MessageBus:OutboxPollIdleSleep"] = "00:00:00.05"
        });
        builder.Services.AddSingleton<IControlService, TestControl>();
        builder.ConfigureAgent();
        var busBuilder = (MessageBusBuilder)builder.Services.Single(s => s.ServiceType == typeof(MessageBusBuilder)).ImplementationInstance!;
        busBuilder.WithProviderServiceBus(options =>
        {
            options.ConnectionString = builder.Configuration.GetConnectionString("MessageBus");
            options.TopologyProvisioning.Enabled = false;
            options.SenderFactory = (_, _) => sender;
        });
        var host = builder.Build();
        hosts.Add(host);
        await host.StartAsync();
        return host;
    }

    private async Task<int> Scalar(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task Execute(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TestControl : IControlService
    {
        public Task PauseAgent() => Task.CompletedTask;
        public Task ResumeAgent() => Task.CompletedTask;
    }

    private sealed class TestSender : ServiceBusSender
    {
        private readonly ConcurrentDictionary<ServiceBusMessageBatch, List<ServiceBusMessage>> batches = new();
        public bool Available { get; set; }
        public ConcurrentQueue<ServiceBusMessage> Delivered { get; } = new();
        public override ValueTask<ServiceBusMessageBatch> CreateMessageBatchAsync(CancellationToken cancellationToken = default)
        {
            var messages = new List<ServiceBusMessage>();
            var batch = ServiceBusModelFactory.ServiceBusMessageBatch(0, messages, tryAddCallback: _ => true);
            batches[batch] = messages;
            return ValueTask.FromResult(batch);
        }
        public override Task SendMessagesAsync(ServiceBusMessageBatch messageBatch, CancellationToken cancellationToken = default)
        {
            var messages = batches[messageBatch];
            batches.TryRemove(messageBatch, out _);
            if (!Available) throw new ServiceBusException("Simulated outage.", ServiceBusFailureReason.ServiceCommunicationProblem);
            foreach (var message in messages) Delivered.Enqueue(message);
            return Task.CompletedTask;
        }
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
