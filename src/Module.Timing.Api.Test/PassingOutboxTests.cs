using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Outbox.PostgreSql.Configuration;
using VeloTime.Module.Timing.Interface.Messages;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Timing.Storage;
using Assert = Xunit.Assert;

namespace VeloTime.Module.Timing.Api.Test;

/// <summary>
/// Real PostgreSQL checks that a passing fact and its PassingRecordedV1 outbox row commit or roll back together,
/// and that the Timing.Api host (which shares timing.smb_outbox with Timing.Processor) can dispatch passing events.
/// </summary>
public sealed class PassingOutboxTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string databaseName = $"passing_outbox_{Guid.NewGuid():N}";
    private readonly List<IHost> hosts = [];
    private readonly CommitFailure commitFailure = new();
    private string connectionString = "";
    private string adminConnectionString = "";

    public async Task InitializeAsync()
    {
        adminConnectionString = Environment.GetEnvironmentVariable("VELOTIME_TEST_OUTBOX_POSTGRES") ?? "";
        if (string.IsNullOrWhiteSpace(adminConnectionString)) return;
        await ExecuteSql(adminConnectionString, $"CREATE DATABASE \"{databaseName}\"");
        connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName }.ConnectionString;
        await using var storage = new TimingDbContext(new DbContextOptionsBuilder<TimingDbContext>().UseNpgsql(connectionString).Options);
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
            await ExecuteSql(adminConnectionString, $"DROP DATABASE \"{databaseName}\" WITH (FORCE)");
    }

    [PostgreSqlOutboxFact]
    public async Task RecordedPassingIsDeliveredOnceAndRedeliveryEnqueuesNothing()
    {
        var sender = new TestSender();
        var host = await StartHost(sender);
        var observation = new PassingObservation("outbox-agent", Guid.NewGuid(), "106187718", "ProChip", "finish", DateTime.UtcNow);

        var first = await Register(host, observation);
        var duplicate = await Register(host, observation);

        Assert.Equal(PassingRegistrationOutcome.Recorded, first.Outcome);
        Assert.Equal(PassingRegistrationOutcome.Duplicate, duplicate.Outcome);
        await WaitUntil(async () => await Scalar("SELECT count(*)::int FROM timing.smb_outbox WHERE delivery_complete") == 1);
        Assert.Equal(1, await Scalar("SELECT count(*)::int FROM timing.smb_outbox"));
        var message = Assert.Single(sender.TransportMessages);
        Assert.Equal(nameof(PassingRecordedV1), message.ApplicationProperties["MessageName"]);
        Assert.Equal(first.Passing.TransponderId.ToString(), message.SessionId);
        Assert.Equal(first.Passing.Id.ToString(), message.MessageId);
        var recorded = JsonSerializer.Deserialize<PassingRecordedV1>(message.Body.ToString(), JsonOptions)!;
        Assert.Equal(first.Passing.Id, recorded.PassingId);
        Assert.Equal(first.Passing.InstallationId, recorded.InstallationId);
        Assert.Equal(first.Passing.Time, recorded.Time.ToUniversalTime());
    }

    [PostgreSqlOutboxFact]
    public async Task CommitFailureRollsBackPassingAndEvent()
    {
        var host = await StartHost(new TestSender { Available = false });
        var observation = new PassingObservation("rollback-agent", Guid.NewGuid(), "106187718", "ProChip", "finish", DateTime.UtcNow);
        // Resolve installation, timing point and transponder first; they commit outside the fact transaction.
        await Register(host, observation with { SourcePassingId = Guid.NewGuid(), Time = observation.Time.AddMinutes(-1) });
        var outboxBefore = await Scalar("SELECT count(*)::int FROM timing.smb_outbox");

        commitFailure.Enabled = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Register(host, observation));
        commitFailure.Enabled = false;

        Assert.Equal(0, await Scalar($"SELECT count(*)::int FROM timing.\"Passing\" WHERE \"SourcePassingId\" = '{observation.SourcePassingId}'"));
        Assert.Equal(outboxBefore, await Scalar("SELECT count(*)::int FROM timing.smb_outbox"));

        Assert.Equal(PassingRegistrationOutcome.Recorded, (await Register(host, observation)).Outcome);
        Assert.Equal(outboxBefore + 1, await Scalar("SELECT count(*)::int FROM timing.smb_outbox"));
    }

    [PostgreSqlOutboxFact]
    public async Task ConflictingRedeliveryIsFlaggedWithoutAnEvent()
    {
        var host = await StartHost(new TestSender { Available = false });
        var time = DateTime.UtcNow;
        var id = Guid.NewGuid();
        await Register(host, new PassingObservation("conflict-agent", id, "106187718", "ProChip", "finish", time));

        var conflict = await Register(host, new PassingObservation("conflict-agent", id, "106187718", "ProChip", "finish", time.AddSeconds(5)));

        Assert.Equal(PassingRegistrationOutcome.Conflict, conflict.Outcome);
        Assert.Equal(1, await Scalar("SELECT count(*)::int FROM timing.\"PassingConflict\""));
        Assert.Equal(1, await Scalar("SELECT count(*)::int FROM timing.smb_outbox"));
    }

    private static async Task<PassingRegistration> Register(IHost host, PassingObservation observation)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAgentService>().RegisterPassingAsync(observation);
    }

    private async Task<IHost> StartHost(TestSender sender)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MessageBus"] = "Endpoint=sb://localhost/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA=="
        });
        builder.Services.AddDbContext<TimingDbContext>(options => options.UseNpgsql(connectionString).AddInterceptors(commitFailure));
        builder.Services.AddHybridCache();
        builder.Services.AddMetrics();
        builder.Services.AddSingleton<Metrics>();
        builder.Services.AddScoped<InstallationService>();
        builder.Services.AddScoped<IAgentService, AgentService>();
        builder.AddOwnershipMessaging();
        var busBuilder = (MessageBusBuilder)builder.Services.Single(service => service.ServiceType == typeof(MessageBusBuilder)).ImplementationInstance!;
        busBuilder.WithProviderServiceBus(options =>
        {
            options.ConnectionString = builder.Configuration.GetConnectionString("MessageBus");
            options.TopologyProvisioning.Enabled = false;
            options.SenderFactory = (_, _) => sender;
        });
        var host = builder.Build();
        hosts.Add(host);
        var settings = host.Services.GetRequiredService<PostgreSqlOutboxSettings>();
        settings.PollIdleSleep = TimeSpan.FromMilliseconds(50);
        settings.LockExpiration = TimeSpan.FromSeconds(5);
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

    private static async Task ExecuteSql(string connection, string sql)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand(sql, db);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!await condition()) await Task.Delay(50, timeout.Token);
    }

    private sealed class CommitFailure : DbTransactionInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Enabled) throw new InvalidOperationException("Simulated commit failure.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TestSender : ServiceBusSender
    {
        private readonly ConcurrentDictionary<ServiceBusMessageBatch, List<ServiceBusMessage>> batches = new();
        public bool Available { get; set; } = true;
        public ConcurrentQueue<ServiceBusMessage> TransportMessages { get; } = new();

        public override ValueTask<ServiceBusMessageBatch> CreateMessageBatchAsync(CancellationToken cancellationToken = default)
        {
            var messages = new List<ServiceBusMessage>();
            var batch = ServiceBusModelFactory.ServiceBusMessageBatch(0, messages, tryAddCallback: _ => true);
            batches[batch] = messages;
            return ValueTask.FromResult(batch);
        }

        public override Task SendMessagesAsync(ServiceBusMessageBatch messageBatch, CancellationToken cancellationToken = default)
        {
            if (!Available) throw new ServiceBusException("Simulated broker outage.", ServiceBusFailureReason.ServiceBusy);
            foreach (var message in batches[messageBatch]) TransportMessages.Enqueue(message);
            batches.TryRemove(messageBatch, out _);
            return Task.CompletedTask;
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
