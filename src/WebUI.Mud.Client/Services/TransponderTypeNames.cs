namespace VeloTime.WebUI.Mud.Client.Services;

/// <summary>Display names for Timing's transponder type values.</summary>
public static class TransponderTypeNames
{
    public static string Display(string? type) => type switch
    {
        "MylapsProChip" => "MYLAPS ProChip",
        null or "" => string.Empty,
        _ => type
    };
}
