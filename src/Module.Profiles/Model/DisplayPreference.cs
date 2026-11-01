namespace VeloTime.Module.Profiles.Model;

public sealed class DisplayPreference
{
    public required string Issuer { get; init; }
    public required string Subject { get; init; }
    public required string DistanceUnit { get; set; }
    public required string SpeedUnit { get; set; }
    public required string TimeMode { get; set; }
    public string? TimeZoneId { get; set; }
}
