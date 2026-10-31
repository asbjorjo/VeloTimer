using System.Globalization;

namespace VeloTime.Module.Statistics.Interface.Data;

/// <summary>Shared typed validation and invariant public API/BFF/page URL encoding.</summary>
public static class PublicStatisticsQuery
{
    public const int PageSize = 25;
    public const int FetchSize = PageSize + 1;
    public const string DiscoveryPath = "/api/statistics/public/discovery";
    public const string ResultsPath = "/api/statistics/public/results";

    private static readonly string[] DurationFormats = [@"hh\:mm\:ss\.fff", @"d\.hh\:mm\:ss\.fff"];
    private static readonly HashSet<string> QueryKeys =
        ["facilityId", "itemId", "configurationId", "name", "minTime", "maxTime", "minSpeed", "maxSpeed", "cursor", "direction"];

    public static GetPublicStatisticsResultsRequest Normalize(GetPublicStatisticsResultsRequest request) =>
        request with { Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim() };

    /// <summary>Keys match wire query names and can be used directly in a 400 ValidationProblem.</summary>
    public static Dictionary<string, string[]> Validate(GetPublicStatisticsResultsRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.FacilityId == Guid.Empty) errors["facilityId"] = ["A nonempty facility ID is required."];
        if (request.StatisticsItemId == Guid.Empty) errors["itemId"] = ["A nonempty item ID is required."];
        if (request.StatisticsItemConfigId == Guid.Empty) errors["configurationId"] = ["A nonempty configuration ID is required."];
        ValidateDuration(request.MinDuration, "minTime", errors);
        ValidateDuration(request.MaxDuration, "maxTime", errors);
        if (request.MinDuration > request.MaxDuration) errors["maxTime"] = ["Maximum duration must be at least minimum duration."];
        ValidateSpeed(request.MinSpeedKmh, "minSpeed", errors);
        ValidateSpeed(request.MaxSpeedKmh, "maxSpeed", errors);
        if (request.MinSpeedKmh > request.MaxSpeedKmh) errors["maxSpeed"] = ["Maximum speed must be at least minimum speed."];
        if ((request.Cursor is null) != (request.Direction is null))
            errors["cursor"] = ["Cursor and direction must be supplied together."];
        if (request.Direction.HasValue && !Enum.IsDefined(request.Direction.Value))
            errors["direction"] = ["Direction must be next or previous."];
        if (request.Cursor is { } cursor && !IsValidCursor(cursor))
            errors["cursor"] = ["Cursor requires a nonempty entry ID and a finite UTC timestamp with PostgreSQL microsecond precision."];
        return errors;
    }

    /// <summary>
    /// Parse decoded single-valued query parameters. Callers reject duplicate keys before calling.
    /// Page URLs merge their three path IDs into these query keys before parsing.
    /// </summary>
    public static bool TryParse(
        IReadOnlyDictionary<string, string?> query,
        out GetPublicStatisticsResultsRequest? request,
        out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>();
        foreach (var key in query.Keys)
            if (!QueryKeys.Contains(key)) errors[key] = ["Unsupported public statistics query parameter."];
        var parseErrors = errors;
        string? Value(string key) => query.TryGetValue(key, out var value) ? value : null;
        Guid Id(string key)
        {
            if (Guid.TryParseExact(Value(key), "D", out var id)) return id;
            parseErrors[key] = ["An ID in UUID D format is required."];
            return Guid.Empty;
        }
        TimeSpan? Duration(string key)
        {
            if (!query.ContainsKey(key)) return null;
            if (TryParseDuration(Value(key), out var duration)) return duration;
            parseErrors[key] = ["Use hh:mm:ss.fff or d.hh:mm:ss.fff (invariant, nonnegative)."];
            return null;
        }
        double? Speed(string key)
        {
            if (!query.ContainsKey(key)) return null;
            if (double.TryParse(Value(key), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign |
                    NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var speed) &&
                double.IsFinite(speed) && speed >= 0) return speed;
            parseErrors[key] = ["Use a finite nonnegative invariant number in km/h."];
            return null;
        }

        PublicStatisticsCursorDTO? cursor = null;
        if (query.ContainsKey("cursor") && !TryParseCursor(Value("cursor"), out cursor))
            errors["cursor"] = ["Use a UTC round-trip timestamp, |, and an entry UUID."];
        PublicStatisticsPageDirection? direction = Value("direction") switch
        {
            "next" => PublicStatisticsPageDirection.Next,
            "previous" => PublicStatisticsPageDirection.Previous,
            _ => null
        };
        if (query.ContainsKey("direction") && direction is null)
            errors["direction"] = ["Direction must be next or previous."];
        var candidate = Normalize(new GetPublicStatisticsResultsRequest(
            Id("facilityId"), Id("itemId"), Id("configurationId"), Value("name"),
            Duration("minTime"), Duration("maxTime"), Speed("minSpeed"), Speed("maxSpeed"), cursor, direction));
        foreach (var error in Validate(candidate)) errors.TryAdd(error.Key, error.Value);
        request = errors.Count == 0 ? candidate : null;
        return request is not null;
    }

    public static bool TryParseDuration(string? text, out TimeSpan duration) =>
        TimeSpan.TryParseExact(text, DurationFormats, CultureInfo.InvariantCulture, out duration) &&
        duration >= TimeSpan.Zero;

    /// <summary>Filter bounds only; result duration presentation may truncate sub-millisecond digits.</summary>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero || duration.Ticks % TimeSpan.TicksPerMillisecond != 0)
            throw new ArgumentOutOfRangeException(nameof(duration), "Filter duration must be nonnegative whole milliseconds.");
        return duration.ToString(duration.Days == 0 ? DurationFormats[0] : DurationFormats[1], CultureInfo.InvariantCulture);
    }

    public static string FormatCursor(PublicStatisticsCursorDTO cursor)
    {
        if (!IsValidCursor(cursor))
            throw new ArgumentException("Invalid PostgreSQL cursor.", nameof(cursor));
        return cursor.TimeEnd.ToString("O", CultureInfo.InvariantCulture) + "|" + cursor.Id.ToString("D");
    }

    public static bool TryParseCursor(string? text, out PublicStatisticsCursorDTO? cursor)
    {
        cursor = null;
        if (text is null) return false;
        var parts = text.Split('|');
        if (parts.Length != 2 ||
            !DateTimeOffset.TryParseExact(parts[0], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ||
            time.Offset != TimeSpan.Zero || time.Ticks % 10 != 0 || !IsFiniteDatabaseTimestamp(time) ||
            !Guid.TryParseExact(parts[1], "D", out var id) || id == Guid.Empty) return false;
        cursor = new PublicStatisticsCursorDTO(time, id);
        return true;
    }

    /// <summary>Decoded values. Use FormUrlEncodedContent or equivalent escaping; never concatenate raw values.</summary>
    public static Dictionary<string, string> ToQuery(GetPublicStatisticsResultsRequest request)
    {
        request = Normalize(request);
        if (Validate(request).Count != 0) throw new ArgumentException("Invalid public statistics request.", nameof(request));
        var values = new Dictionary<string, string>
        {
            ["facilityId"] = request.FacilityId.ToString("D"),
            ["itemId"] = request.StatisticsItemId.ToString("D"),
            ["configurationId"] = request.StatisticsItemConfigId.ToString("D")
        };
        if (request.Name is not null) values["name"] = request.Name;
        if (request.MinDuration is { } minTime) values["minTime"] = FormatDuration(minTime);
        if (request.MaxDuration is { } maxTime) values["maxTime"] = FormatDuration(maxTime);
        if (request.MinSpeedKmh is { } minSpeed) values["minSpeed"] = minSpeed.ToString("R", CultureInfo.InvariantCulture);
        if (request.MaxSpeedKmh is { } maxSpeed) values["maxSpeed"] = maxSpeed.ToString("R", CultureInfo.InvariantCulture);
        if (request.Cursor is not null)
        {
            values["cursor"] = FormatCursor(request.Cursor);
            values["direction"] = request.Direction == PublicStatisticsPageDirection.Next ? "next" : "previous";
        }
        return values;
    }

    private static bool IsFiniteDatabaseTimestamp(DateTimeOffset time) =>
        time > DateTimeOffset.MinValue && time < DateTimeOffset.MaxValue;

    private static bool IsValidCursor(PublicStatisticsCursorDTO cursor) =>
        cursor.Id != Guid.Empty && cursor.TimeEnd.Offset == TimeSpan.Zero &&
        cursor.TimeEnd.Ticks % 10 == 0 && IsFiniteDatabaseTimestamp(cursor.TimeEnd);

    private static void ValidateDuration(TimeSpan? duration, string key, Dictionary<string, string[]> errors)
    {
        if (duration is { } value && (value < TimeSpan.Zero || value.Ticks % TimeSpan.TicksPerMillisecond != 0))
            errors[key] = ["Duration must be nonnegative whole milliseconds."];
    }

    private static void ValidateSpeed(double? speed, string key, Dictionary<string, string[]> errors)
    {
        if (speed is { } value && (!double.IsFinite(value) || value < 0))
            errors[key] = ["Speed must be finite and nonnegative (km/h)."];
    }
}
