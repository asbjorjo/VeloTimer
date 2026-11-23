using System.Diagnostics.Metrics;
using System.Reflection;
using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SlimMessageBus;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Statistics;
using VeloTime.Module.Statistics.Handlers;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Interface.Messages;
using Assert = Xunit.Assert;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsValidityPostgreSqlTests
{
    [PostgreSqlStatisticsFact]
    public async Task SampleOutboxAtomicRollbackReplayAndConcurrentDelivery()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var metrics = new Metrics(services.GetRequiredService<IMeterFactory>());
        var facilities = DispatchProxy.Create<IFacilitiesClient, TimingSampleEventTimeTests.FacilitiesProxy>();
        var proxy = (TimingSampleEventTimeTests.FacilitiesProxy)(object)facilities;
        var distance = new CoursePointDistance { CoursePointStartId = Guid.NewGuid(), CoursePointEndId = Guid.NewGuid(),
            FacilityId = Guid.NewGuid(), Distance = 100 };
        var message = new TimingSampleComplete(Start.AddTicks(9), Start.AddSeconds(1).AddTicks(9),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        proxy.Resolve = async (_, _, _, _, _) =>
        {
            if (Interlocked.Increment(ref arrivals) == 2) barrier.SetResult();
            await barrier.Task.WaitAsync(TimeSpan.FromSeconds(15));
            return distance;
        };
        await using var a = Db();
        await using var b = Db();
        TimingSampleHandler Handler(StatisticsDbContext db) => new(db, facilities, metrics, NullLogger<TimingSampleHandler>.Instance);
        await Task.WhenAll(Handler(a).OnHandle(message, CancellationToken.None),
            Handler(b).OnHandle(message, CancellationToken.None));
        await using var verify = Db();
        Assert.Equal(1, await verify.Set<Sample>().CountAsync());
        Assert.Equal(1, await verify.Set<SampleCompleteOutbox>().CountAsync());
        proxy.Resolve = (_, _, _, _, _) => throw new InvalidOperationException("Facilities unavailable");
        await Handler(verify).OnHandle(message with { TimeStart = Start, TimeEnd = Start.AddSeconds(1) }, CancellationToken.None);
        Assert.Equal(2, proxy.Calls);
        Assert.Equal(1, await verify.Set<SampleCompleteOutbox>().CountAsync());

        await verify.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION statistics.reject_sample_outbox() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'injected outbox failure'; END $$;
            CREATE TRIGGER reject_sample_outbox BEFORE INSERT ON statistics.sample_complete_outbox
                FOR EACH ROW EXECUTE FUNCTION statistics.reject_sample_outbox();
            """);
        proxy.Resolve = (_, _, _, _, _) => Task.FromResult(distance);
        var retry = message with { TransponderId = Guid.NewGuid() };
        await using (var failed = Db())
            await Assert.ThrowsAsync<DbUpdateException>(() => Handler(failed).OnHandle(retry, CancellationToken.None));
        Assert.Equal(1, await verify.Set<Sample>().CountAsync());
        Assert.Equal(1, await verify.Set<SampleCompleteOutbox>().CountAsync());
        await verify.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_sample_outbox ON statistics.sample_complete_outbox");
        await using (var retried = Db())
            await Handler(retried).OnHandle(retry, CancellationToken.None);
        Assert.Equal(2, await verify.Set<Sample>().CountAsync());
        Assert.Equal(2, await verify.Set<SampleCompleteOutbox>().CountAsync());
    }

    [PostgreSqlStatisticsFact]
    public async Task SampleOutboxSendFailureAndRestartRetainAndRetryInOrder()
    {
        var rider = Guid.NewGuid();
        await using (var db = Db())
        {
            db.AddRange(Enumerable.Range(0, 2).Select(i => new SampleCompleteOutbox
            {
                SampleId = Guid.NewGuid(), TransponderId = rider, TimeStart = Start.AddSeconds(i),
                TimeEnd = Start.AddSeconds(i + 1), CoursePointStart = Guid.NewGuid(),
                CoursePointEnd = Guid.NewGuid(), Distance = 100
            }));
            await db.SaveChangesAsync();
        }

        var bus = DispatchProxy.Create<IMessageBus, OutboxBus>();
        var proxy = (OutboxBus)(object)bus;
        proxy.Fail = true;
        await using (var db = Db())
            await Assert.ThrowsAsync<InvalidOperationException>(() => new SampleCompleteOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None));
        await using (var db = Db())
            Assert.Equal(2, await db.Set<SampleCompleteOutbox>().CountAsync(x => x.DeliveredAtUtc == null));
        proxy.Fail = false;
        await using (var db = Db())
            Assert.True(await new SampleCompleteOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None));
        await using (var db = Db())
            Assert.True(await new SampleCompleteOutboxDispatcher(db, bus).DispatchOneAsync(CancellationToken.None));
        Assert.Equal(3, proxy.Messages.Count); // failed acknowledgement can have delivered
        Assert.Equal(proxy.Messages[0], proxy.Messages[1]);
        Assert.Equal(Start, proxy.Messages[1].TimeStart);
        Assert.Equal(Start.AddSeconds(1), proxy.Messages[2].TimeStart);
        await using (var db = Db())
            Assert.Equal(0, await db.Set<SampleCompleteOutbox>().CountAsync(x => x.DeliveredAtUtc == null));
    }

        [PostgreSqlStatisticsFact]
        public async Task SampleOutboxWorkerRestartResumesCommittedPendingWork()
        {
            await using (var db = Db())
            {
                db.Add(new SampleCompleteOutbox { SampleId = Guid.NewGuid(), TransponderId = Guid.NewGuid(),
                    TimeStart = Start, TimeEnd = Start.AddSeconds(1),
                    CoursePointStart = Guid.NewGuid(), CoursePointEnd = Guid.NewGuid(), Distance = 100 });
                await db.SaveChangesAsync();
            }
            var bus = DispatchProxy.Create<IMessageBus, OutboxBus>();
            var proxy = (OutboxBus)(object)bus;
            proxy.Fail = true;
            await using var provider = new ServiceCollection()
                .AddScoped(_ => Db())
                .AddSingleton(bus)
                .AddScoped<SampleCompleteOutboxDispatcher>()
                .BuildServiceProvider();
            var scopes = provider.GetRequiredService<IServiceScopeFactory>();
            var first = new SampleCompleteOutboxWorker(scopes, NullLogger<SampleCompleteOutboxWorker>.Instance);
            await first.StartAsync(CancellationToken.None);
            await WaitFor(async () =>
            {
                await using var db = Db();
                return proxy.Messages.Count > 0 && await db.Set<SampleCompleteOutbox>().CountAsync(x => x.DeliveredAtUtc == null) == 1;
            });
            await first.StopAsync(CancellationToken.None);
            first.Dispose();
            proxy.Fail = false;
            var second = new SampleCompleteOutboxWorker(scopes, NullLogger<SampleCompleteOutboxWorker>.Instance);
            await second.StartAsync(CancellationToken.None);
            await WaitFor(async () =>
            {
                await using var db = Db();
                return await db.Set<SampleCompleteOutbox>().CountAsync(x => x.DeliveredAtUtc == null) == 0;
            });
            await second.StopAsync(CancellationToken.None);
            second.Dispose();
            Assert.Equal(2, proxy.Messages.Count);
            Assert.Equal(proxy.Messages[0], proxy.Messages[1]);
        }

        private static async Task WaitFor(Func<Task<bool>> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!await condition()) await Task.Delay(50, timeout.Token);
        }
        [PostgreSqlStatisticsFact]
        public async Task SampleOutboxUsesProcessorProducerRoutingAndSession()
        {
            var sender = new SampleSender();
            var builder = Host.CreateApplicationBuilder();
            builder.Configuration["ConnectionStrings:MessageBus"] = "Endpoint=sb://localhost/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==";
            builder.Configuration["ConnectionStrings:keycloak"] = "https://identity.test.invalid/realms/velotime";
            builder.Configuration["ConnectionStrings:velotimedb"] = connection;
            builder.Configuration["ConnectionStrings:cache"] = "localhost:6379";
            var startup = typeof(TimingSampleHandler).Assembly.GetType("StartupExtensions")!;
            startup.GetMethod("AddServices", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [builder]);
            var mbb = (MessageBusBuilder)builder.Services.Single(x => x.ServiceType == typeof(MessageBusBuilder)).ImplementationInstance!;
            mbb.AutoStartConsumersEnabled(false);
            string? path = null;
            mbb.WithProviderServiceBus(options =>
            {
                options.ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==";
                options.TopologyProvisioning.Enabled = false;
                options.SenderFactory = (target, _) => { path = target; return sender; };
            });
            await using var provider = builder.Services.BuildServiceProvider();
            var master = provider.GetRequiredService<IMasterMessageBus>();
            await master.Start();
            try
            {
                var rider = Guid.NewGuid();
                await using var db = Db();
                db.Add(new SampleCompleteOutbox { SampleId = Guid.NewGuid(), TransponderId = rider,
                    TimeStart = Start, TimeEnd = Start.AddSeconds(1),
                    CoursePointStart = Guid.NewGuid(), CoursePointEnd = Guid.NewGuid(), Distance = 100 });
                await db.SaveChangesAsync();
                await using var scope = provider.CreateAsyncScope();
                Assert.True(await new SampleCompleteOutboxDispatcher(db, scope.ServiceProvider.GetRequiredService<IMessageBus>())
                    .DispatchOneAsync(CancellationToken.None));
                var transport = Assert.Single(sender.Messages);
                Assert.Equal("velotime-statistics", path);
                Assert.Equal(rider.ToString(), transport.SessionId);
                Assert.Equal(nameof(SampleComplete), transport.ApplicationProperties["MessageName"]);
                Assert.Equal(rider, JsonSerializer.Deserialize<SampleComplete>(transport.Body.ToString(),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!.TransponderId);
            }
            finally { await master.Stop(); }
        }

        private sealed class SampleSender : ServiceBusSender
        {
            private readonly ConcurrentDictionary<ServiceBusMessageBatch, List<ServiceBusMessage>> batches = new();
            public List<ServiceBusMessage> Messages { get; } = [];
            public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default)
            {
                Messages.Add(message);
                return Task.CompletedTask;
            }
            public override ValueTask<ServiceBusMessageBatch> CreateMessageBatchAsync(CancellationToken cancellationToken = default)
            {
                var messages = new List<ServiceBusMessage>();
                var batch = ServiceBusModelFactory.ServiceBusMessageBatch(0, messages, tryAddCallback: _ => true);
                batches[batch] = messages;
                return ValueTask.FromResult(batch);
            }
            public override Task SendMessagesAsync(ServiceBusMessageBatch batch, CancellationToken cancellationToken = default)
            {
                Messages.AddRange(batches[batch]);
                batches.TryRemove(batch, out _);
                return Task.CompletedTask;
            }
            public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
            public override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        }
    [PostgreSqlStatisticsFact]
    public async Task SampleOutboxTwoDispatchersRespectPerRiderLocks()
    {
        var rider = Guid.NewGuid();
        var other = Guid.NewGuid();
        await using (var db = Db())
        {
            foreach (var id in new[] { rider, rider, other })
                db.Add(new SampleCompleteOutbox { SampleId = Guid.NewGuid(), TransponderId = id,
                    TimeStart = Start, TimeEnd = Start.AddSeconds(1),
                    CoursePointStart = Guid.NewGuid(), CoursePointEnd = Guid.NewGuid(), Distance = 100 });
            await db.SaveChangesAsync();
        }
        var bus = DispatchProxy.Create<IMessageBus, BlockingOutboxBus>();
        var proxy = (BlockingOutboxBus)(object)bus;
        await using var a = Db();
        await using var b = Db();
        var first = new SampleCompleteOutboxDispatcher(a, bus).DispatchOneAsync(CancellationToken.None);
        await proxy.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(await new SampleCompleteOutboxDispatcher(b, bus).DispatchOneAsync(CancellationToken.None));
        Assert.Equal(new[] { rider, other }, proxy.Messages.ToArray());
        proxy.Release.SetResult();
        Assert.True(await first);
        await using var c = Db();
        Assert.True(await new SampleCompleteOutboxDispatcher(c, bus).DispatchOneAsync(CancellationToken.None));
        Assert.Equal(new[] { rider, other, rider }, proxy.Messages.ToArray());
    }

    [PostgreSqlStatisticsFact]
    public async Task SampleOutboxFailureAfterAcknowledgementIsRetried()
    {
        await using var db = Db();
        db.Add(new SampleCompleteOutbox { SampleId = Guid.NewGuid(), TransponderId = Guid.NewGuid(),
            TimeStart = Start, TimeEnd = Start.AddSeconds(1),
            CoursePointStart = Guid.NewGuid(), CoursePointEnd = Guid.NewGuid(), Distance = 100 });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION statistics.reject_sample_delivery() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'injected mark failure'; END $$;
            CREATE TRIGGER reject_sample_delivery BEFORE UPDATE ON statistics.sample_complete_outbox
                FOR EACH ROW EXECUTE FUNCTION statistics.reject_sample_delivery();
            """);
        var bus = DispatchProxy.Create<IMessageBus, OutboxBus>();
        var proxy = (OutboxBus)(object)bus;
        await using (var failed = Db())
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                new SampleCompleteOutboxDispatcher(failed, bus).DispatchOneAsync(CancellationToken.None));
        Assert.Single(proxy.Messages);
        Assert.Equal(1, await db.Set<SampleCompleteOutbox>().CountAsync(x => x.DeliveredAtUtc == null));
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_sample_delivery ON statistics.sample_complete_outbox");
        await using (var retried = Db())
            Assert.True(await new SampleCompleteOutboxDispatcher(retried, bus).DispatchOneAsync(CancellationToken.None));
        Assert.Equal(2, proxy.Messages.Count);
        Assert.Equal(proxy.Messages[0], proxy.Messages[1]);
        Assert.Equal(0, await db.Set<SampleCompleteOutbox>().CountAsync(x => x.DeliveredAtUtc == null));
    }

    public class BlockingOutboxBus : DispatchProxy
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<Guid> Messages { get; } = new();
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(IMessageBus.Publish)) throw new NotSupportedException(method?.Name);
            Messages.Enqueue(((SampleComplete)args![0]!).TransponderId);
            if (Messages.Count != 1) return Task.CompletedTask;
            Started.SetResult();
            return Release.Task;
        }
    }

    public class OutboxBus : DispatchProxy
    {
        public bool Fail { get; set; }
        public List<SampleComplete> Messages { get; } = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(IMessageBus.Publish)) throw new NotSupportedException(method?.Name);
            Messages.Add((SampleComplete)args![0]!);
            return Fail ? Task.FromException(new InvalidOperationException("lost acknowledgement")) : Task.CompletedTask;
        }
    }
}
