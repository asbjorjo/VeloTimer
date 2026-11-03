using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Outbox.PostgreSql.Configuration;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Statistics.Api;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;
using Assert = Xunit.Assert;

namespace VeloTime.Module.Statistics.Api.Test;

/// <summary>Actual endpoint, guarded publisher and EF outbox, using a synthetic sender only.</summary>
public sealed class StatisticsThresholdPostgreSqlTests
{
    [PublicStatisticsPostgreSqlFact]
    public async Task SaveIsMetadataOnlyAtomicAndNoopPreservesGenerationWith4096RetainedRows()
    {
        await using var harness = await Harness.Create();
        var item = await harness.CreateItem();
        var config = await harness.CreateConfig(item.Id);
        await harness.Host.SeedAsync(Enumerable.Range(0, 4096).Select(i => (object)Entry(config, i)).ToArray());
        harness.Host.Sql.Commands.Clear();
        var request = new SaveStatisticsItemRequest("Metadata", "", 250, TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(20));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(HttpStatusCode.OK, (await harness.Client.PutAsJsonAsync(ItemUrl(item.Id), request)).StatusCode);
        watch.Stop();
        Assert.DoesNotContain(harness.Host.Sql.Commands, c => c.Text.Contains("statistics_entry", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, await harness.OutboxCount());
        var job = await harness.Job(item.Id);
        Assert.True(job.IsPending);
        Assert.Null(job.LastEntryId);
        Assert.Equal(StatisticsValidityRevalidationPhase.Simple, job.Phase);
        await using (var scope = harness.Host.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
            Assert.Equal(4096, await db.Set<StatisticsEntry>().CountAsync(e => e.IsValid));
            Assert.Equal(TimeSpan.FromSeconds(11), (await db.Set<StatisticsItem>().SingleAsync()).MinDuration);
        }
        Assert.Equal(HttpStatusCode.OK, (await harness.Client.PutAsJsonAsync(ItemUrl(item.Id), request)).StatusCode);
        Assert.Equal(job.Generation, (await harness.Job(item.Id)).Generation);
        Assert.Equal(1, await harness.OutboxCount());
        Assert.Equal(HttpStatusCode.Conflict, (await harness.Client.PutAsJsonAsync(ItemUrl(item.Id), request with { Distance = 500 })).StatusCode);
        Assert.Equal(job.Generation, (await harness.Job(item.Id)).Generation);
        Console.WriteLine($"4096 retained rows; threshold save {watch.ElapsedMilliseconds}ms; zero entry queries; one durable event.");
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task RequestedBroaderLocalBoundsRoundtripClearingAndDisjointGlobalEdit()
    {
        await using var h = await Harness.Create();
        var item = await h.CreateItem(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20));
        var config = await h.CreateConfig(item.Id);
        var request = ConfigRequest(item.Id) with { MinDuration = TimeSpan.FromSeconds(5), MaxDuration = TimeSpan.FromSeconds(30) };
        var response = await h.Client.PutAsJsonAsync(ConfigUrl(config.Id), request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<StatisticsConfigurationDTO>())!;
        Assert.Equal(request.MinDuration, saved.MinDuration);
        Assert.Equal(request.MaxDuration, saved.MaxDuration);
        var narrowed = request with { MinDuration = TimeSpan.FromSeconds(15), MaxDuration = TimeSpan.FromSeconds(18) };
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsJsonAsync(ConfigUrl(config.Id), narrowed)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PutAsJsonAsync(ItemUrl(item.Id),
            new SaveStatisticsItemRequest("Lap", "", 250, TimeSpan.FromSeconds(19), TimeSpan.FromSeconds(20)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PutAsJsonAsync(ConfigUrl(config.Id),
            narrowed with { MinDuration = TimeSpan.FromSeconds(21), MaxDuration = null })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsJsonAsync(ConfigUrl(config.Id),
            request with { MinDuration = null, MaxDuration = null })).StatusCode);
        saved = Assert.Single((await h.Client.GetFromJsonAsync<List<StatisticsConfigurationDTO>>("/api/statistics/admin/configurations"))!);
        Assert.Null(saved.MinDuration);
        Assert.Null(saved.MaxDuration);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsJsonAsync(ItemUrl(item.Id), new SaveStatisticsItemRequest("Lap", "", 250))).StatusCode);
        Assert.Null(Assert.Single((await h.Client.GetFromJsonAsync<List<StatisticsItemDTO>>("/api/statistics/admin/items"))!).MinDuration);
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task ThresholdOnlySimpleAndMultiEditsWithInvalidResultsKeepStructuralAndDeleteGuards()
    {
        await using var h = await Harness.Create();
        var item = await h.CreateItem();
        var simple = await h.CreateConfig(item.Id);
        var multiRequest = new SaveStatisticsConfigurationRequest(item.Id, StatisticsConfigurationType.Multi, null, null, simple.Id, 2);
        var response = await h.Client.PostAsJsonAsync("/api/statistics/admin/configurations", multiRequest);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var multi = (await response.Content.ReadFromJsonAsync<StatisticsConfigurationDTO>())!;
        var entry = Entry(simple, 0);
        entry.IsValid = false;
        entry.InvalidReason = StatisticsEntryInvalidReason.AboveMaximumDuration;
        await h.Host.SeedAsync(entry, new StatisticsEntry
        {
            Id = Guid.NewGuid(), StatisticsItemId = item.Id, StatisticsItemConfigId = multi.Id,
            TimeStart = entry.TimeStart, TimeEnd = entry.TimeEnd, Duration = entry.Duration,
            Speed = 25, FacilityId = entry.FacilityId, TransponderId = entry.TransponderId
        });
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsJsonAsync(ConfigUrl(simple.Id),
            ConfigRequest(item.Id) with { MinDuration = TimeSpan.FromSeconds(1) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsJsonAsync(ConfigUrl(multi.Id),
            multiRequest with { MaxDuration = TimeSpan.FromSeconds(100) })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await h.Client.PutAsJsonAsync(ConfigUrl(simple.Id),
            ConfigRequest(item.Id) with { CoursePointEnd = PublicStatisticsTestHost.Start })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await h.Client.PutAsJsonAsync(ConfigUrl(multi.Id),
            multiRequest with { Repetitions = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await h.Client.DeleteAsync(ConfigUrl(simple.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await h.Client.DeleteAsync(ItemUrl(item.Id))).StatusCode);
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task PositiveWholeMicrosecondValidationAndWriteAuthorization()
    {
        await using var h = await Harness.Create();
        var item = await h.CreateItem();
        foreach (var ticks in new long[] { -10, 0, 1, 9, 11 })
            Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PutAsJsonAsync(ItemUrl(item.Id),
                new SaveStatisticsItemRequest("Lap", "", 250, TimeSpan.FromTicks(ticks)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsJsonAsync(ItemUrl(item.Id),
            new SaveStatisticsItemRequest("Lap", "", 250, TimeSpan.FromTicks(10), TimeSpan.FromTicks(10)))).StatusCode);
        foreach (var role in new[] { "user", "timing-admin", "facilities-admin" })
        {
            using var client = h.Host.App.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(ItemUrl(item.Id),
                new SaveStatisticsItemRequest("No", "", 250))).StatusCode);
        }
        using var anonymous = PublicStatisticsDiscoveryTests.Anonymous(h.Host);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(ItemUrl(item.Id),
            new SaveStatisticsItemRequest("No", "", 250))).StatusCode);
        Assert.Equal(1, await h.OutboxCount());
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task SerializationAndCommitFailuresRollbackDefinitionCheckpointAndOutbox()
    {
        await using var h = await Harness.Create();
        var item = await h.CreateItem();
        var original = await h.Job(item.Id);
        var serializer = h.Host.App.Services.GetRequiredService<JsonMessageSerializer>();
        var options = serializer.Options;
        serializer.Options = new JsonSerializerOptions(options);
        serializer.Options.Converters.Add(new RejectSerialization());
        Assert.Equal(HttpStatusCode.InternalServerError, (await h.Client.PutAsJsonAsync(ItemUrl(item.Id),
            new SaveStatisticsItemRequest("Changed", "", 250, TimeSpan.FromSeconds(1)))).StatusCode);
        serializer.Options = options;
        await AssertRolledBack();
        h.CommitFailure.Enabled = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await h.Client.PutAsJsonAsync(ItemUrl(item.Id),
            new SaveStatisticsItemRequest("Changed", "", 250, TimeSpan.FromSeconds(1)))).StatusCode);
        h.CommitFailure.Enabled = false;
        await AssertRolledBack();

        async Task AssertRolledBack()
        {
            var persisted = Assert.Single((await h.Client.GetFromJsonAsync<List<StatisticsItemDTO>>("/api/statistics/admin/items"))!);
            Assert.Equal(item, persisted);
            Assert.Equal(original.Generation, (await h.Job(item.Id)).Generation);
            Assert.Equal(original.LastEntryId, (await h.Job(item.Id)).LastEntryId);
            Assert.Equal(0, await h.OutboxCount());
        }
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task RealOutboxInitializationDdlFailurePropagatesInsteadOfStartingWithoutDurability()
    {
        var exception = await Assert.ThrowsAnyAsync<SlimMessageBus.MessageBusException>(() => Harness.Create(incompatibleSchema: true));
        Assert.Contains("Outbox schema migration failed", exception.ToString());
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task OutboxInsertFailureRollsBackDefinitionAndGeneration()
    {
        await using var h = await Harness.Create();
        var item = await h.CreateItem();
        var job = await h.Job(item.Id);
        await using (var scope = h.Host.App.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<StatisticsDbContext>().Database.ExecuteSqlRawAsync(
                "ALTER TABLE statistics.smb_outbox ADD CONSTRAINT injected_enqueue_failure CHECK (false) NOT VALID");
        Assert.Equal(HttpStatusCode.InternalServerError, (await h.Client.PutAsJsonAsync(ItemUrl(item.Id),
            new SaveStatisticsItemRequest("Changed", "", 250, TimeSpan.FromSeconds(1)))).StatusCode);
        Assert.Equal(item, Assert.Single((await h.Client.GetFromJsonAsync<List<StatisticsItemDTO>>("/api/statistics/admin/items"))!));
        Assert.Equal(job.Generation, (await h.Job(item.Id)).Generation);
        Assert.Equal(job.LastEntryId, (await h.Job(item.Id)).LastEntryId);
        Assert.Equal(0, await h.OutboxCount());
        Assert.Empty(h.Sender.Messages);
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task GuardedPublisherCannotSendOutsideTransactionAndUncommittedWorkIsInvisible()
    {
        await using var h = await Harness.Create();
        var item = await h.CreateItem();
        await using (var scope = h.Host.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
            var enqueue = scope.ServiceProvider.GetRequiredService<IStatisticsThresholdEnqueue>();
            var message = new StatisticsValidityThresholdsChangedV1(Guid.NewGuid(), item.Id, null);
            await Assert.ThrowsAsync<InvalidOperationException>(() => enqueue.EnqueueAsync(message));
            await Assert.ThrowsAsync<ArgumentException>(() => enqueue.EnqueueAsync(message with { EventId = Guid.Empty }));
            await using var transaction = await db.Database.BeginTransactionAsync();
            await enqueue.EnqueueAsync(message);
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM statistics.smb_outbox").SingleAsync());
            Assert.Equal(0, await h.OutboxCount());
            await transaction.RollbackAsync();
        }
        Assert.Equal(0, await h.OutboxCount());
        Assert.Empty(h.Sender.Messages);
    }

    [PublicStatisticsPostgreSqlFact]
    public async Task ControlledBrokerOutageRestartDeliversDurableEventWithExactRoutingIds()
    {
        await using var h = await Harness.Create();
        var item = await h.CreateItem();
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsJsonAsync(ItemUrl(item.Id),
            new SaveStatisticsItemRequest("Lap", "", 250, TimeSpan.FromSeconds(1)))).StatusCode);
        await WaitUntil(async () => await h.Scalar<int>("SELECT delivery_attempt FROM statistics.smb_outbox LIMIT 1") > 0);
        Assert.Equal(1, await h.OutboxCount());
        Assert.Empty(h.Sender.Messages);
        var bus = h.Host.App.Services.GetRequiredService<IMasterMessageBus>();
        await bus.Stop();
        h.Sender.Available = true;
        await bus.Start();
        await WaitUntil(async () => await h.Scalar<int>("SELECT count(*)::int FROM statistics.smb_outbox WHERE delivery_complete") == 1);
        var transport = Assert.Single(h.Sender.Messages);
        var message = JsonSerializer.Deserialize<StatisticsValidityThresholdsChangedV1>(transport.Body.ToString(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(item.Id, message.StatisticsItemId);
        Assert.Null(message.StatisticsItemConfigId);
        Assert.Equal(message.EventId.ToString(), transport.MessageId);
        Assert.Equal(item.Id.ToString(), transport.SessionId);
        Assert.Equal(nameof(StatisticsValidityThresholdsChangedV1), transport.ApplicationProperties["MessageName"]);
        Assert.Equal(1, await h.OutboxCount()); // Cleanup deliberately disabled.
    }

    private static string ItemUrl(Guid id) => $"/api/statistics/admin/items/{id}";
    private static string ConfigUrl(Guid id) => $"/api/statistics/admin/configurations/{id}";
    private static SaveStatisticsConfigurationRequest ConfigRequest(Guid item) => new(item,
        StatisticsConfigurationType.Simple, PublicStatisticsTestHost.Start, PublicStatisticsTestHost.End, null, null);
    private static StatisticsEntry Entry(StatisticsConfigurationDTO config, int i) => new()
    {
        Id = Guid.NewGuid(), StatisticsItemId = config.StatisticsItemId, StatisticsItemConfigId = config.Id,
        TransponderId = Guid.NewGuid(), FacilityId = PublicStatisticsTestHost.Facility,
        TimeStart = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc).AddSeconds(i),
        TimeEnd = new DateTime(2026, 11, 3, 0, 0, 10, DateTimeKind.Utc).AddSeconds(i),
        Duration = TimeSpan.FromSeconds(10), Speed = 25
    };

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!await condition()) await Task.Delay(50, timeout.Token);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public PublicStatisticsTestHost Host = null!;
        public HttpClient Client = null!;
        public readonly ControlledSender Sender = new();
        public readonly CommitFailure CommitFailure = new();

        public static async Task<Harness> Create(bool incompatibleSchema = false)
        {
            var h = new Harness();
            try
            {
                h.Host = await PublicStatisticsTestHost.CreateAsync(true, services =>
                {
                    services.ConfigureDbContext<StatisticsDbContext>((_, options) => options.AddInterceptors(h.CommitFailure));
                    var builder = (MessageBusBuilder)services.Single(s => s.ServiceType == typeof(MessageBusBuilder)).ImplementationInstance!;
                    builder.WithProviderServiceBus(options =>
                    {
                        options.ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==";
                        options.TopologyProvisioning.Enabled = false;
                        options.SenderFactory = (_, _) => h.Sender;
                    });
                });
                h.Client = h.Host.App.CreateClient();
                h.Client.DefaultRequestHeaders.Add("X-Test-Role", "statistics-admin");
                if (incompatibleSchema)
                {
                    await using var scope = h.Host.App.Services.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<StatisticsDbContext>().Database.ExecuteSqlRawAsync(
                        "CREATE TABLE statistics.smb_outbox (incompatible integer)");
                }
                h.Host.App.Services.GetRequiredService<PostgreSqlOutboxSettings>().PollIdleSleep = TimeSpan.FromMilliseconds(50);
                await h.Host.App.Services.GetRequiredService<IMasterMessageBus>().Start();
                return h;
            }
            catch { await h.DisposeAsync(); throw; }
        }

        public async Task<StatisticsItemDTO> CreateItem(TimeSpan? min = null, TimeSpan? max = null)
        {
            var response = await Client.PostAsJsonAsync("/api/statistics/admin/items", new SaveStatisticsItemRequest("Lap", "", 250, min, max));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<StatisticsItemDTO>())!;
        }
        public async Task<StatisticsConfigurationDTO> CreateConfig(Guid item)
        {
            var response = await Client.PostAsJsonAsync("/api/statistics/admin/configurations", ConfigRequest(item));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<StatisticsConfigurationDTO>())!;
        }
        public async Task<StatisticsValidityRevalidation> Job(Guid item)
        {
            await using var scope = Host.App.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<StatisticsDbContext>()
                .Set<StatisticsValidityRevalidation>().AsNoTracking().SingleAsync(j => j.StatisticsItemId == item);
        }
        public Task<int> OutboxCount() => Scalar<int>("SELECT count(*)::int FROM statistics.smb_outbox");
        public async Task<T> Scalar<T>(string sql)
        {
            await using var scope = Host.App.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<StatisticsDbContext>()
                .Database.SqlQueryRaw<T>($"SELECT ({sql}) AS \"Value\"").SingleAsync();
        }
        public async ValueTask DisposeAsync()
        {
            Client?.Dispose();
            if (Host is not null)
            {
                await Host.App.Services.GetRequiredService<IMasterMessageBus>().Stop();
                await Host.DisposeAsync();
            }
        }
    }

    private sealed class CommitFailure : DbTransactionInterceptor
    {
        public bool Enabled;
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Enabled) throw new InvalidOperationException("injected transaction commit failure");
            return ValueTask.FromResult(result);
        }
    }
    private sealed class RejectSerialization : JsonConverter<StatisticsValidityThresholdsChangedV1>
    {
        public override StatisticsValidityThresholdsChangedV1? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, StatisticsValidityThresholdsChangedV1 value, JsonSerializerOptions options) => throw new JsonException("injected serialization failure");
    }
    private sealed class ControlledSender : ServiceBusSender
    {
        private readonly ConcurrentDictionary<ServiceBusMessageBatch, List<ServiceBusMessage>> batches = new();
        public bool Available;
        public ConcurrentQueue<ServiceBusMessage> Messages { get; } = new();
        public override ValueTask<ServiceBusMessageBatch> CreateMessageBatchAsync(CancellationToken cancellationToken = default)
        {
            var messages = new List<ServiceBusMessage>();
            var batch = ServiceBusModelFactory.ServiceBusMessageBatch(0, messages, tryAddCallback: _ => true);
            batches[batch] = messages;
            return ValueTask.FromResult(batch);
        }
        public override Task SendMessagesAsync(ServiceBusMessageBatch batch, CancellationToken cancellationToken = default)
        {
            var messages = batches[batch];
            batches.TryRemove(batch, out _);
            if (!Available) throw new ServiceBusException("controlled outage", ServiceBusFailureReason.ServiceBusy);
            foreach (var message in messages) Messages.Enqueue(message);
            return Task.CompletedTask;
        }
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
