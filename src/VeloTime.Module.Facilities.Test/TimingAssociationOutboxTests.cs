using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
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
using SlimMessageBus.Host.Outbox.PostgreSql.Configuration;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Facilities.Api;
using VeloTime.Module.Facilities.Interface.Messages;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;
using Assert = Xunit.Assert;

namespace VeloTime.Module.Facilities.Test;

public sealed class FacilitiesOutboxPostgreSqlFactAttribute : FactAttribute
{
    public FacilitiesOutboxPostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FACILITIES_TEST_POSTGRES_ADMIN_CONNECTION")))
            Skip = "Set FACILITIES_TEST_POSTGRES_ADMIN_CONNECTION (test-only Database=postgres, create/drop privileges).";
    }
}

public sealed class TimingAssociationOutboxTests : IAsyncLifetime
{
    private readonly string databaseName = $"facilities_outbox_{Guid.NewGuid():N}";
    private readonly List<IHost> hosts = [];
    private string adminConnection = "";
    private string connectionString = "";
    private readonly Guid facilityId = Guid.NewGuid();
    private readonly Guid coursePointId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        adminConnection = Environment.GetEnvironmentVariable("FACILITIES_TEST_POSTGRES_ADMIN_CONNECTION") ?? "";
        if (string.IsNullOrWhiteSpace(adminConnection)) return;
        if (new NpgsqlConnectionStringBuilder(adminConnection).Database != "postgres")
            throw new InvalidOperationException("Outbox tests require a test-only admin connection with Database=postgres.");
        await Sql(adminConnection, $"CREATE DATABASE \"{databaseName}\" TEMPLATE template0");
        connectionString = new NpgsqlConnectionStringBuilder(adminConnection) { Database = databaseName, Pooling = false }.ConnectionString;
        await using var storage = Storage();
        await storage.Database.MigrateAsync();
        storage.Add(new Facility { Id = facilityId });
        storage.Add(new CoursePoint { Id = coursePointId });
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
            await Sql(adminConnection, $"DROP DATABASE \"{databaseName}\" WITH (FORCE)");
    }

    [FacilitiesOutboxPostgreSqlFact]
    public async Task OutageAndRestartRecoverStablePayloadInOrderAndRetainDispatchedRows()
    {
        var sender = new ControlledSender { Available = false };
        var firstHost = await StartHost(sender);
        var first = await CommitEvent(firstHost);
        await WaitUntil(async () => await Scalar("SELECT count(*)::int FROM facility.smb_outbox WHERE delivery_attempt > 0") == 1);
        var second = await CommitEvent(firstHost);
        var expected = await Payloads();
        Assert.Equal(2, expected.Count);
        Assert.Equal(2, await AssociationCount());
        Assert.Empty(sender.Delivered);
        await firstHost.StopAsync();

        sender.Available = true;
        await StartHost(sender);
        await StartHost(sender);
        await WaitUntil(async () => await Scalar("SELECT count(*)::int FROM facility.smb_outbox WHERE delivery_complete") == 2);
        Assert.Equal(new[] { first.EventId.ToString(), second.EventId.ToString() },
            sender.Delivered.Select(message => message.MessageId));
        Assert.Equal(expected, sender.Delivered.Select(message => message.Body.ToString()));
        Assert.All(sender.Attempts, message =>
        {
            var payload = JsonSerializer.Deserialize<CoursePointTimingAssociationsChangedV1>(message.Body.ToString(),
                TimingAssociationMessagingTests.JsonOptions)!;
            Assert.Equal(payload.EventId.ToString(), message.MessageId);
            Assert.Equal(facilityId.ToString(), message.SessionId);
            Assert.Equal(nameof(CoursePointTimingAssociationsChangedV1), message.ApplicationProperties["MessageName"]);
            Assert.NotEqual(Guid.Empty, payload.ChangeId);
            Assert.Equal(TimeSpan.Zero, payload.OccurredAtUtc.Offset);
            Assert.Contains(message.Body.ToString(), expected);
        });
        await Sql(connectionString, "UPDATE facility.smb_outbox SET timestamp = now() - interval '30 days'");
        await Task.Delay(250);
        Assert.Equal(2, await Scalar("SELECT count(*)::int FROM facility.smb_outbox WHERE delivery_complete"));
    }

    [FacilitiesOutboxPostgreSqlFact]
    public async Task LostAcknowledgementRetriesSameEventAndChangeIdsWithoutChangingPayload()
    {
        var sender = new ControlledSender { LoseAcknowledgementOnce = true };
        var host = await StartHost(sender);
        var message = await CommitEvent(host);
        await WaitUntil(async () => await Scalar("SELECT count(*)::int FROM facility.smb_outbox WHERE delivery_complete") == 1);
        Assert.Equal(2, sender.Delivered.Count);
        Assert.All(sender.Delivered, delivered =>
        {
            Assert.Equal(message.EventId.ToString(), delivered.MessageId);
            Assert.Equal(facilityId.ToString(), delivered.SessionId);
            var payload = JsonSerializer.Deserialize<CoursePointTimingAssociationsChangedV1>(delivered.Body.ToString(),
                TimingAssociationMessagingTests.JsonOptions)!;
            Assert.Equal(message.ChangeId, payload.ChangeId);
        });
        Assert.Single(sender.Delivered.Select(delivered => delivered.Body.ToString()).Distinct());
        Assert.Equal(1, await AssociationCount());
        Assert.Equal(1, await Scalar("SELECT count(*)::int FROM facility.smb_outbox"));
    }

    [FacilitiesOutboxPostgreSqlFact]
    public async Task UncommittedMappingAndEventAreInvisibleAndRollbackNeverDelivers()
    {
        var sender = new ControlledSender();
        var host = await StartHost(sender);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
            await using var transaction = await storage.Database.BeginTransactionAsync();
            await SaveAndEnqueue(scope.ServiceProvider);
            Assert.Equal(1, await storage.Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM facility.smb_outbox").SingleAsync());
            Assert.Equal(0, await Scalar("SELECT count(*)::int FROM facility.smb_outbox"));
            Assert.Equal(0, await AssociationCount());
            await Task.Delay(250);
            Assert.Empty(sender.Delivered);
            await transaction.RollbackAsync();
        }
        Assert.Equal(0, await AssociationCount());
        Assert.Equal(0, await Scalar("SELECT count(*)::int FROM facility.smb_outbox"));
        Assert.Empty(sender.Delivered);
    }

    [FacilitiesOutboxPostgreSqlFact]
    public async Task SerializationAndOutboxInsertFailuresRollBackMappingsAndEvents()
    {
        var sender = new ControlledSender();
        var host = await StartHost(sender);
        var serializer = host.Services.GetRequiredService<JsonMessageSerializer>();
        var originalOptions = serializer.Options;
        var rejecting = new JsonSerializerOptions(originalOptions);
        rejecting.Converters.Add(new RejectSerialization());
        serializer.Options = rejecting;
        var serializationFailure = await Assert.ThrowsAnyAsync<Exception>(() => CommitEvent(host));
        Assert.Contains("Simulated serialization failure", serializationFailure.ToString());
        Assert.Equal(0, await AssociationCount());
        Assert.Equal(0, await Scalar("SELECT count(*)::int FROM facility.smb_outbox"));
        serializer.Options = originalOptions;
        await Sql(connectionString, """
            CREATE FUNCTION facility.reject_outbox() RETURNS trigger LANGUAGE plpgsql AS
            $$ BEGIN RAISE EXCEPTION 'Simulated outbox insert failure'; END; $$;
            CREATE TRIGGER reject_outbox BEFORE INSERT ON facility.smb_outbox
            FOR EACH ROW EXECUTE FUNCTION facility.reject_outbox();
            """);
        var insertFailure = await Assert.ThrowsAnyAsync<Exception>(() => CommitEvent(host));
        Assert.Contains("Simulated outbox insert failure", insertFailure.ToString());
        Assert.Equal(0, await AssociationCount());
        Assert.Equal(0, await Scalar("SELECT count(*)::int FROM facility.smb_outbox"));
        Assert.Empty(sender.Delivered);
    }

    [FacilitiesOutboxPostgreSqlFact]
    public async Task DeferredConstraintCommitFailureRollsBackMappingAndInsertedOutboxEvents()
    {
        var sender = new ControlledSender();
        var host = await StartHost(sender);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
            await using var transaction = await storage.Database.BeginTransactionAsync();
            await storage.Database.ExecuteSqlRawAsync("""
                SET CONSTRAINTS facility.ex_timing_association_timing_point,
                    facility.ex_timing_association_course_point DEFERRED
                """);
            await SaveAndEnqueue(scope.ServiceProvider);
            await SaveAndEnqueue(scope.ServiceProvider); // Same coursepoint, overlapping all-time intervals.
            Assert.Equal(2, await storage.Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM facility.smb_outbox").SingleAsync());
            var failure = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
            Assert.Equal("23P01", failure.SqlState);
        }
        Assert.Equal(0, await AssociationCount());
        Assert.Equal(0, await Scalar("SELECT count(*)::int FROM facility.smb_outbox"));
        Assert.Empty(sender.Delivered);
    }

    [FacilitiesOutboxPostgreSqlFact]
    public async Task OutboxSchemaInitializationFailurePreventsStartup()
    {
        await Sql(connectionString, "CREATE TABLE facility.smb_outbox (incompatible integer)");
        var failure = await Assert.ThrowsAnyAsync<MessageBusException>(() => StartHost(new ControlledSender()));
        Assert.Contains("Outbox schema migration failed", failure.ToString());
    }

    [FacilitiesOutboxPostgreSqlFact]
    public async Task ProductionPublisherRequiresCallerTransactionAndPropagatesCancellation()
    {
        var host = await StartHost(new ControlledSender());
        await using var scope = host.Services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<ITimingAssociationEventPublisher>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.EnqueueAsync(TimingAssociationMessagingTests.Event()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            publisher.EnqueueAsync(TimingAssociationMessagingTests.Event(), new CancellationToken(true)));
        Assert.Equal(0, await Scalar("SELECT count(*)::int FROM facility.smb_outbox"));
    }

    private async Task<CoursePointTimingAssociationsChangedV1> CommitEvent(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
        await using var transaction = await storage.Database.BeginTransactionAsync();
        var message = await SaveAndEnqueue(scope.ServiceProvider, nonOverlapping: true);
        await transaction.CommitAsync();
        return message;
    }

    private async Task<CoursePointTimingAssociationsChangedV1> SaveAndEnqueue(IServiceProvider services, bool nonOverlapping = false)
    {
        var storage = services.GetRequiredService<FacilityDbContext>();
        var count = await storage.Set<CoursePointTimingAssociation>().CountAsync();
        var association = new CoursePointTimingAssociation
        {
            Id = Guid.NewGuid(), CoursePointId = coursePointId, TimingPointId = Guid.NewGuid(),
            ValidFromUtc = nonOverlapping ? DateTimeOffset.UnixEpoch.AddDays(count) : null,
            ValidToUtc = nonOverlapping ? DateTimeOffset.UnixEpoch.AddDays(count + 1) : null
        };
        storage.Add(association);
        await storage.SaveChangesAsync();
        await storage.Entry(association).ReloadAsync(); // Event snapshots use PostgreSQL's persisted precision.
        var message = new CoursePointTimingAssociationsChangedV1(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            facilityId, [new(null, new(association.TimingPointId, coursePointId, association.ValidFromUtc, association.ValidToUtc))]);
        await services.GetRequiredService<ITimingAssociationEventPublisher>().EnqueueAsync(message);
        return message;
    }

    private async Task<IHost> StartHost(ControlledSender sender)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration["ConnectionStrings:MessageBus"] = TimingAssociationMessagingTests.TestBusConnection;
        builder.Services.AddDbContext<FacilityDbContext>(options => options
            .UseNpgsql(connectionString, pg => pg.MigrationsHistoryTable("__ef_migrations_history", "ef"))
            .UseSnakeCaseNamingConvention());
        builder.AddTimingAssociationMessaging();
        var busBuilder = (MessageBusBuilder)builder.Services.Single(service => service.ServiceType == typeof(MessageBusBuilder)).ImplementationInstance!;
        busBuilder.WithProviderServiceBus(options =>
        {
            options.ConnectionString = TimingAssociationMessagingTests.TestBusConnection;
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

    private FacilityDbContext Storage() => new(new DbContextOptionsBuilder<FacilityDbContext>()
        .UseNpgsql(connectionString, pg => pg.MigrationsHistoryTable("__ef_migrations_history", "ef"))
        .UseSnakeCaseNamingConvention().Options);

    private async Task<int> AssociationCount()
    {
        await using var storage = Storage();
        return await storage.Set<CoursePointTimingAssociation>().CountAsync();
    }

    private async Task<List<string>> Payloads()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT message_payload FROM facility.smb_outbox ORDER BY timestamp", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var payloads = new List<string>();
        while (await reader.ReadAsync())
            payloads.Add(System.Text.Encoding.UTF8.GetString(reader.GetFieldValue<byte[]>(0)));
        return payloads;
    }

    private async Task<int> Scalar(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task Sql(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!await condition()) await Task.Delay(50, timeout.Token);
    }

    private sealed class RejectSerialization : JsonConverter<CoursePointTimingAssociationsChangedV1>
    {
        public override CoursePointTimingAssociationsChangedV1? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, CoursePointTimingAssociationsChangedV1 value, JsonSerializerOptions options) =>
            throw new JsonException("Simulated serialization failure.");
    }

    private sealed class ControlledSender : ServiceBusSender
    {
        private readonly ConcurrentDictionary<ServiceBusMessageBatch, List<ServiceBusMessage>> batches = new();
        public bool Available { get; set; } = true;
        public bool LoseAcknowledgementOnce { get; set; }
        public ConcurrentQueue<ServiceBusMessage> Delivered { get; } = new();
        public ConcurrentQueue<ServiceBusMessage> Attempts { get; } = new();

        public override ValueTask<ServiceBusMessageBatch> CreateMessageBatchAsync(CancellationToken cancellationToken = default)
        {
            var messages = new List<ServiceBusMessage>();
            var batch = ServiceBusModelFactory.ServiceBusMessageBatch(0, messages, tryAddCallback: _ => true);
            batches[batch] = messages;
            return ValueTask.FromResult(batch);
        }

        public override async Task SendMessagesAsync(ServiceBusMessageBatch batch, CancellationToken cancellationToken = default)
        {
            var messages = batches[batch];
            batches.TryRemove(batch, out _);
            await Task.Delay(100, cancellationToken);
            foreach (var message in messages) Attempts.Enqueue(message);
            if (!Available) throw new ServiceBusException("Simulated outage", ServiceBusFailureReason.ServiceBusy);
            foreach (var message in messages) Delivered.Enqueue(message);
            if (LoseAcknowledgementOnce)
            {
                LoseAcknowledgementOnce = false;
                throw new ServiceBusException("Simulated lost acknowledgement", ServiceBusFailureReason.ServiceBusy);
            }
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
