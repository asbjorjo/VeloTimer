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
using SlimMessageBus.Host.Outbox.PostgreSql.Configuration;
using SlimMessageBus.Host.Outbox;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Interface.Messages;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Service;
using VeloTime.Module.Timing.Storage;
using Assert = Xunit.Assert;

namespace VeloTime.Module.Timing.Api.Test;

public sealed class PostgreSqlOutboxFactAttribute : FactAttribute
{
    public PostgreSqlOutboxFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOTIME_TEST_OUTBOX_POSTGRES")))
            Skip = "Set VELOTIME_TEST_OUTBOX_POSTGRES to run real PostgreSQL outbox tests.";
    }
}

public sealed class OwnershipOutboxTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string databaseName = $"ownership_outbox_{Guid.NewGuid():N}";
    private readonly List<IHost> hosts = [];
    private readonly CommitFailure commitFailure = new();
    private readonly TestLogs logs = new();
    private string connectionString = "";
    private string adminConnectionString = "";
    private Guid transponderId;

    public async Task InitializeAsync()
    {
        adminConnectionString = Environment.GetEnvironmentVariable("VELOTIME_TEST_OUTBOX_POSTGRES") ?? "";
        if (string.IsNullOrWhiteSpace(adminConnectionString)) return;
        await ExecuteSql(adminConnectionString, $"CREATE DATABASE \"{databaseName}\"");
        connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName }.ConnectionString;
        await using var storage = new TimingDbContext(new DbContextOptionsBuilder<TimingDbContext>().UseNpgsql(connectionString).Options);
        await storage.Database.EnsureCreatedAsync();
        var transponder = new MylapsX2Transponder { Id = Guid.NewGuid(), SystemId = "106187718" };
        transponderId = transponder.Id;
        storage.Add(transponder);
        await storage.SaveChangesAsync();
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
    public async Task BrokerOutageAndRestartRecoverOriginalEventsInOrderAcrossTwoInstances()
    {
        var sender = new TestSender { Available = false };
        var firstHost = await StartHost(sender);
        await using (var scope = firstHost.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<TransponderOwnershipService>();
            await service.SaveAsync(null, Request(), CancellationToken.None);
        }
        await WaitUntil(async () => await Scalar<int>("SELECT delivery_attempt FROM timing.smb_outbox LIMIT 1") > 0);
        var first = Assert.Single(await ReadEvents());
        Assert.NotEqual(Guid.Empty, first.EventId);
        Assert.Equal(transponderId, first.TransponderId);
        Assert.Equal(Request().OwnedFrom, Assert.Single(first.Changes).After!.OwnedFrom);
        await using (var scope = firstHost.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
            var owner = await storage.Set<TransponderOwner>().SingleAsync();
            await scope.ServiceProvider.GetRequiredService<TransponderOwnershipService>().SaveAsync(owner.Id,
                Request() with { OwnedTo = Request().OwnedFrom.AddDays(2) }, CancellationToken.None);
        }
        Assert.Equal(2, await Scalar<int>("SELECT count(*)::int FROM timing.smb_outbox WHERE NOT delivery_complete AND NOT delivery_aborted"));
        Assert.Equal(1, await OwnershipCount());
        Assert.Empty(sender.Delivered);
        Assert.Contains(logs.Errors, message => message.Contains("Producing message batch", StringComparison.Ordinal));
        var expected = await ReadEvents();
        Assert.Equal(first.EventId, expected[0].EventId);

        await firstHost.StopAsync();
        sender.Available = true;
        await StartHost(sender);
        await StartHost(sender);
        await WaitUntil(async () => await Scalar<int>("SELECT count(*)::int FROM timing.smb_outbox WHERE delivery_complete") == 2);
        Assert.Equal(expected.Select(message => message.EventId), sender.Delivered.Select(message => message.EventId));
        Assert.Equal(expected.SelectMany(message => message.Changes), sender.Delivered.SelectMany(message => message.Changes));
        Assert.All(sender.TransportMessages, message =>
        {
            Assert.Equal(transponderId.ToString(), message.SessionId);
            Assert.Equal(nameof(TransponderOwnershipChanged), message.ApplicationProperties["MessageName"]);
            Assert.Equal(JsonSerializer.Deserialize<TransponderOwnershipChanged>(message.Body.ToString(), JsonOptions)!.EventId.ToString(), message.MessageId);
        });
    }

    [PostgreSqlOutboxFact]
    public async Task ClaimEventMatchesPersistedOwnershipInstants()
    {
        var host = await StartHost(new TestSender { Available = false });
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TransponderOwnershipService>()
                .ClaimAsync("HT-24422", Request().OwnerId, CancellationToken.None);
        await using var readerScope = host.Services.CreateAsyncScope();
        var storage = readerScope.ServiceProvider.GetRequiredService<TimingDbContext>();
        var saved = await storage.Set<TransponderOwner>().AsNoTracking().SingleAsync();
        Assert.Equal(TransponderOwnershipService.ToDto(saved), Assert.Single(Assert.Single(await ReadEvents()).Changes).After);
    }

    [PostgreSqlOutboxFact]
    public async Task SerializationFailureRollsBackOwnershipBeforeOutboxInsert()
    {
        var sender = new TestSender();
        var host = await StartHost(sender);
        var options = new JsonSerializerOptions(JsonOptions);
        options.Converters.Add(new RejectOwnershipSerialization());
        host.Services.GetRequiredService<JsonMessageSerializer>().Options = options;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await Assert.ThrowsAsync<OwnershipChangeEnqueueException>(() =>
                scope.ServiceProvider.GetRequiredService<TransponderOwnershipService>().SaveAsync(null, Request(), CancellationToken.None));
        }
        Assert.Equal(0, await OwnershipCount());
        Assert.Equal(0, await Scalar<int>("SELECT count(*)::int FROM timing.smb_outbox"));
        Assert.Empty(sender.Delivered);
    }

    [PostgreSqlOutboxFact]
    public async Task OutboxMigrationFailurePreventsHostStartup()
    {
        await ExecuteSql(connectionString, "CREATE TABLE timing.smb_outbox (incompatible integer)");
        var exception = await Assert.ThrowsAnyAsync<MessageBusException>(() => StartHost(new TestSender()));
        Assert.Contains("Outbox schema migration failed", exception.ToString());
    }

    [PostgreSqlOutboxFact]
    public async Task OutboxInsertFailureRollsBackOwnershipAndEvent()
    {
        var sender = new TestSender();
        var host = await StartHost(sender);
        await ExecuteSql(connectionString, """
            CREATE FUNCTION timing.reject_outbox() RETURNS trigger LANGUAGE plpgsql AS
            $$ BEGIN RAISE EXCEPTION 'Simulated outbox insert failure'; END; $$;
            CREATE TRIGGER reject_outbox BEFORE INSERT ON timing.smb_outbox
            FOR EACH ROW EXECUTE FUNCTION timing.reject_outbox();
            """);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var exception = await Assert.ThrowsAsync<OwnershipChangeEnqueueException>(() =>
                scope.ServiceProvider.GetRequiredService<TransponderOwnershipService>().SaveAsync(null, Request(), CancellationToken.None));
            Assert.NotNull(exception.InnerException);
        }
        Assert.Equal(0, await OwnershipCount());
        Assert.Equal(0, await Scalar<int>("SELECT count(*)::int FROM timing.smb_outbox"));
        Assert.Empty(sender.Delivered);
    }

    [PostgreSqlOutboxFact]
    public async Task FailedCommitRollsBackBothOwnershipAndRealOutboxInsert()
    {
        var sender = new TestSender();
        var host = await StartHost(sender);
        commitFailure.Enabled = true;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<TransponderOwnershipService>().SaveAsync(null, Request(), CancellationToken.None));
        }
        Assert.Equal(0, await OwnershipCount());
        Assert.Equal(0, await Scalar<int>("SELECT count(*)::int FROM timing.smb_outbox"));
        Assert.Empty(sender.Delivered);
    }

    [PostgreSqlOutboxFact]
    public async Task UncommittedEventIsInvisibleAndRollbackNeverDelivers()
    {
        var sender = new TestSender();
        var host = await StartHost(sender);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
            await using var transaction = await storage.Database.BeginTransactionAsync();
            var owner = new TransponderOwner
            {
                Id = Guid.NewGuid(), TransponderId = transponderId, OwnerId = Request().OwnerId, OwnedFrom = Request().OwnedFrom.UtcDateTime
            };
            storage.Add(owner);
            await storage.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().Publish(
                new TransponderOwnershipChanged(Guid.NewGuid(), transponderId, [new(null, TransponderOwnershipService.ToDto(owner))]));
            Assert.Equal(1, await storage.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM timing.smb_outbox").SingleAsync());
            Assert.Equal(0, await Scalar<int>("SELECT count(*)::int FROM timing.smb_outbox"));
            await Task.Delay(250);
            Assert.Empty(sender.Delivered);
            await transaction.RollbackAsync();
        }
        Assert.Equal(0, await OwnershipCount());
        Assert.Equal(0, await Scalar<int>("SELECT count(*)::int FROM timing.smb_outbox"));
        Assert.Empty(sender.Delivered);
    }

    [PostgreSqlOutboxFact]
    public async Task CleanupDeletesDeliveredMessagesButPreservesPendingEvents()
    {
        var sender = new TestSender();
        var host = await StartHost(sender);
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TransponderOwnershipService>().SaveAsync(null, Request(), CancellationToken.None);
        await WaitUntil(async () => await Scalar<int>("SELECT count(*)::int FROM timing.smb_outbox WHERE delivery_complete") == 1);
        sender.Available = false;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<TimingDbContext>();
            var owner = await storage.Set<TransponderOwner>().SingleAsync();
            await scope.ServiceProvider.GetRequiredService<TransponderOwnershipService>().SaveAsync(owner.Id,
                Request() with { OwnedTo = Request().OwnedFrom.AddDays(1) }, CancellationToken.None);
        }
        await ExecuteSql(connectionString, "UPDATE timing.smb_outbox SET timestamp = now() - interval '4 days'");
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IOutboxMessageRepository<PostgreSqlOutboxMessage>>();
            Assert.Equal(1, await repository.DeleteSent(DateTimeOffset.UtcNow.AddDays(-3), 100, CancellationToken.None));
        }
        Assert.Equal(1, await Scalar<int>("SELECT count(*)::int FROM timing.smb_outbox WHERE NOT delivery_complete AND NOT delivery_aborted"));
    }

    private SaveTransponderOwnershipRequest Request() => new(transponderId,
        new Guid("309cfd07-1517-4e8d-a5e4-d3a233791285"), new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), null);

    private async Task<IHost> StartHost(TestSender sender)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MessageBus"] = "Endpoint=sb://localhost/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA=="
        });
        builder.Services.AddDbContext<TimingDbContext>(options => options.UseNpgsql(connectionString).AddInterceptors(commitFailure));
        builder.Services.AddScoped<TransponderOwnershipService>();
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

    private async Task<int> OwnershipCount()
    {
        await using var storage = new TimingDbContext(new DbContextOptionsBuilder<TimingDbContext>().UseNpgsql(connectionString).Options);
        return await storage.Set<TransponderOwner>().CountAsync();
    }

    private async Task<List<TransponderOwnershipChanged>> ReadEvents()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT message_payload FROM timing.smb_outbox ORDER BY timestamp", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var events = new List<TransponderOwnershipChanged>();
        while (await reader.ReadAsync())
            events.Add(JsonSerializer.Deserialize<TransponderOwnershipChanged>(reader.GetFieldValue<byte[]>(0), JsonOptions)!);
        return events;
    }

    private async Task<T> Scalar<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteSql(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
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

    private sealed class RejectOwnershipSerialization : JsonConverter<TransponderOwnershipChanged>
    {
        public override TransponderOwnershipChanged? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, TransponderOwnershipChanged value, JsonSerializerOptions options) =>
            throw new JsonException("Simulated serialization failure.");
    }

    private sealed class TestSender : ServiceBusSender
    {
        private readonly ConcurrentDictionary<ServiceBusMessageBatch, List<ServiceBusMessage>> batches = new();
        public bool Available { get; set; } = true;
        public ConcurrentQueue<TransponderOwnershipChanged> Delivered { get; } = new();
        public ConcurrentQueue<ServiceBusMessage> TransportMessages { get; } = new();

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
            await Task.Delay(100, cancellationToken);
            if (!Available) throw new ServiceBusException("Simulated broker outage.", ServiceBusFailureReason.ServiceBusy);
            foreach (var message in messages)
            {
                TransportMessages.Enqueue(message);
                Delivered.Enqueue(JsonSerializer.Deserialize<TransponderOwnershipChanged>(message.Body.ToString(), JsonOptions)!);
            }
            batches.TryRemove(messageBatch, out _);
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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
                if (IsEnabled(logLevel)) errors.Enqueue($"{formatter(state, exception)} {exception}");
            }
        }
    }
}
