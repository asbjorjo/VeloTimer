using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using VeloTime.Module.Notifications.Interface.Messages;
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
public sealed class NotificationsRedisBackplaneTests
{
    private readonly ITestOutputHelper output;

    public NotificationsRedisBackplaneTests(ITestOutputHelper output) => this.output = output;

    [NotificationsRedisFact]
    public async Task RedisBackplaneDeliversBridgeHintAcrossTwoWebHostsWithinTwoSeconds()
    {
        var redis = Environment.GetEnvironmentVariable("VELOTIME_TEST_REDIS")!;
        var prefix = RedisChannel.Literal("velotime:test:notifications:" + Guid.NewGuid().ToString("N"));
        using var first = CreateHost(redis, prefix);
        using var second = CreateHost(redis, prefix);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        using var socket = await second.CreateWebSocketClient().ConnectAsync(
            new Uri("ws://localhost/hubs/notifications"), timeout.Token);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"),
            WebSocketMessageType.Text, true, timeout.Token);
        var handshake = await ReceiveMessageAsync(socket, timeout.Token);
        Assert.Equal("{}", handshake.TrimEnd('\u001e'));

        var eventId = Guid.NewGuid();
        var started = Stopwatch.GetTimestamp();
        await new NotificationsRefreshBridge(first.Services.GetRequiredService<IHubContext<NotificationsHub>>())
            .OnHandle(new NotificationsRefreshRequestedV1(eventId), timeout.Token);
        var refresh = await ReceiveMessageAsync(socket, timeout.Token);
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.Contains("\"target\":\"NotificationsRefresh\"", refresh, StringComparison.Ordinal);
        Assert.Contains(eventId.ToString(), refresh, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("subject", refresh, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("issuer", refresh, StringComparison.OrdinalIgnoreCase);
        output.WriteLine($"Local SignalR bridge-to-client latency: {elapsed.TotalMilliseconds:F0} ms (target <= 2000 ms).");
        Assert.True(elapsed <= TimeSpan.FromSeconds(2),
            $"Local connected-client refresh took {elapsed.TotalMilliseconds:F0} ms; target is 2000 ms.");
    }

    private static TestServer CreateHost(string redis, RedisChannel prefix) => new(new WebHostBuilder()
        .ConfigureServices(services => services.AddSignalR().AddStackExchangeRedis(redis,
            options => options.Configuration.ChannelPrefix = prefix))
        .Configure(app =>
        {
            app.UseWebSockets();
            app.UseRouting();
            app.UseEndpoints(endpoints => endpoints.MapHub<NotificationsHub>("/hubs/notifications"));
        }));

    private static async Task<string> ReceiveMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new byte[2048];
        while (true)
        {
            var received = await socket.ReceiveAsync(buffer, cancellationToken);
            if (received.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("SignalR closed before delivering the expected frame.");
            builder.Append(Encoding.UTF8.GetString(buffer, 0, received.Count));
            if (builder.ToString().Contains('\u001e')) return builder.ToString();
        }
    }
}
