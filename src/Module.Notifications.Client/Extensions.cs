using VeloTime.Module.Notifications.Client;

namespace Microsoft.Extensions.DependencyInjection;

public static class Extensions
{
    public static IHttpClientBuilder AddNotificationsClient(this IServiceCollection services) =>
        services.AddHttpClient<INotificationsClient, NotificationsClient>(
            static client => client.BaseAddress = new("https+http://module-notifications-api"));
}
