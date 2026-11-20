namespace VeloTime.Module.Notifications.Service;

public sealed class NotificationException(int statusCode, string code) : Exception(code)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
