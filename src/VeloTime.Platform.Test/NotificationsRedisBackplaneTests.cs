using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using VeloTime.Module.Notifications.Interface.Messages;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Notifications;
using Xunit.Abstractions;

namespace VeloTime.Platform.Test;

public sealed class NotificationsRedisFactAttribute : FactAttribute
{
    public NotificationsRedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOTIME_TEST_REDIS")))
            Skip = "Requires a disposable Redis server via VELOTIME_TEST_REDIS.";
    }
}

[CollectionDefinition("Notifications Redis backplane", DisableParallelization = true)]
public sealed class NotificationsRedisCollection
{
    public const string Name = "Notifications Redis backplane";
}

[Collection(NotificationsRedisCollection.Name)]
public sealed class NotificationsRedisBackplaneTests(ITestOutputHelper output)
{
    [NotificationsRedisFact]
    public async Task BrokerHintReachesWasmAndServerSubscribersOnBothReplicasWithoutDuplicateFanout()
    {
        var redis = Environment.GetEnvironmentVariable("VELOTIME_TEST_REDIS")!;
        var prefix = "velotime:test:notifications:" + Guid.NewGuid().ToString("N");
        using var first = CreateHost(redis, prefix);
        using var second = CreateHost(redis, prefix);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var firstEvents = new ConcurrentQueue<NotificationRefreshKind>();
        var secondEvents = new ConcurrentQueue<NotificationRefreshKind>();
        var wasmEvents = new ConcurrentQueue<NotificationRefreshKind>();
        var firstSource = new ServerNotificationRefreshSource(first.Services.GetRequiredService<NotificationRefreshFanout>());
        var secondSource = new ServerNotificationRefreshSource(second.Services.GetRequiredService<NotificationRefreshFanout>());
        await using var firstSubscription = await firstSource.SubscribeAsync(Record(firstEvents), timeout.Token);
        await using var secondSubscription = await secondSource.SubscribeAsync(Record(secondEvents), timeout.Token);
        await firstSubscription.SetActiveAsync(true, timeout.Token);
        await secondSubscription.SetActiveAsync(true, timeout.Token);
        await WaitUntil(() => firstEvents.Contains(NotificationRefreshKind.Connected) &&
            secondEvents.Contains(NotificationRefreshKind.Connected), timeout.Token);

        var connection = new HubConnectionBuilder().WithUrl("http://localhost/hubs/notifications", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => second.CreateHandler();
        }).WithAutomaticReconnect().Build();
        var rawHints = 0;
        using var rawHandler = connection.On<NotificationsRefreshRequestedV1>("NotificationsRefresh", _ => Interlocked.Increment(ref rawHints));
        var wasmSource = new WasmNotificationRefreshSource(() => connection);
        await using var wasmSubscription = await wasmSource.SubscribeAsync(Record(wasmEvents), timeout.Token);
        await wasmSubscription.SetActiveAsync(true, timeout.Token);
        await WaitUntil(() => wasmEvents.Contains(NotificationRefreshKind.Connected), timeout.Token);

        var hint = new NotificationsRefreshRequestedV1(Guid.NewGuid());
        var started = Stopwatch.GetTimestamp();
        var bridge = new NotificationsRefreshBridge(first.Services.GetRequiredService<IHubContext<NotificationsHub>>(),
            first.Services.GetRequiredService<NotificationRefreshFanout>());
        await bridge.OnHandle(hint, timeout.Token);
        await WaitUntil(() => RefreshCount(firstEvents) == 1 && RefreshCount(secondEvents) == 1 &&
            RefreshCount(wasmEvents) == 1, timeout.Token);
        var elapsed = Stopwatch.GetElapsedTime(started);
        output.WriteLine($"Local bridge to both server replicas and Microsoft client: {elapsed.TotalMilliseconds:F0} ms.");
        Assert.True(elapsed <= TimeSpan.FromSeconds(2));

        // Broker redelivery is globally broadcast once per invocation; subscribers
        // deduplicate EventId. Server Redis callbacks must never re-broadcast to hubs.
        await bridge.OnHandle(hint, timeout.Token);
        await WaitUntil(() => Volatile.Read(ref rawHints) == 2, timeout.Token);
        await Task.Delay(150, timeout.Token);
        Assert.Equal(2, rawHints);
        Assert.Equal(1, RefreshCount(firstEvents));
        Assert.Equal(1, RefreshCount(secondEvents));
        Assert.Equal(1, RefreshCount(wasmEvents));

        await secondSubscription.SetActiveAsync(false, timeout.Token);
        await firstSubscription.DisposeAsync();
        await bridge.OnHandle(new NotificationsRefreshRequestedV1(Guid.NewGuid()), timeout.Token);
        await WaitUntil(() => RefreshCount(wasmEvents) == 2, timeout.Token);
        Assert.Equal(1, RefreshCount(firstEvents));
        Assert.Equal(1, RefreshCount(secondEvents));
        var connectedBeforeReactivation = secondEvents.Count(kind => kind == NotificationRefreshKind.Connected);
        await secondSubscription.SetActiveAsync(true, timeout.Token);
        Assert.Equal(connectedBeforeReactivation + 1, secondEvents.Count(kind => kind == NotificationRefreshKind.Connected));
    }

    [NotificationsRedisFact]
    public async Task ServerRedisReconnectEmitsRepairRefreshAndStillDeliversHints()
    {
        var redis = Environment.GetEnvironmentVariable("VELOTIME_TEST_REDIS")!;
        var prefix = "velotime:test:notifications:" + Guid.NewGuid().ToString("N");
        using var host = CreateHost(redis, prefix);
        var fanout = host.Services.GetRequiredService<NotificationRefreshFanout>();
        var events = new ConcurrentQueue<NotificationRefreshKind>();
        await using var subscription = fanout.Subscribe(Record(events), CancellationToken.None);
        await subscription.SetActiveAsync(true, default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await WaitUntil(() => events.Contains(NotificationRefreshKind.Connected), timeout.Token);
        var connection = host.Services.GetRequiredService<IConnectionMultiplexer>();
        await connection.GetDatabase().ExecuteAsync("CLIENT", "KILL", "TYPE", "pubsub");
        await WaitUntil(() => events.Contains(NotificationRefreshKind.TransportFailed) &&
            events.Count(kind => kind == NotificationRefreshKind.Connected) >= 2, timeout.Token);
        await fanout.PublishAsync(Guid.NewGuid(), timeout.Token);
        await WaitUntil(() => RefreshCount(events) == 1, timeout.Token);
        Assert.True(connection.IsConnected);
    }

    [NotificationsRedisFact]
    public async Task ServerFanoutIgnoresInvalidPayloadAndIsolatesFailedDisposedAndCancelledSubscribers()
    {
        var redis = Environment.GetEnvironmentVariable("VELOTIME_TEST_REDIS")!;
        var prefix = "velotime:test:notifications:" + Guid.NewGuid().ToString("N");
        using var host = CreateHost(redis, prefix);
        var fanout = host.Services.GetRequiredService<NotificationRefreshFanout>();
        var events = new ConcurrentQueue<NotificationRefreshKind>();
        using var token = new CancellationTokenSource();
        await using var good = fanout.Subscribe(Record(events), CancellationToken.None);
        await using var broken = fanout.Subscribe(_ => throw new InvalidOperationException("fixture"), CancellationToken.None);
        var cancelledCalls = 0;
        await using var cancelled = fanout.Subscribe(_ => { cancelledCalls++; return Task.CompletedTask; }, token.Token);
        await good.SetActiveAsync(true, default);
        using var readyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WaitUntil(() => events.Contains(NotificationRefreshKind.Connected), readyTimeout.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => broken.SetActiveAsync(true, default));
        await cancelled.SetActiveAsync(true, default);
        token.Cancel();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowCalls = 0;
        await using var slow = fanout.Subscribe(async kind =>
        {
            if (kind != NotificationRefreshKind.Refresh) return;
            Interlocked.Increment(ref slowCalls);
            await release.Task;
        }, CancellationToken.None);
        await slow.SetActiveAsync(true, default);
        try
        {
            await host.Services.GetRequiredService<IConnectionMultiplexer>().GetSubscriber()
                .PublishAsync(RedisChannel.Literal(prefix + ":server"), "not-an-event");
            await fanout.PublishAsync(Guid.NewGuid(), default);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await WaitUntil(() => RefreshCount(events) == 1 && Volatile.Read(ref slowCalls) == 1, timeout.Token);
            await fanout.PublishAsync(Guid.NewGuid(), default);
            await WaitUntil(() => RefreshCount(events) == 2, timeout.Token);
            Assert.Equal(1, slowCalls); // Still blocked; did not delay the healthy subscriber's next hint.
            Assert.Equal(1, cancelledCalls);
            await good.DisposeAsync();
            await fanout.PublishAsync(Guid.NewGuid(), default);
            await Task.Delay(150, timeout.Token);
            Assert.Equal(2, RefreshCount(events));
            Assert.True(host.Services.GetRequiredService<IConnectionMultiplexer>().IsConnected);
        }
        finally { release.TrySetResult(); }
    }

    [NotificationsRedisFact]
    public async Task InitialSubscribeFailureDoesNotBlockHostAndRetryRepairsActiveSubscriber()
    {
        var channel = RedisChannel.Literal("velotime:test:notifications:" + Guid.NewGuid().ToString("N"));
        var configuration = ConfigurationOptions.Parse(Environment.GetEnvironmentVariable("VELOTIME_TEST_REDIS")!);
        configuration.AllowAdmin = true;
        configuration.ReconnectRetryPolicy = new ExponentialRetry(100);
        using var connection = await ConnectionMultiplexer.ConnectAsync(configuration);
        var realSubscriber = connection.GetSubscriber();
        var dummy = await realSubscriber.SubscribeAsync(RedisChannel.Literal(channel + ":dummy"));
        var attempts = 0;
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retry = new TaskCompletionSource<ChannelMessageQueue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriber = ForwardingProxy.Create<ISubscriber>(realSubscriber, (method, _) =>
        {
            if (method.Name != "SubscribeAsync" || method.ReturnType != typeof(Task<ChannelMessageQueue>)) return null;
            if (Interlocked.Increment(ref attempts) == 1)
                return Task.FromException<ChannelMessageQueue>(new RedisConnectionException(
                    ConnectionFailureType.UnableToConnect, "fixture initial subscribe failure"));
            retryStarted.TrySetResult();
            return retry.Task;
        });
        var multiplexer = ForwardingProxy.Create<IConnectionMultiplexer>(connection, (method, _) =>
            method.Name == "GetSubscriber" ? subscriber : null);
        var logger = new RecordingFanoutLogger();
        using var host = CreateFanoutHost(multiplexer, channel.ToString(), logger);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var events = new ConcurrentQueue<NotificationRefreshKind>();
        var fanout = host.Services.GetRequiredService<NotificationRefreshFanout>();
        await using var subscription = fanout.Subscribe(Record(events), timeout.Token);
        await subscription.SetActiveAsync(true, timeout.Token);
        Assert.Contains(NotificationRefreshKind.TransportFailed, events);
        await retryStarted.Task.WaitAsync(timeout.Token);
        Assert.Contains(logger.Warnings, message => message.Contains("subscription failed"));
        Assert.DoesNotContain(NotificationRefreshKind.Connected, events);
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConnectionRestored += (_, e) =>
        {
            if (e.ConnectionType == ConnectionType.Subscription) restored.TrySetResult();
        };
        await connection.GetDatabase().ExecuteAsync("CLIENT", "KILL", "TYPE", "pubsub");
        await restored.Task.WaitAsync(timeout.Token);
        Assert.DoesNotContain(NotificationRefreshKind.Connected, events);

        retry.SetResult(await realSubscriber.SubscribeAsync(channel));
        await WaitUntil(() => events.Contains(NotificationRefreshKind.Connected), timeout.Token);
        await fanout.PublishAsync(Guid.NewGuid(), timeout.Token);
        await WaitUntil(() => RefreshCount(events) == 1, timeout.Token);
        Assert.Equal(2, attempts);
        Assert.Single(events.Where(kind => kind == NotificationRefreshKind.Connected));
        await dummy.UnsubscribeAsync();
    }

    [NotificationsRedisFact]
    public async Task StopCancelsInitialRetryDelay()
    {
        using var connection = await ConnectionMultiplexer.ConnectAsync(Environment.GetEnvironmentVariable("VELOTIME_TEST_REDIS")!);
        var attempts = 0;
        var subscriber = ForwardingProxy.Create<ISubscriber>(connection.GetSubscriber(), (method, _) =>
        {
            if (method.Name != "SubscribeAsync" || method.ReturnType != typeof(Task<ChannelMessageQueue>)) return null;
            Interlocked.Increment(ref attempts);
            return Task.FromException<ChannelMessageQueue>(new TimeoutException("fixture"));
        });
        var multiplexer = ForwardingProxy.Create<IConnectionMultiplexer>(connection, (method, _) =>
            method.Name == "GetSubscriber" ? subscriber : null);
        using var host = CreateFanoutHost(multiplexer, "velotime:test:notifications:" + Guid.NewGuid().ToString("N"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await WaitUntil(() => Volatile.Read(ref attempts) == 1, timeout.Token);
        await host.Services.GetRequiredService<NotificationRefreshFanout>().StopAsync(timeout.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(1200), timeout.Token);
        Assert.Equal(1, attempts);
        Assert.True(connection.IsConnected);
    }

    [NotificationsRedisFact]
    public async Task StopCancelsPendingSubscribeAndUnsubscribesLateQueueWithoutDisposingConnection()
    {
        var channel = RedisChannel.Literal("velotime:test:notifications:" + Guid.NewGuid().ToString("N"));
        using var connection = await ConnectionMultiplexer.ConnectAsync(Environment.GetEnvironmentVariable("VELOTIME_TEST_REDIS")!);
        var realSubscriber = connection.GetSubscriber();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<ChannelMessageQueue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var removedHandlers = 0;
        var subscriber = ForwardingProxy.Create<ISubscriber>(realSubscriber, (method, _) =>
        {
            if (method.Name != "SubscribeAsync" || method.ReturnType != typeof(Task<ChannelMessageQueue>)) return null;
            started.TrySetResult();
            return pending.Task;
        });
        var multiplexer = ForwardingProxy.Create<IConnectionMultiplexer>(connection, (method, _) =>
        {
            if (method.Name is "remove_ConnectionFailed" or "remove_ConnectionRestored")
                Interlocked.Increment(ref removedHandlers);
            return method.Name == "GetSubscriber" ? subscriber : null;
        });
        using var host = CreateFanoutHost(multiplexer, channel.ToString());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await started.Task.WaitAsync(timeout.Token);
        var fanout = host.Services.GetRequiredService<NotificationRefreshFanout>();
        var events = new ConcurrentQueue<NotificationRefreshKind>();
        await using var subscription = fanout.Subscribe(Record(events), timeout.Token);
        await subscription.SetActiveAsync(true, timeout.Token);
        await fanout.StopAsync(timeout.Token);
        Assert.True(connection.IsConnected);
        Assert.Equal(2, removedHandlers);
        host.Dispose();
        pending.SetResult(await realSubscriber.SubscribeAsync(channel));
        while (await realSubscriber.PublishAsync(channel, "cleanup-check") != 0)
            await Task.Delay(10, timeout.Token);
        Assert.DoesNotContain(NotificationRefreshKind.Connected, events);
        Assert.DoesNotContain(NotificationRefreshKind.Refresh, events);
    }

    private static TestServer CreateFanoutHost(IConnectionMultiplexer redis, string channel,
        ILogger<NotificationRefreshFanout>? logger = null) => new(new WebHostBuilder()
        .ConfigureServices(services =>
        {
            services.AddSingleton(redis);
            if (logger is not null) services.AddSingleton(logger);
            services.AddSingleton(provider => new NotificationRefreshFanout(redis,
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<NotificationRefreshFanout>>(), channel));
            services.AddHostedService(provider => provider.GetRequiredService<NotificationRefreshFanout>());
        })
        .Configure(_ => { }));

    private sealed class RecordingFanoutLogger : ILogger<NotificationRefreshFanout>
    {
        public ConcurrentQueue<string> Warnings { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Enqueue(formatter(state, exception));
        }
    }

    public class ForwardingProxy : DispatchProxy
    {
        private object _target = null!;
        private Func<MethodInfo, object?[], object?> _intercept = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var method = targetMethod!;
            var arguments = args ?? [];
            var result = _intercept(method, arguments);
            if (result is not null) return result;
            try { return method.Invoke(_target, arguments); }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        public static T Create<T>(T target, Func<MethodInfo, object?[], object?> intercept) where T : class
        {
            var proxy = DispatchProxy.Create<T, ForwardingProxy>();
            var forwarding = (ForwardingProxy)(object)proxy;
            forwarding._target = target;
            forwarding._intercept = intercept;
            return proxy;
        }
    }

    private static Func<NotificationRefreshKind, Task> Record(ConcurrentQueue<NotificationRefreshKind> events) =>
        kind => { events.Enqueue(kind); return Task.CompletedTask; };
    private static int RefreshCount(ConcurrentQueue<NotificationRefreshKind> events) =>
        events.Count(kind => kind == NotificationRefreshKind.Refresh);
    private static async Task WaitUntil(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition()) await Task.Delay(10, cancellationToken);
    }

    private static TestServer CreateHost(string redis, string prefix) => new(new WebHostBuilder()
        .ConfigureServices(services =>
        {
            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var configuration = ConfigurationOptions.Parse(redis);
                configuration.AllowAdmin = true;
                configuration.ReconnectRetryPolicy = new ExponentialRetry(100);
                return ConnectionMultiplexer.Connect(configuration);
            });
            services.AddSingleton(provider => new NotificationRefreshFanout(
                provider.GetRequiredService<IConnectionMultiplexer>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<NotificationRefreshFanout>>(), prefix + ":server"));
            services.AddHostedService(provider => provider.GetRequiredService<NotificationRefreshFanout>());
            services.AddSignalR().AddStackExchangeRedis(redis,
                options => options.Configuration.ChannelPrefix = RedisChannel.Literal(prefix));
        })
        .Configure(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints => endpoints.MapHub<NotificationsHub>("/hubs/notifications"));
        }));
}
