using Microsoft.Extensions.Hosting;
using VeloTime.Module.Notifications.Storage;

namespace VeloTime.Module.Notifications;

public static class HostingExtensions
{
    public static IHostApplicationBuilder AddModuleNotifications(this IHostApplicationBuilder builder)
    {
        builder.AddModuleStorage<NotificationsDbContext>(connectionName: "velotimedb");
        return builder;
    }
}
