namespace VeloTime.Module.Statistics.Interface.Data;

/// <summary>One public profile's personal best; speed is metres per second.</summary>
public sealed record HallOfFameTimeDTO(string ProfileName, TimeSpan Duration, double SpeedMetresPerSecond, Guid FacilityId);

/// <summary>One public profile's valid Lap results and current-definition distance in metres.</summary>
public sealed record HallOfFameLapsDTO(string ProfileName, long LapCount, double TotalDistanceMetres);

/// <summary>A fixed top-ten leaderboard. An unavailable item is distinct from an empty leaderboard.</summary>
public sealed record HallOfFameDTO(bool IsAvailable, IReadOnlyList<HallOfFameTimeDTO> Times, IReadOnlyList<HallOfFameLapsDTO> Laps);

/// <summary>Validated all-time leaderboard selection; null facility means all facilities.</summary>
public sealed record HallOfFameRequest(string Category, Guid? FacilityId = null);

public static class HallOfFameQuery
{
    public const int Limit = 10;
    public const string Path = "/api/statistics/hall-of-fame";
    public static IReadOnlyList<string> Categories { get; } = Array.AsReadOnly(new[] { "lap", "200m", "1000m", "3000m", "4000m", "laps" });

    public static string ItemName(string category) => category switch
    {
        "lap" or "laps" => "Lap",
        "200m" or "1000m" or "3000m" or "4000m" => category,
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    public static bool TryParse(IReadOnlyDictionary<string, string?> values,
        out HallOfFameRequest request, out Dictionary<string, string[]> errors)
    {
        errors = new(StringComparer.Ordinal);
        foreach (var key in values.Keys.Where(key => key is not ("category" or "facilityId")))
            errors[key] = ["Unsupported Hall of Fame parameter."];
        var category = values.TryGetValue("category", out var selected) ? selected : "lap";
        if (category is null || !Categories.Contains(category))
            errors["category"] = ["Select lap, 200m, 1000m, 3000m, 4000m or laps."];
        Guid? facility = null;
        if (values.TryGetValue("facilityId", out var id))
        {
            if (!Guid.TryParse(id, out var parsed) || parsed == Guid.Empty)
                errors["facilityId"] = ["Provide a nonempty facility ID."];
            else facility = parsed;
        }
        request = new(category ?? "", facility);
        return errors.Count == 0;
    }

    public static Dictionary<string, string?> ToQuery(HallOfFameRequest request)
    {
        var values = new Dictionary<string, string?> { ["category"] = request.Category };
        if (request.FacilityId.HasValue) values["facilityId"] = request.FacilityId.Value.ToString("D");
        if (!TryParse(values, out _, out _)) throw new ArgumentException("Invalid Hall of Fame selection.", nameof(request));
        return values;
    }
}
