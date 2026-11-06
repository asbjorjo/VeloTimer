using VeloTime.Module.Notifications.Interface.Identity;

namespace VeloTime.Module.Notifications.Api.Identity;

public sealed class NotificationSessionAssertionOptions
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public Dictionary<string, string> PublicKeys { get; set; } = new(StringComparer.Ordinal);
    public int MaximumLifetimeSeconds { get; set; } = NotificationSessionAssertion.DefaultLifetimeSeconds;
    public int ClockSkewSeconds { get; set; } = 5;
}
