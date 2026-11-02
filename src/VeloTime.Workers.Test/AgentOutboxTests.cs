using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using SlimMessageBus;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Outbox.PostgreSql;
using SlimMessageBus.Host.Outbox.PostgreSql.DbContext;
using SlimMessageBus.Host.Outbox.PostgreSql.Configuration;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Agent.Interface.Messages.Events;
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
    private readonly TestLogs logs = new();
    private readonly CommitFailure commitFailure = new();
    private readonly PassingInsertGate insertGate = new();
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
    public async Task LoopBatchIsDurablyAcceptedDuringBrokerOutage()
    {
        var host = await StartHost(new TestSender());
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MessagingService>().SendEventsAsync([Loop("first")]);
        Assert.Equal(1, await Scalar("SELECT count(*)::int FROM agent.smb_outbox"));
    }

    [PostgreSqlOutboxFact]
    public async Task HostRemainsRunningAndAcceptsAnotherBatchAfterRepeatedBrokerFailures()
    {
        var host = await StartHost(new TestSender());
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<MessagingService>();
        await service.SendEventsAsync([Loop("first")]);
        await WaitUntil(async () => await Scalar("SELECT COALESCE(max(delivery_attempt), 0)::int FROM agent.smb_outbox") >= 4);

        Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        await service.SendEventsAsync([Loop("second")]);

        Assert.Equal(2, await Scalar("SELECT count(*)::int FROM agent.smb_outbox WHERE NOT delivery_complete AND NOT delivery_aborted"));
        Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
    }

    [PostgreSqlOutboxFact]
    public async Task OldestFailureBlocksLaterEventsAndRecoversAfterRestart()
    {
        var sender = new TestSender { Available = true, RejectedLoop = "first" };
        var host = await StartHost(sender);
        var settings = host.Services.GetRequiredService<PostgreSqlOutboxSettings>();
        Assert.True(settings.MaintainSequence);
        Assert.Equal(1, settings.PollBatchSize);
        Assert.Equal(int.MaxValue, settings.MaxDeliveryAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(50), settings.PollIdleSleep);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
            var repository = Assert.IsType<DbContextOutboxRepository<AgentDbContext>>(
                scope.ServiceProvider.GetRequiredService<IPostgreSqlMessageOutboxRepository>());
            Assert.Same(storage, repository.DbContext);
            await scope.ServiceProvider.GetRequiredService<MessagingService>().SendEventsAsync([Loop("first"), Loop("second")]);
        }
        await WaitUntil(async () => await Scalar("SELECT max(delivery_attempt) FROM agent.smb_outbox") >= 5);
        Assert.Empty(sender.Delivered);
        Assert.Equal(2, await Scalar("SELECT count(*)::int FROM agent.smb_outbox WHERE NOT delivery_complete AND NOT delivery_aborted"));
        await WaitUntil(() => Task.FromResult(logs.Errors.Any(m => m.Contains("Error while processing Path:", StringComparison.Ordinal))));
        Assert.Contains(logs.Errors, m => m.Contains("Producing message batch", StringComparison.Ordinal));
        Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        await host.StopAsync();
        sender.RejectedLoop = null;
        await StartHost(sender);
        await WaitUntil(async () => await Scalar("SELECT count(*)::int FROM agent.smb_outbox WHERE delivery_complete") == 2);
        Assert.Equal(new[] { "first", "second" }, sender.Delivered.Select(m => JsonSerializer.Deserialize<LoopConfigEvent>(m.Body, JsonOptions)!.LoopId));
        Assert.All(sender.Delivered, m =>
        {
            Assert.Equal("test-agent", m.ApplicationProperties["AgentId"]);
            Assert.Equal(nameof(LoopConfigEvent), m.ApplicationProperties["MessageName"]);
            Assert.Equal("test-agent", m.SessionId);
        });
    }

    [PostgreSqlOutboxFact]
    public async Task PassingResendsDedupeEveryCallAndNeverOverwritePersistedRows()
    {
        var host = await StartHost(new TestSender());
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<MessagingService>();
        await service.SendEventsAsync([Passing("one"), Passing("one")]);
        await service.SendEventsAsync([Passing("one", true), Passing("two"), Passing("two")]);
        await service.SendEventsAsync([Passing("one", true), Passing("two", true)]);
        await service.SendEventsAsync([Passing("three")]);
        await service.SendEventsAsync([Passing("three", true)]);
        Assert.Equal(3, await Scalar("SELECT count(*)::int FROM agent.passing"));
        Assert.Equal(0, await Scalar("SELECT count(*)::int FROM agent.passing WHERE low_battery"));
        Assert.Equal(3, await Scalar("SELECT count(*)::int FROM agent.smb_outbox"));
    }

    [PostgreSqlOutboxFact]
    public async Task ConcurrentPassingResendsInSeparateScopesAreDeduplicatedNotErrors()
    {
        var host = await StartHost(new TestSender());
        await using var firstScope = host.Services.CreateAsyncScope();
        await using var secondScope = host.Services.CreateAsyncScope();
        insertGate.Target = firstScope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var first = Task.Run(() => firstScope.ServiceProvider.GetRequiredService<MessagingService>()
            .SendEventsAsync([KeyedPassing("shared"), KeyedPassing("first-only")]));
        // The first call has inserted "shared" and its outbox row but not committed; the second call must block on that row.
        await insertGate.Inserted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var second = Task.Run(() => secondScope.ServiceProvider.GetRequiredService<MessagingService>()
            .SendEventsAsync([KeyedPassing("second-only"), KeyedPassing("shared", true)]));
        await WaitUntil(async () => await Scalar(
            $"SELECT count(*)::int FROM pg_stat_activity WHERE datname = '{databaseName}' AND wait_event_type = 'Lock'") == 1);
        insertGate.Release();
        await Task.WhenAll(first, second);

        Assert.Equal(3, await Scalar("SELECT count(*)::int FROM agent.passing"));
        Assert.Equal(0, await Scalar("SELECT count(*)::int FROM agent.passing WHERE low_battery"));
        Assert.Equal(3, await Scalar("SELECT count(*)::int FROM agent.smb_outbox"));
        foreach (var id in new[] { "shared", "first-only", "second-only" })
            Assert.Equal(1, await OutboxPassingCount(id));
    }

    [PostgreSqlOutboxFact]
    public async Task ConcurrentPassingResendInsertsWhenWinningTransactionRollsBack()
    {
        var host = await StartHost(new TestSender());
        await using var firstScope = host.Services.CreateAsyncScope();
        await using var secondScope = host.Services.CreateAsyncScope();
        insertGate.Target = firstScope.ServiceProvider.GetRequiredService<AgentDbContext>();
        insertGate.FailCommit = true;
        var first = Task.Run(() => firstScope.ServiceProvider.GetRequiredService<MessagingService>()
            .SendEventsAsync([KeyedPassing("shared"), KeyedPassing("first-only")]));
        await insertGate.Inserted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var second = Task.Run(() => secondScope.ServiceProvider.GetRequiredService<MessagingService>()
            .SendEventsAsync([KeyedPassing("shared", true), KeyedPassing("second-only")]));
        await WaitUntil(async () => await Scalar(
            $"SELECT count(*)::int FROM pg_stat_activity WHERE datname = '{databaseName}' AND wait_event_type = 'Lock'") == 1);
        insertGate.Release();
        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        await second;

        Assert.Equal(2, await Scalar("SELECT count(*)::int FROM agent.passing"));
        Assert.Equal(1, await Scalar("SELECT count(*)::int FROM agent.passing WHERE id = 'shared' AND low_battery"));
        Assert.Equal(2, await Scalar("SELECT count(*)::int FROM agent.smb_outbox"));
        Assert.Equal(1, await OutboxPassingCount("shared"));
        Assert.Equal(1, await OutboxPassingCount("second-only"));
        Assert.Equal(0, await OutboxPassingCount("first-only"));
    }

    [PostgreSqlOutboxFact]
    public async Task LargeMultiChunkPassingBatchIsInsertedAndEnqueuedOncePerPassing()
    {
        var host = await StartHost(new TestSender());
        var ids = Enumerable.Range(0, 2500).Select(i => $"p{2500 - i:D5}").ToArray();
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<MessagingService>().SendEventsAsync(ids.Select(id => KeyedPassing(id)));
        Assert.Equal(2500, await Scalar("SELECT count(*)::int FROM agent.passing"));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT {OutboxPassingId} FROM agent.smb_outbox", connection);
        var enqueued = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) enqueued.Add(reader.GetString(0));
        Assert.Equal(ids.Order(StringComparer.Ordinal), enqueued.Order(StringComparer.Ordinal));
    }

    // PassingEvent carries no passing ID, so these tests key each passing by a unique LoopId.
    private const string OutboxPassingId =
        "coalesce(convert_from(message_payload, 'UTF8')::jsonb->>'LoopId', convert_from(message_payload, 'UTF8')::jsonb->>'loopId')";

    private static Passing KeyedPassing(string id, bool lowBattery = false) => new()
    {
        Id = id, Time = DateTime.UtcNow, TransponderType = "X2", TransponderId = "123", LoopId = id, LowBattery = lowBattery
    };

    private Task<int> OutboxPassingCount(string id) =>
        Scalar($"SELECT count(*)::int FROM agent.smb_outbox WHERE {OutboxPassingId} = '{id}'");

    [PostgreSqlOutboxFact]
    public async Task SerializationFailureMidBatchRollsBackOutboxAndClearsTracker()
    {
        var host = await StartHost(new TestSender());
        var options = new JsonSerializerOptions(JsonOptions);
        options.Converters.Add(new RejectSecondPassing());
        host.Services.GetRequiredService<JsonMessageSerializer>().Options = options;
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<MessagingService>();
        await Assert.ThrowsAnyAsync<Exception>(() => service.SendEventsAsync([Passing("one"), Passing("two")]));
        Assert.Empty(scope.ServiceProvider.GetRequiredService<AgentDbContext>().ChangeTracker.Entries());
        Assert.Equal(0, await Scalar("SELECT count(*)::int FROM agent.passing"));
        Assert.Equal(0, await Scalar("SELECT count(*)::int FROM agent.smb_outbox"));
        host.Services.GetRequiredService<JsonMessageSerializer>().Options = JsonOptions;
        await service.SendEventsAsync([Passing("one"), Passing("two")]);
        Assert.Equal(2, await Scalar("SELECT count(*)::int FROM agent.passing"));
        Assert.Equal(2, await Scalar("SELECT count(*)::int FROM agent.smb_outbox"));
    }

    [PostgreSqlOutboxFact]
    public async Task PassingInsertFailureRollsBackPreviouslyInsertedOutboxEvents()
    {
        var host = await StartHost(new TestSender());
        await Execute(connectionString, "ALTER TABLE agent.passing ADD CONSTRAINT reject_two CHECK (id <> $$two$$)");
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<MessagingService>();
        await Assert.ThrowsAsync<DbUpdateException>(() => service.SendEventsAsync([Passing("one"), Passing("two")]));
        Assert.Empty(scope.ServiceProvider.GetRequiredService<AgentDbContext>().ChangeTracker.Entries());
        Assert.Equal(0, await Scalar("SELECT count(*)::int FROM agent.passing"));
        Assert.Equal(0, await Scalar("SELECT count(*)::int FROM agent.smb_outbox"));
    }

    [PostgreSqlOutboxFact]
    public async Task OutboxInsertFailureAndCancellationPropagateWithoutPersistingPassings()
    {
        var host = await StartHost(new TestSender());
        await Execute(connectionString, "ALTER TABLE agent.smb_outbox ADD CONSTRAINT reject_enqueue CHECK (false)");
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<MessagingService>();
        await Assert.ThrowsAnyAsync<Exception>(() => service.SendEventsAsync([Passing("one")]));
        Assert.Empty(scope.ServiceProvider.GetRequiredService<AgentDbContext>().ChangeTracker.Entries());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SendEventsAsync([Passing("one")], cancellation.Token));
        await service.SendEventsAsync(Array.Empty<Passing>(), cancellation.Token);
        Assert.Equal(0, await Scalar("SELECT count(*)::int FROM agent.passing"));
        Assert.Equal(0, await Scalar("SELECT count(*)::int FROM agent.smb_outbox"));
    }

    [PostgreSqlOutboxFact]
    public async Task CommitFailureRollsBackRowsAndOutboxAndAllowsRetryInSameScope()
    {
        var host = await StartHost(new TestSender());
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<MessagingService>();
        commitFailure.Enabled = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendEventsAsync([Passing("one")]));
        commitFailure.Enabled = false;
        Assert.Empty(scope.ServiceProvider.GetRequiredService<AgentDbContext>().ChangeTracker.Entries());
        Assert.Equal(0, await Scalar("SELECT count(*)::int FROM agent.passing"));
        Assert.Equal(0, await Scalar("SELECT count(*)::int FROM agent.smb_outbox"));
        await service.SendEventsAsync([Passing("one")]);
        Assert.Equal(1, await Scalar("SELECT count(*)::int FROM agent.passing"));
        Assert.Equal(1, await Scalar("SELECT count(*)::int FROM agent.smb_outbox"));
    }

    [PostgreSqlOutboxFact]
    public async Task EveryEventContractIsDurableAndRetainsTransportCorrelation()
    {
        var sender = new TestSender();
        var host = await StartHost(sender);
        var enumerations = 0;
        IEnumerable<LoopConfig> Batch()
        {
            Assert.Equal(1, ++enumerations);
            yield return Loop("first");
            yield return Loop("second");
        }
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<MessagingService>();
            await service.SendEventsAsync(Batch());
            await service.SendEventAsync(new InstallationConfig { Systems = [] });
            await service.SendEventAsync(new LoopStatus { Time = DateTime.UtcNow, LoopId = "first", Noise = 0.1 });
            await service.SendEventsAsync([Passing("one")]);
            await service.SendEventsAsync([new SegmentConfig { SegmentId = "segment", Name = "Lap", FromLoopId = "first", ToLoopId = "second", Length = 250, SystemId = "system" }]);
            await service.SendEventsAsync([new SystemConfig { SystemId = "system", Sport = "cycling", Description = "system", Loops = [], Segments = [] }]);
        }
        Assert.Equal(7, await Scalar("SELECT count(*)::int FROM agent.smb_outbox"));
        Assert.Empty(sender.Delivered);
        sender.Available = true;
        await WaitUntil(async () => await Scalar("SELECT count(*)::int FROM agent.smb_outbox WHERE delivery_complete") == 7);
        Assert.Equal(7, sender.Delivered.Count);
        Assert.Equal(6, sender.Delivered.Select(m => m.ApplicationProperties["MessageName"]).Distinct().Count());
        Assert.All(sender.Delivered, m =>
        {
            Assert.Equal("test-agent", m.ApplicationProperties["AgentId"]);
            Assert.Equal(m.ApplicationProperties["MessageName"].ToString() == nameof(PassingEvent) ? "X2_123" : "test-agent", m.SessionId);
        });
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

    /// <summary>Holds one context's enqueue transaction open (inserted but uncommitted) until released.</summary>
    private sealed class PassingInsertGate : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DbContext? Target { get; set; }
        public bool FailCommit { get; set; }
        public TaskCompletionSource Inserted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => release.TrySetResult();
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Target == null || !ReferenceEquals(eventData.Context, Target)) return result;
            Inserted.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            if (FailCommit) throw new InvalidOperationException("Simulated commit failure.");
            return result;
        }
    }

    private sealed class TestLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Errors { get; } = new();
        public ILogger CreateLogger(string categoryName) => new TestLogger(Errors);
        public void Dispose() { }
        private sealed class TestLogger(ConcurrentQueue<string> errors) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel)) errors.Enqueue(formatter(state, exception));
            }
        }
    }

    [PostgreSqlOutboxFact]
    public async Task UnresolvableOldestEventRemainsPendingRatherThanBeingAborted()
    {
        var sender = new TestSender();
        var host = await StartHost(sender);
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<MessagingService>().SendEventsAsync([Loop("first"), Loop("second")]);
        await Execute(connectionString, "UPDATE agent.smb_outbox SET message_type = $$Missing.Message, Missing.Assembly$$ WHERE id = (SELECT id FROM agent.smb_outbox ORDER BY timestamp LIMIT 1)");
        await WaitUntil(() => Task.FromResult(logs.Errors.Any(m => m.Contains("Error while processing outbox messages", StringComparison.Ordinal))));
        sender.Available = true;
        await Task.Delay(200);
        Assert.Empty(sender.Delivered);
        Assert.Equal(2, await Scalar("SELECT count(*)::int FROM agent.smb_outbox WHERE NOT delivery_complete AND NOT delivery_aborted"));
        Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static Passing Passing(string id, bool lowBattery = false) => new()
    {
        Id = id, Time = DateTime.UtcNow, TransponderType = "X2", TransponderId = "123", LoopId = "loop", LowBattery = lowBattery
    };

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!await condition()) await Task.Delay(50, timeout.Token);
    }

    private sealed class RejectSecondPassing : JsonConverter<PassingEvent>
    {
        public override PassingEvent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, PassingEvent value, JsonSerializerOptions options)
        {
            if (++count == 2) throw new JsonException("Simulated serialization failure.");
            JsonSerializer.Serialize(writer, value, JsonOptions);
        }
        private int count;
    }

    private static LoopConfig Loop(string id) => new() { LoopId = id, Name = id, SystemId = "system", Order = 1 };

    private async Task<IHost> StartHost(TestSender sender)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VELOTIME_AGENT"] = "test-agent",
            ["ConnectionStrings:AgentDb"] = connectionString,
            ["ConnectionStrings:MessageBus"] = "Endpoint=sb://localhost/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==",
            ["MessageBus:OutboxPollIdleSleep"] = "00:00:00.05"
        });
        builder.Services.AddSingleton<IControlService, TestControl>();
        builder.ConfigureAgent();
        builder.Services.ConfigureDbContext<AgentDbContext>(options => options.AddInterceptors(commitFailure, insertGate), ServiceLifetime.Singleton);
        var host = builder.Build();
        hosts.Add(host);
        var bus = Assert.IsType<ServiceBusMessageBus>(((IMessageBusTarget)host.Services.GetRequiredService<IMessageBus>()).Target);
        bus.ProviderSettings.SenderFactory = (_, _) => sender;
        await host.StartAsync();
        await bus.Start();
        await WaitUntil(async () => await Scalar("SELECT (to_regclass($$agent.smb_outbox$$) IS NOT NULL)::int") == 1);
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
        public string? RejectedLoop { get; set; }
        public ConcurrentQueue<ServiceBusMessage> Delivered { get; } = new();
        public override ValueTask<ServiceBusMessageBatch> CreateMessageBatchAsync(CancellationToken cancellationToken = default)
        {
            var messages = new List<ServiceBusMessage>();
            var batch = ServiceBusModelFactory.ServiceBusMessageBatch(0, messages, tryAddCallback: _ => true);
            batches[batch] = messages;
            return ValueTask.FromResult(batch);
        }
        public override async Task SendMessagesAsync(ServiceBusMessageBatch messageBatch, CancellationToken cancellationToken = default)
        {
            var messages = batches[messageBatch];
            batches.TryRemove(messageBatch, out _);
            await Task.Delay(100, cancellationToken);
            if (!Available || messages.Any(m => RejectedLoop != null && JsonSerializer.Deserialize<LoopConfigEvent>(m.Body, JsonOptions)?.LoopId == RejectedLoop)) throw new ServiceBusException("Simulated outage.", ServiceBusFailureReason.ServiceCommunicationProblem);
            foreach (var message in messages) Delivered.Enqueue(message);
        }
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
