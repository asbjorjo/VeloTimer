using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using VeloTime.Module.Notifications.Service;
using VeloTime.Module.Notifications.Storage;

namespace VeloTime.Module.Notifications;

public static class HostingExtensions
{
    public static IHostApplicationBuilder AddModuleNotifications(this IHostApplicationBuilder builder)
    {
        builder.AddModuleStorage<NotificationsDbContext>(connectionName: "velotimedb");
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddDataProtection().SetApplicationName("VeloTime.Notifications");
        builder.Services.AddScoped<NotificationService>();
        builder.Services.AddScoped<NotificationSnapshotService>();
        return builder;
    }
}
