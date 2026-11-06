using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Module.Notifications.Interface.Messages;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.Platform.Test;

public sealed class NotificationsRefreshLifecycleTests
{
    [Fact]
    public async Task CallbacksDeduplicateSerializeSuspendAndDrainOnDispose()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var callbacks = new NotificationRefreshCallbacks(async _ =>
        {
            calls++; entered.TrySetResult(); await release.Task;
        });
        var id = Guid.NewGuid();
        await callbacks.NotifyAsync(NotificationRefreshKind.Refresh, id); // Inactive does not consume EventId.
        Assert.Equal(0, calls);
        callbacks.Active = true;
        var first = callbacks.NotifyAsync(NotificationRefreshKind.Refresh, id);
        await entered.Task;
        var duplicate = callbacks.NotifyAsync(NotificationRefreshKind.Refresh, id);
        var other = callbacks.NotifyAsync(NotificationRefreshKind.Refresh, Guid.NewGuid());
        var disposal = callbacks.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        release.SetResult();
        await Task.WhenAll(first, duplicate, other, disposal);
        callbacks.Active = true; // Disposed handles cannot be revived.
        await callbacks.NotifyAsync(NotificationRefreshKind.Connected);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CallbackRedeliveryIsBoundedAndRepeatedIdIsIgnored()
    {
        var calls = 0;
        await using var callbacks = new NotificationRefreshCallbacks(_ => { calls++; return Task.CompletedTask; }) { Active = true };
        var id = Guid.NewGuid();
        await callbacks.NotifyAsync(NotificationRefreshKind.Refresh, id);
        await callbacks.NotifyAsync(NotificationRefreshKind.Refresh, id);
        Assert.Equal(1, calls);
        for (var i = 0; i < 256; i++) await callbacks.NotifyAsync(NotificationRefreshKind.Refresh, Guid.NewGuid());
        await callbacks.NotifyAsync(NotificationRefreshKind.Refresh, id);
        Assert.Equal(258, calls);
    }

    [Fact]
    public async Task CallbackPumpCoalescesLargeRefreshBurstAndPreservesDistinctConnectionStates()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new ConcurrentQueue<NotificationRefreshKind>();
        await using var callbacks = new NotificationRefreshCallbacks(async kind =>
        {
            received.Enqueue(kind);
            if (received.Count == 1) { entered.TrySetResult(); await release.Task; }
        }) { Active = true };

        var first = callbacks.NotifyAsync(NotificationRefreshKind.Refresh, Guid.NewGuid());
        await entered.Task;
        var burst = new Task[1000];
        for (var i = 0; i < burst.Length; i++)
            burst[i] = callbacks.NotifyAsync(NotificationRefreshKind.Refresh, Guid.NewGuid());
        Assert.All(burst, task => Assert.Same(first, task));
        Assert.Equal(1, burst.Distinct().Count());

        var failed = callbacks.NotifyAsync(NotificationRefreshKind.TransportFailed);
        var connected = callbacks.NotifyAsync(NotificationRefreshKind.Connected);
        var failedAgain = callbacks.NotifyAsync(NotificationRefreshKind.TransportFailed);
        Assert.Same(first, connected);
        Assert.Same(first, failed);
        Assert.Same(first, failedAgain);
        release.SetResult();
        await Task.WhenAll(burst.Append(first));

        Assert.Equal([NotificationRefreshKind.Refresh, NotificationRefreshKind.Refresh],
            received.Where(kind => kind == NotificationRefreshKind.Refresh));
        Assert.Equal(
            [NotificationRefreshKind.Connected, NotificationRefreshKind.TransportFailed],
            received.Where(kind => kind != NotificationRefreshKind.Refresh));
    }

    [Fact]
    public async Task RefreshHintDuringFollowupQueuesOneMoreRefreshWithLatestCapturedState()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var followupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFollowup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capturedStates = new ConcurrentQueue<int>();
        var calls = 0;
        var state = 0;
        await using var callbacks = new NotificationRefreshCallbacks(async kind =>
        {
            if (kind != NotificationRefreshKind.Refresh) return;
            capturedStates.Enqueue(Volatile.Read(ref state));
            switch (Interlocked.Increment(ref calls))
            {
                case 1:
                    firstEntered.TrySetResult();
                    await releaseFirst.Task;
                    break;
                case 2:
                    followupEntered.TrySetResult();
                    await releaseFollowup.Task;
                    break;
            }
        }) { Active = true };

        var pump = callbacks.NotifyAsync(NotificationRefreshKind.Refresh);
        await firstEntered.Task;
        Volatile.Write(ref state, 1);
        Assert.Same(pump, callbacks.NotifyAsync(NotificationRefreshKind.Refresh));
        releaseFirst.SetResult();

        await followupEntered.Task;
        Assert.Equal(1, capturedStates.Last()); // The follow-up took its snapshot before the new hint.
        Volatile.Write(ref state, 2);
        var duringFollowup = callbacks.NotifyAsync(NotificationRefreshKind.Refresh);
        Assert.Same(pump, duringFollowup);
        for (var i = 0; i < 10_000; i++)
            Assert.Same(pump, callbacks.NotifyAsync(NotificationRefreshKind.Refresh));

        releaseFollowup.SetResult();
        await pump;
        Assert.Equal(3, calls);
        Assert.Equal([0, 1, 2], capturedStates);
    }

    [Fact]
    public async Task CallbackPumpSuspensionDropsOldPendingWorkAndFailureStillDrainsPendingWork()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new ConcurrentQueue<NotificationRefreshKind>();
        var callbacks = new NotificationRefreshCallbacks(async kind =>
        {
            received.Enqueue(kind);
            if (received.Count == 1)
            {
                entered.TrySetResult();
                await release.Task;
                throw new InvalidOperationException("fixture callback failure");
            }
        }) { Active = true };

        var first = callbacks.NotifyAsync(NotificationRefreshKind.Refresh);
        await entered.Task;
        _ = callbacks.NotifyAsync(NotificationRefreshKind.Connected);
        _ = callbacks.NotifyAsync(NotificationRefreshKind.Refresh);
        callbacks.Active = false;
        callbacks.Active = true;
        _ = callbacks.NotifyAsync(NotificationRefreshKind.TransportFailed);
        _ = callbacks.NotifyAsync(NotificationRefreshKind.Refresh);
        release.SetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        Assert.Equal([NotificationRefreshKind.Refresh, NotificationRefreshKind.Refresh],
            received.Where(kind => kind == NotificationRefreshKind.Refresh));
        Assert.DoesNotContain(NotificationRefreshKind.Connected, received);
        Assert.Contains(NotificationRefreshKind.TransportFailed, received);
        await callbacks.DisposeAsync();
    }

    [Fact]
    public async Task MicrosoftClientRetriesStartupReceivesTypedHintsAndRestartsWithoutLeaking()
    {
        using var server = CreateHost();
        var events = new ConcurrentQueue<NotificationRefreshKind>();
        var requests = new ConcurrentQueue<string>();
        var failPoll = 0;
        HubConnection? connection = null;
        var source = new WasmNotificationRefreshSource(() => connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/notifications", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => new FailNegotiationHandler(server.CreateHandler(), requests,
                    failures: 1, () => Interlocked.Exchange(ref failPoll, 0) == 1);
            }).WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromMilliseconds(50)]).Build());
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var subscription = await source.SubscribeAsync(kind => { events.Enqueue(kind); return Task.CompletedTask; }, lifetime.Token);
        Assert.Empty(requests); // Creation and prerender do not start network work.
        await subscription.SetActiveAsync(true, lifetime.Token);
        await WaitUntil(() => events.Contains(NotificationRefreshKind.Connected));
        Assert.Contains(NotificationRefreshKind.TransportFailed, events);
        Assert.Equal(2, requests.Count(path => path.Contains("/negotiate", StringComparison.Ordinal)));
        var hub = server.Services.GetRequiredService<IHubContext<LifecycleHub>>();
        var hint = new NotificationsRefreshRequestedV1(Guid.NewGuid());
        await hub.Clients.All.SendAsync("NotificationsRefresh", hint, lifetime.Token);
        await WaitUntil(() => events.Count(kind => kind == NotificationRefreshKind.Refresh) == 1);
        await hub.Clients.All.SendAsync("NotificationsRefresh", hint, lifetime.Token);
        await Task.Delay(100);
        Assert.Equal(1, events.Count(kind => kind == NotificationRefreshKind.Refresh));

        Interlocked.Exchange(ref failPoll, 1); // Break one real client HTTP poll, not a protocol frame.
        await hub.Clients.All.SendAsync("NotificationsRefresh", hint, lifetime.Token); // Wake pending poll.
        await WaitUntil(() => events.Count(kind => kind == NotificationRefreshKind.Connected) == 2);
        Assert.True(events.Count(kind => kind == NotificationRefreshKind.TransportFailed) >= 2);

        await subscription.SetActiveAsync(false, lifetime.Token);
        Assert.Equal(HubConnectionState.Disconnected, connection.State);
        var count = events.Count;
        await hub.Clients.All.SendAsync("NotificationsRefresh", new NotificationsRefreshRequestedV1(Guid.NewGuid()), lifetime.Token);
        await Task.Delay(100);
        Assert.Equal(count, events.Count);
        await subscription.SetActiveAsync(true, lifetime.Token);
        await WaitUntil(() => events.Count(kind => kind == NotificationRefreshKind.Connected) == 3);
        await subscription.DisposeAsync();
        Assert.Equal(HubConnectionState.Disconnected, connection.State);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.StartAsync());
    }

    [Fact]
    public async Task MicrosoftClientStartupExhaustsFourAttemptsAndCancellationStopsRetries()
    {
        using var server = CreateHost();
        var requests = new ConcurrentQueue<string>();
        var failures = 0;
        var source = new WasmNotificationRefreshSource(() => new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/notifications", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => new FailNegotiationHandler(server.CreateHandler(), requests, int.MaxValue);
            }).Build());
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var subscription = await source.SubscribeAsync(kind =>
        {
            if (kind == NotificationRefreshKind.TransportFailed) Interlocked.Increment(ref failures);
            return Task.CompletedTask;
        }, lifetime.Token);
        await subscription.SetActiveAsync(true, lifetime.Token);
        await WaitUntil(() => Volatile.Read(ref failures) >= 1, TimeSpan.FromSeconds(10));
        await WaitUntil(() => requests.Count == 4, TimeSpan.FromSeconds(10));
        Assert.Equal(4, requests.Count);
        await subscription.SetActiveAsync(true, lifetime.Token);
        await WaitUntil(() => requests.Count == 5);
        lifetime.Cancel();
        await subscription.DisposeAsync();
        var count = requests.Count;
        await Task.Delay(1200);
        Assert.Equal(count, requests.Count);
    }

    private static TestServer CreateHost() => new(new WebHostBuilder()
        .ConfigureServices(services => services.AddSignalR())
        .Configure(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints => endpoints.MapHub<LifecycleHub>("/hubs/notifications"));
        }));

    private static async Task WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, cancellation.Token);
    }

    public sealed class LifecycleHub : Hub { }

    private sealed class FailNegotiationHandler(HttpMessageHandler inner,
        ConcurrentQueue<string> requests, int failures, Func<bool>? failPoll = null) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            requests.Enqueue(path);
            Assert.False(request.RequestUri.Query.Contains("access_token", StringComparison.Ordinal));
            if (request.Method == HttpMethod.Get && path.EndsWith("/notifications", StringComparison.Ordinal) &&
                failPoll?.Invoke() == true)
                throw new HttpRequestException("Fixture transport interruption");
            if (path.EndsWith("/negotiate", StringComparison.Ordinal) &&
                requests.Count(p => p.EndsWith("/negotiate", StringComparison.Ordinal)) <= failures)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            return base.SendAsync(request, cancellationToken);
        }
    }
}
