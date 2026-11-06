using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using VeloTime.Module.Notifications.Interface.Messages;
using VeloTime.WebUI.Mud.Notifications;

namespace VeloTime.Platform.Test;

public sealed class NotificationsRefreshBridgeTests
{
    [Fact]
    public async Task BrokerBridgeBroadcastsOnlyMinimalRefreshHintToAllHubClients()
    {
        var proxy = new RecordingClientProxy();
        var published = new List<(RedisChannel Channel, RedisValue Value)>();
        var subscriber = PlatformServiceTests.TestProxy.Create<ISubscriber>((method, args) =>
        {
            Assert.Equal("PublishAsync", method.Name);
            published.Add(((RedisChannel)args[0]!, (RedisValue)args[1]!));
            return 2L;
        });
        var redis = PlatformServiceTests.TestProxy.Create<IConnectionMultiplexer>((method, _) =>
        {
            Assert.Equal("GetSubscriber", method.Name);
            return subscriber;
        });
        var fanout = new NotificationRefreshFanout(redis, NullLogger<NotificationRefreshFanout>.Instance);
        var bridge = new NotificationsRefreshBridge(new RecordingHubContext(proxy), fanout);
        var message = new NotificationsRefreshRequestedV1(Guid.NewGuid());
        using var cancellation = new CancellationTokenSource();

        await bridge.OnHandle(message, cancellation.Token);

        Assert.Equal("NotificationsRefresh", proxy.Method);
        Assert.Equal([message], proxy.Arguments);
        Assert.Equal(cancellation.Token, proxy.CancellationToken);
        var publication = Assert.Single(published);
        Assert.Equal("velotime:webui:notifications:server-refresh:v1", publication.Channel.ToString());
        Assert.Equal(message.EventId.ToString("D"), publication.Value.ToString());
        Assert.Equal(["EventId"], typeof(NotificationsRefreshRequestedV1)
            .GetProperties().Select(property => property.Name));
    }

    private sealed class RecordingHubContext(IClientProxy all) : IHubContext<NotificationsHub>
    {
        public IHubClients Clients { get; } = new RecordingHubClients(all);
        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class RecordingHubClients(IClientProxy all) : IHubClients
    {
        public IClientProxy All => all;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class RecordingClientProxy : IClientProxy
    {
        public string? Method { get; private set; }
        public object?[] Arguments { get; private set; } = [];
        public CancellationToken CancellationToken { get; private set; }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Method = method;
            Arguments = args;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
