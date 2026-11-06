using System.Text.Json;

namespace VeloTime.Module.Notifications.Client;

public partial class NotificationsClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings)
    {
        settings.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        settings.PropertyNameCaseInsensitive = true;
    }
}
