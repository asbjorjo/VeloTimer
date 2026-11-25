using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using SlimMessageBus;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Outbox;
using SlimMessageBus.Host.Outbox.PostgreSql;
using SlimMessageBus.Host.Outbox.PostgreSql.Configuration;
using SlimMessageBus.Host.Outbox.PostgreSql.DbContext;
using SlimMessageBus.Host.Outbox.PostgreSql.Transactions;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;
using Assert = Xunit.Assert;

namespace VeloTime.Workers.Test;

/// <summary>
/// Prerequisite experiment, NOT proposed production registration. Real processor producers,
/// serializer, EF context, SMB persistence/dispatcher and PostgreSQL; only Azure's sender is fake.
/// Each test owns a new database. No host workers or Azure consumers are started.
/// </summary>
public sealed partial class StatisticsSmbOutboxProofTests : IAsyncLifetime
{
    private const string Schema = "statistics_processor_proof";
    private const string ReceiptHeader = "StatisticsReceiptId";
    private static readonly DateTime Start = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
    private readonly string database = $"smb_proof_{Guid.NewGuid():N}";
    private readonly ControlledSender sender = new();
    private readonly RejectCommit rejectCommit = new();
    private string admin = "";
    private string connection = "";
    private ServiceProvider? provider;
    private IMasterMessageBus? master;
    private bool disposed;
    private IMasterMessageBus Bus => master ??= provider!.GetRequiredService<IMasterMessageBus>();

    public async Task InitializeAsync()
    {
        try { await InitializeCore(); }
        catch { await DisposeAsync(); throw; }
    }

    private async Task InitializeCore()
    {
        admin = Environment.GetEnvironmentVariable("VELOTIME_TEST_STATISTICS_POSTGRES") ?? "";
        if (string.IsNullOrWhiteSpace(admin)) return;
        await Sql(admin, $"CREATE DATABASE \"{database}\"");
        connection = new NpgsqlConnectionStringBuilder(admin) { Database = database }.ConnectionString;
        await using (var db = Db()) await db.Database.MigrateAsync();
        // SMB migrates its tables, but does not create DatabaseSchemaName.
        await Sql(connection, $"CREATE SCHEMA {Schema}");

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:MessageBus"] = "Endpoint=sb://localhost/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==";
        builder.Configuration["ConnectionStrings:keycloak"] = "https://identity.test.invalid/realms/velotime";
        builder.Configuration["ConnectionStrings:velotimedb"] = connection;
        builder.Configuration["ConnectionStrings:cache"] = "localhost:6379";
        var startup = typeof(TimingSampleHandler).Assembly.GetType("StartupExtensions")!;
        startup.GetMethod("AddServices", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [builder]);
        builder.Services.ConfigureDbContext<StatisticsDbContext>((_, options) => options.AddInterceptors(rejectCommit));
        // Re-enter AddSlimMessageBus so newly added plugins register their DI services as well.
        builder.Services.AddSlimMessageBus(mbb =>
        {
            mbb.AutoStartConsumersEnabled(false);
            mbb.WithProviderServiceBus(options =>
            {
                options.ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==";
                options.TopologyProvisioning.Enabled = false;
                options.SenderFactory = (path, _) =>
                {
                    Assert.Equal("velotime-statistics", path);
                    return sender;
                };
            });
            // Deliberately isolated from AddStatisticsOutbox / statistics.smb_outbox.
            // Concrete, individual messages only; no IEnumerable/collection envelope registration.
            mbb.UseOutbox(true, type => type == typeof(SampleComplete) || type == typeof(EntryCreated));
            mbb.AddOutboxUsingDbContext<StatisticsDbContext>(options =>
            {
                options.PostgreSqlSettings.DatabaseSchemaName = Schema;
                options.MaintainSequence = true;
                options.PollBatchSize = 1;
                options.PollIdleSleep = TimeSpan.FromMilliseconds(30);
                options.MaxDeliveryAttempts = int.MaxValue;
                options.MessageCleanup = new OutboxMessageCleanupSettings { Enabled = false };
            });
        });
        provider = builder.Services.BuildServiceProvider();
        // AutoStartConsumersEnabled(false) prevents automatic start, not an explicit Start().
        // Remove inbound subscriptions from this producer-only experiment before starting SMB.
        Assert.IsAssignableFrom<MessageBusBase>(Bus).Settings.Consumers.Clear();
        await Bus.Start();
    }

    [PostgreSqlStatisticsFact]
    public async Task ScopedReceiptAndSmbInsertCommitAndRollbackTogetherIncludingEnqueueAndCommitFailures()
    {
        await using (var scope = provider!.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
            var repository = Assert.IsType<DbContextOutboxRepository<StatisticsDbContext>>(
                scope.ServiceProvider.GetRequiredService<IOutboxMessageFactory>());
            var transactions = Assert.IsType<DbContextTransactionService<StatisticsDbContext>>(
                scope.ServiceProvider.GetRequiredService<IPostgreSqlTransactionService>());
            Assert.Same(db, repository.DbContext);
            Assert.Same(db, transactions.DbContext);
            await using var tx = await db.Database.BeginTransactionAsync();
            var receipt = Receipt();
            db.Add(receipt);
            await db.SaveChangesAsync();
            await Publish(scope.ServiceProvider, Message(receipt), receipt.SampleId);
            Assert.Same(db.Database.GetDbConnection(), repository.Connection);
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                $"SELECT count(*)::int AS \"Value\" FROM {Schema}.smb_outbox").SingleAsync());
            Assert.Equal(0, await Count());
            await using (var outside = Db()) Assert.Empty(await outside.Set<SampleCompleteOutbox>().ToListAsync());
            await tx.RollbackAsync();
        }
        Assert.Equal(0, await Count());
        var committed = Receipt();
        await Enqueue(committed);
        Assert.Equal(1, await Count());
        await using (var outside = Db()) Assert.Equal(committed.SampleId, (await outside.Set<SampleCompleteOutbox>().SingleAsync()).SampleId);

        rejectCommit.Enabled = true;
        try { await Assert.ThrowsAsync<InvalidOperationException>(() => Enqueue(Receipt())); }
        finally { rejectCommit.Enabled = false; }
        Assert.Equal(1, await Count());
        await using (var db = Db()) Assert.Equal(1, await db.Set<SampleCompleteOutbox>().CountAsync());

        await Sql(connection, $"ALTER TABLE {Schema}.smb_outbox ADD CONSTRAINT reject_insert CHECK (false) NOT VALID");
        await Assert.ThrowsAnyAsync<Exception>(() => Enqueue(Receipt()));
        Assert.Equal(1, await Count());
        await using (var db = Db()) Assert.Equal(1, await db.Set<SampleCompleteOutbox>().CountAsync());
        Assert.Empty(sender.Accepted);
    }

    [PostgreSqlStatisticsFact]
    public async Task ActualTransportPreservesIndividualPayloadsHeadersAndPendingApiOutbox()
    {
        // An independent API bus uses the unchanged production API extension in the SAME database.
        var apiServices = Host.CreateApplicationBuilder().Services;
        apiServices.AddDbContext<StatisticsDbContext>(o => o.UseNpgsql(connection).UseSnakeCaseNamingConvention());
        apiServices.AddSlimMessageBus(mbb =>
        {
            mbb.WithProviderServiceBus(o =>
            {
                o.ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==";
                o.TopologyProvisioning.Enabled = false;
                o.SenderFactory = (_, _) => throw new InvalidOperationException("API must not dispatch in this proof");
            });
            mbb.AddJsonSerializer();
            mbb.Produce<StatisticsValidityThresholdsChangedV1>(p => p.DefaultTopic("velotime-statistics"));
            mbb.AddStatisticsOutbox();
        });
        await using var api = apiServices.BuildServiceProvider();
        var apiBus = api.GetRequiredService<IMasterMessageBus>();
        await apiBus.Start();
        await apiBus.Stop();
        var threshold = new StatisticsValidityThresholdsChangedV1(Guid.NewGuid(), Guid.NewGuid(), null);
        await using (var scope = api.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().Publish(threshold);
            await tx.CommitAsync();
        }
        var apiBefore = await Scalar<string>("SELECT row_to_json(o)::text FROM statistics.smb_outbox o");
        var settings = provider!.GetRequiredService<PostgreSqlOutboxSettings>();
        Assert.Equal(Schema, settings.PostgreSqlSettings.DatabaseSchemaName);
        Assert.True(settings.MaintainSequence);
        Assert.Equal(1, settings.PollBatchSize);
        Assert.False(settings.MessageCleanup.Enabled);

        var receipt = Receipt();
        await Enqueue(receipt);
        var entry = new EntryCreated(receipt.TransponderId, Start, Start.AddSeconds(1),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var derived = entry with { EntryId = Guid.NewGuid() };
        await EnqueueEntry(entry, single: true);
        await EnqueueEntry(derived, single: false);
        Assert.Equal(3, await Count());
        sender.Available = true;
        await Until(async () => await DeliveredCount() == 3);
        var messages = sender.Accepted.ToArray();
        Assert.Equal(3, messages.Length);
        Assert.All(sender.BatchSizes, size => Assert.Equal(1, size));
        Assert.Equal(receipt.TransponderId.ToString(), messages[0].SessionId);
        Assert.Equal(nameof(SampleComplete), messages[0].ApplicationProperties["MessageName"]);
        Assert.False(messages[0].ApplicationProperties.ContainsKey("Single"));
        Assert.Equal(Message(receipt), JsonSerializer.Deserialize<SampleComplete>(messages[0].Body.ToString(), JsonOptions));
        Assert.Equal(entry.TransponderId.ToString(), messages[1].SessionId);
        Assert.Equal(nameof(EntryCreated), messages[1].ApplicationProperties["MessageName"]);
        Assert.Equal(true, messages[1].ApplicationProperties["Single"]);
        Assert.Equal(entry, JsonSerializer.Deserialize<EntryCreated>(messages[1].Body.ToString(), JsonOptions));
        Assert.Equal(derived.TransponderId.ToString(), messages[2].SessionId);
        Assert.Equal(nameof(EntryCreated), messages[2].ApplicationProperties["MessageName"]);
        Assert.False(messages[2].ApplicationProperties.ContainsKey("Single"));
        Assert.Equal(derived, JsonSerializer.Deserialize<EntryCreated>(messages[2].Body.ToString(), JsonOptions));
        foreach (var message in messages)
        {
            var id = Guid.Parse((string)message.ApplicationProperties[ReceiptHeader]);
            Assert.True(await DeliveryStatus(id, (string)message.ApplicationProperties["MessageName"]));
        }
        Assert.Equal(apiBefore, await Scalar<string>("SELECT row_to_json(o)::text FROM statistics.smb_outbox o"));
        Assert.Equal(3, await Count()); // processor cleanup disabled
    }

    [PostgreSqlStatisticsFact]
    public async Task ConsumerCanCommitProcessingBeforeSenderAcknowledgementAndDeliveryMark()
    {
        var receipt = Receipt();
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sender.OnAccepted = async message =>
        {
            try
            {
                Assert.Equal(receipt.SampleId.ToString(), message.ApplicationProperties[ReceiptHeader]);
                Assert.False(await DeliveryStatus(receipt.SampleId));
                // Controlled consumer processing in a fresh scoped processor DbContext/transaction.
                // This is a timing probe, not a replacement for SampleCompleteHandler's domain rules.
                await using var scope = provider!.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
                await using var tx = await db.Database.BeginTransactionAsync();
                var source = await db.Set<SampleCompleteOutbox>().SingleAsync(r => r.SampleId == receipt.SampleId);
                source.EntriesCreatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                processed.TrySetResult();
                await releaseAck.Task.WaitAsync(TimeSpan.FromSeconds(20));
            }
            catch (Exception ex) { processed.TrySetException(ex); throw; }
        };
        await Enqueue(receipt);
        sender.Available = true;
        try
        {
            await processed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(await DeliveryStatus(receipt.SampleId));
            await using var db = Db();
            Assert.NotNull((await db.Set<SampleCompleteOutbox>().SingleAsync()).EntriesCreatedAtUtc);
        }
        finally { releaseAck.TrySetResult(); }
        await Until(() => DeliveryStatus(receipt.SampleId));
    }

    [PostgreSqlStatisticsFact]
    public async Task LostAcknowledgementRetriesSameCorrelationAfterRestartWithoutMarkingSuccess()
    {
        var receipt = Receipt();
        await Enqueue(receipt);
        sender.FailAfterAccept = true;
        sender.Available = true;
        await Until(async () => sender.Accepted.Count > 0 &&
            await Scalar<int>($"SELECT delivery_attempt FROM {Schema}.smb_outbox") > 0);
        await Bus.Stop();
        Assert.False(await DeliveryStatus(receipt.SampleId));
        Assert.Equal(0, await Scalar<int>($"SELECT count(*)::int FROM {Schema}.smb_outbox WHERE delivery_aborted"));
        sender.FailAfterAccept = false;
        await Bus.Start();
        await Until(() => DeliveryStatus(receipt.SampleId));
        Assert.True(sender.Accepted.Count >= 2);
        Assert.All(sender.Accepted, m =>
        {
            Assert.Equal(receipt.SampleId.ToString(), m.ApplicationProperties[ReceiptHeader]);
            Assert.Equal(receipt.TransponderId.ToString(), m.SessionId);
            Assert.Equal(nameof(SampleComplete), m.ApplicationProperties["MessageName"]);
            Assert.Equal(Message(receipt), JsonSerializer.Deserialize<SampleComplete>(m.Body.ToString(), JsonOptions));
        });
        Assert.Equal(1, await Count());
    }

    [PostgreSqlStatisticsFact]
    public async Task StockSequenceDoesNotWaitForPredecessorDomainProcessing()
    {
        var first = Receipt();
        var second = Receipt(first.TransponderId);
        await Enqueue(first);
        await Enqueue(second);
        sender.Available = true; // accepted/acknowledged, deliberately never consumed
        await Until(async () => await DeliveredCount() == 2);
        await using var db = Db();
        Assert.All(await db.Set<SampleCompleteOutbox>().ToListAsync(), r => Assert.Null(r.EntriesCreatedAtUtc));
        Assert.Equal(new[] { first.SampleId.ToString(), second.SampleId.ToString() },
            sender.Accepted.Select(m => (string)m.ApplicationProperties[ReceiptHeader]).ToArray());
        // Positive evidence of the missing processing barrier, NOT an assertion that replacement is safe.
        var selection = typeof(IOutboxMessageRepository<PostgreSqlOutboxMessage>).GetMethod("LockAndSelect")!;
        Assert.Equal(new[] { typeof(string), typeof(int), typeof(bool), typeof(TimeSpan), typeof(CancellationToken) },
            selection.GetParameters().Select(p => p.ParameterType));
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static SampleCompleteOutbox Receipt(Guid? rider = null) => new()
    {
        SampleId = Guid.NewGuid(), TransponderId = rider ?? Guid.NewGuid(),
        TimeStart = Start, TimeEnd = Start.AddSeconds(1),
        CoursePointStart = Guid.NewGuid(), CoursePointEnd = Guid.NewGuid(), Distance = 250
    };
    private static SampleComplete Message(SampleCompleteOutbox row) => new(row.TransponderId, row.TimeStart,
        row.TimeEnd, row.CoursePointStart, row.CoursePointEnd, row.Distance, row.SampleId);
    private static Task Publish<T>(IServiceProvider services, T message, Guid receipt, bool single = false)
    {
        var headers = new Dictionary<string, object> { [ReceiptHeader] = receipt.ToString() };
        if (single) headers["Single"] = true;
        return services.GetRequiredService<IMessageBus>().Publish(message, headers: headers);
    }
    private async Task Enqueue(SampleCompleteOutbox receipt)
    {
        await using var scope = provider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        db.Add(receipt);
        await db.SaveChangesAsync();
        await Publish(scope.ServiceProvider, Message(receipt), receipt.SampleId);
        await tx.CommitAsync();
    }
    private async Task EnqueueEntry(EntryCreated entry, bool single)
    {
        await using var scope = provider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        db.Add(new EntryCreatedOutbox
        {
            EntryId = entry.EntryId!.Value, TransponderId = entry.TransponderId,
            TimeStart = entry.TimeStart, TimeEnd = entry.TimeEnd, StatisticsItemId = entry.StatisticsItemId,
            ConfigItemId = entry.ConfigItemId, Single = single, MultiDecisionVersion = 1
        });
        await db.SaveChangesAsync();
        await Publish(scope.ServiceProvider, entry, entry.EntryId.Value, single);
        await tx.CommitAsync();
    }
    private Task<int> Count() => Scalar<int>($"SELECT count(*)::int FROM {Schema}.smb_outbox");
    private Task<int> DeliveredCount() => Scalar<int>($"SELECT count(*)::int FROM {Schema}.smb_outbox WHERE delivery_complete");
    private async Task<bool> DeliveryStatus(Guid receipt, string messageName = nameof(SampleComplete))
    {
        await using var db = Db();
        // SingleAsync deliberately fails on missing OR ambiguous evidence.
        return await db.Database.SqlQuery<bool>($"""
            SELECT delivery_complete AS "Value" FROM statistics_processor_proof.smb_outbox
            WHERE headers ->> 'StatisticsReceiptId' = {receipt.ToString()}
              AND headers ->> 'MessageName' = {messageName}
            """).SingleAsync();
    }
    private async Task<T> Scalar<T>(string sql)
    {
        await using var db = Db();
        var query = $"SELECT ({sql}) AS \"Value\""; // private helper; only fixture-owned SQL literals
        return await db.Database.SqlQueryRaw<T>(query).SingleAsync();
    }
    private StatisticsDbContext Db() => new(new DbContextOptionsBuilder<StatisticsDbContext>()
        .UseNpgsql(connection, o => o.MigrationsHistoryTable("__ef_migrations_history", "ef"))
        .UseSnakeCaseNamingConvention().Options);
    private static async Task Sql(string connectionString, string sql)
    {
        await using var db = new NpgsqlConnection(connectionString);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand(sql, db);
        await command.ExecuteNonQueryAsync();
    }
    private static async Task Until(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!await condition()) await Task.Delay(30, timeout.Token);
    }
    public async Task DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            try { if (master is not null) await master.Stop(); }
            finally { if (provider is not null) await provider.DisposeAsync(); }
        }
        finally
        {
            if (connection.Length != 0)
            {
                using var pool = new NpgsqlConnection(connection);
                NpgsqlConnection.ClearPool(pool);
                await Sql(admin, $"DROP DATABASE \"{database}\" WITH (FORCE)");
            }
        }
    }
    private sealed class RejectCommit : DbTransactionInterceptor
    {
        public bool Enabled;
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Enabled) throw new InvalidOperationException("injected commit failure");
            return ValueTask.FromResult(result);
        }
    }
    private sealed class ControlledSender : ServiceBusSender
    {
        private readonly ConcurrentDictionary<ServiceBusMessageBatch, List<ServiceBusMessage>> batches = new();
        public volatile bool Available;
        public volatile bool FailAfterAccept;
        public Func<ServiceBusMessage, Task>? OnAccepted;
        public ConcurrentQueue<ServiceBusMessage> Accepted { get; } = new();
        public ConcurrentQueue<int> BatchSizes { get; } = new();
        public override ValueTask<ServiceBusMessageBatch> CreateMessageBatchAsync(CancellationToken cancellationToken = default)
        {
            var messages = new List<ServiceBusMessage>();
            var batch = ServiceBusModelFactory.ServiceBusMessageBatch(0, messages, tryAddCallback: _ => true);
            batches[batch] = messages;
            return ValueTask.FromResult(batch);
        }
        public override async Task SendMessagesAsync(ServiceBusMessageBatch batch, CancellationToken cancellationToken = default)
        {
            // SMB's transport retry can reuse a batch after a failed acknowledgement.
            var messages = batches[batch];
            if (!Available) throw new ServiceBusException("controlled outage", ServiceBusFailureReason.ServiceBusy);
            BatchSizes.Enqueue(messages.Count);
            foreach (var message in messages)
            {
                Accepted.Enqueue(message);
                if (OnAccepted is not null) await OnAccepted(message);
            }
            if (FailAfterAccept) throw new ServiceBusException("accepted but acknowledgement lost", ServiceBusFailureReason.ServiceBusy);
        }
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
