using Duende.AccessTokenManagement;
using Duende.AccessTokenManagement.OpenIdConnect;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Notifications.Interface.Messages;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.WebUI.Mud.Notifications;

public static class NotificationHostingExtensions
{
    public static void AddWebUiNotifications(this WebApplicationBuilder builder)
    {
        builder.Services.AddNotificationsClient().AddDefaultAccessTokenResiliency()
            .AddUserAccessTokenHandler();
        builder.Services.AddScoped<INotificationsService, NotificationsBrowserService>();
        builder.Services.AddScoped<NotificationUiState>();
        builder.Services.AddAuthorization(options => options.AddPolicy("Notifications.Admin", policy =>
            policy.RequireAuthenticatedUser().RequireClaim("velotime_permission", "velotime.admin")));
        builder.Services.AddSignalR().AddStackExchangeRedis(
            builder.Configuration.GetConnectionString("cache")
                ?? throw new InvalidOperationException("Redis connection is required for notifications."),
            options => options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("velotime:webui:notifications"));
        builder.Services.AddScoped<NotificationsRefreshBridge>();
        builder.Services.AddSlimMessageBus(bus =>
        {
            bus.WithProviderServiceBus(options =>
            {
                options.ConnectionString = builder.Configuration.GetConnectionString("MessageBus");
                options.TopologyProvisioning.Enabled = false;
            });
            bus.AddJsonSerializer();
            bus.Consume<NotificationsRefreshRequestedV1>(consumer => consumer
                .Topic("velotime-notifications").SubscriptionName("webui-notifications")
                .SubscriptionSqlFilter("MessageName = 'NotificationsRefreshRequestedV1'")
                .WithConsumer<NotificationsRefreshBridge>().Instances(1));
        });
    }
}
