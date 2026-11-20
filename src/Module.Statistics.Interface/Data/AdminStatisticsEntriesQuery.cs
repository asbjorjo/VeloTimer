using System.Globalization;

namespace VeloTime.Module.Statistics.Interface.Data;

/// <summary>Shared invariant administrative query transport and validation for API and BFF.</summary>
public static class AdminStatisticsEntriesQuery
{
    public static string SortValue(AdminStatisticsEntriesSort sort) => sort switch
    {
        AdminStatisticsEntriesSort.TimeEnd => "timeEnd",
        AdminStatisticsEntriesSort.Duration => "duration",
        AdminStatisticsEntriesSort.Speed => "speed",
        _ => throw new ArgumentOutOfRangeException(nameof(sort))
    };

    public static string OrderValue(AdminStatisticsEntriesSortOrder order) => order switch
    {
        AdminStatisticsEntriesSortOrder.Ascending => "asc",
        AdminStatisticsEntriesSortOrder.Descending => "desc",
        _ => throw new ArgumentOutOfRangeException(nameof(order))
    };

    /// <summary>Legacy timestamp|UUID for TimeEnd-desc; v1|sort|order|key|UUID otherwise.</summary>
    public static string EncodeCursor(AdminStatisticsEntryCursorDTO cursor)
    {
        var key = cursor.Sort switch
        {
            AdminStatisticsEntriesSort.TimeEnd => cursor.TimeEnd.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            AdminStatisticsEntriesSort.Duration => cursor.Duration?.ToString("c", CultureInfo.InvariantCulture),
            AdminStatisticsEntriesSort.Speed => cursor.Speed,
            _ => null
        };
        var text = cursor.Sort == AdminStatisticsEntriesSort.TimeEnd && cursor.Order == AdminStatisticsEntriesSortOrder.Descending
            ? $"{key}|{cursor.Id:D}"
            : $"v1|{SortValue(cursor.Sort)}|{OrderValue(cursor.Order)}|{key}|{cursor.Id:D}";
        if (!TryParseCursor(text, cursor.Sort, cursor.Order, out var parsed) ||
            parsed != cursor with { TimeEnd = cursor.TimeEnd.ToUniversalTime() })
            throw new ArgumentException("Invalid administrative entry cursor.", nameof(cursor));
        return text;
    }

    public static bool TryParseCursor(string? text, AdminStatisticsEntriesSort sort,
        AdminStatisticsEntriesSortOrder order, out AdminStatisticsEntryCursorDTO? cursor)
    {
        cursor = null;
        if (!Enum.IsDefined(sort) || !Enum.IsDefined(order) || text is null) return false;
        var parts = text.Split('|');
        string key, idText;
        if (parts.Length == 2 && sort == AdminStatisticsEntriesSort.TimeEnd && order == AdminStatisticsEntriesSortOrder.Descending)
            (key, idText) = (parts[0], parts[1]);
        else if (parts.Length == 5 && parts[0] == "v1" && parts[1] == SortValue(sort) && parts[2] == OrderValue(order))
            (key, idText) = (parts[3], parts[4]);
        else return false;
        if (!Guid.TryParseExact(idText, "D", out var id) || id == Guid.Empty) return false;
        switch (sort)
        {
            case AdminStatisticsEntriesSort.TimeEnd:
                if (!PublicStatisticsQuery.TryParseCursor($"{key}|{idText}", out var time)) return false;
                cursor = new(time!.TimeEnd, id, sort, order);
                break;
            case AdminStatisticsEntriesSort.Duration:
                if (!TimeSpan.TryParseExact(key, "c", CultureInfo.InvariantCulture, out var duration) || duration.Ticks % 10 != 0) return false;
                cursor = new(default, id, sort, order, Duration: duration);
                break;
            case AdminStatisticsEntriesSort.Speed:
                if (!double.TryParse(key, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) ||
                    speed.ToString("R", CultureInfo.InvariantCulture) != key) return false;
                cursor = new(default, id, sort, order, Speed: key);
                break;
        }
        return true;
    }

    public static string Encode(GetAdminStatisticsEntriesRequest request)
    {
        var values = new Dictionary<string, string?>
        {
            ["facilityId"] = request.FacilityId?.ToString("D"),
            ["statisticsItemId"] = request.StatisticsItemId?.ToString("D"),
            ["statisticsItemConfigId"] = request.StatisticsItemConfigId?.ToString("D"),
            ["transponderId"] = request.TransponderId?.ToString("D"),
            ["isValid"] = request.IsValid?.ToString().ToLowerInvariant(),
            ["cursor"] = request.Cursor is { } cursor ? EncodeCursor(cursor) : null,
            ["direction"] = request.Direction?.ToString().ToLowerInvariant(),
            ["pageSize"] = request.PageSize.ToString(CultureInfo.InvariantCulture),
            ["minDuration"] = request.MinDuration?.ToString("c", CultureInfo.InvariantCulture),
            ["maxDuration"] = request.MaxDuration?.ToString("c", CultureInfo.InvariantCulture),
            ["minSpeed"] = request.MinSpeedMetresPerSecond?.ToString("R", CultureInfo.InvariantCulture),
            ["maxSpeed"] = request.MaxSpeedMetresPerSecond?.ToString("R", CultureInfo.InvariantCulture),
            ["sort"] = SortValue(request.Sort),
            ["order"] = OrderValue(request.Order)
        };
        return string.Join("&", values.Where(pair => pair.Value is not null)
            .Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value!)}"));
    }

    public static bool TryParse(IReadOnlyDictionary<string, string?> values,
        out GetAdminStatisticsEntriesRequest request, out Dictionary<string, string[]> errors)
    {
        var failures = new Dictionary<string, string[]>();
        string? Value(string key) => values.GetValueOrDefault(key);
        Guid? Id(string key)
        {
            if (!values.ContainsKey(key)) return null;
            if (Guid.TryParseExact(Value(key), "D", out var id) && id != Guid.Empty) return id;
            failures[key] = ["Supply a nonempty UUID."];
            return null;
        }
        string[] allowed = ["facilityId", "statisticsItemId", "statisticsItemConfigId", "transponderId", "isValid", "cursor", "direction", "pageSize", "minDuration", "maxDuration", "minSpeed", "maxSpeed", "sort", "order"];
        foreach (var key in values.Keys)
            if (!allowed.Contains(key)) failures[key] = ["Unsupported parameter."];
        bool? valid = null;
        if (values.ContainsKey("isValid"))
        {
            if (bool.TryParse(Value("isValid"), out var parsed)) valid = parsed;
            else failures["isValid"] = ["Supply true or false."];
        }
        var size = 50;
        if (values.ContainsKey("pageSize") &&
            (!int.TryParse(Value("pageSize"), NumberStyles.None, CultureInfo.InvariantCulture, out size) || size is < 1 or > 100))
            failures["pageSize"] = ["Page size must be 1–100."];
        var sort = Value("sort") switch
        {
            null when !values.ContainsKey("sort") => AdminStatisticsEntriesSort.TimeEnd,
            "timeEnd" => AdminStatisticsEntriesSort.TimeEnd,
            "duration" => AdminStatisticsEntriesSort.Duration,
            "speed" => AdminStatisticsEntriesSort.Speed,
            _ => (AdminStatisticsEntriesSort?)null
        };
        var order = Value("order") switch
        {
            null when !values.ContainsKey("order") => AdminStatisticsEntriesSortOrder.Descending,
            "asc" => AdminStatisticsEntriesSortOrder.Ascending,
            "desc" => AdminStatisticsEntriesSortOrder.Descending,
            _ => (AdminStatisticsEntriesSortOrder?)null
        };
        if (sort is null) failures["sort"] = ["Supply timeEnd, duration or speed."];
        if (order is null) failures["order"] = ["Supply asc or desc."];
        TimeSpan? Duration(string key)
        {
            if (!values.ContainsKey(key)) return null;
            if (TimeSpan.TryParseExact(Value(key), "c", CultureInfo.InvariantCulture, out var duration)) return duration;
            failures[key] = ["Supply an invariant duration in c format (signed and zero durations are allowed)."];
            return null;
        }
        double? Speed(string key)
        {
            if (!values.ContainsKey(key)) return null;
            if (double.TryParse(Value(key), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                    CultureInfo.InvariantCulture, out var speed) && double.IsFinite(speed) && speed >= 0) return speed;
            failures[key] = ["Supply a finite nonnegative speed in metres per second."];
            return null;
        }
        var minDuration = Duration("minDuration");
        var maxDuration = Duration("maxDuration");
        var minSpeed = Speed("minSpeed");
        var maxSpeed = Speed("maxSpeed");
        if (minDuration > maxDuration) failures["minDuration"] = ["Minimum duration must not exceed maximum duration."];
        if (minSpeed > maxSpeed) failures["minSpeed"] = ["Minimum speed must not exceed maximum speed."];
        AdminStatisticsEntryCursorDTO? cursor = null;
        if (values.ContainsKey("cursor"))
        {
            if (sort.HasValue && order.HasValue && TryParseCursor(Value("cursor"), sort.Value, order.Value, out var parsed)) cursor = parsed;
            else failures["cursor"] = ["Supply a valid cursor issued for the selected sort and order."];
        }
        AdminStatisticsEntriesPageDirection? direction = Value("direction") switch
        {
            "next" => AdminStatisticsEntriesPageDirection.Next,
            "previous" => AdminStatisticsEntriesPageDirection.Previous,
            _ => null
        };
        if (values.ContainsKey("direction") && direction is null) failures["direction"] = ["Supply next or previous."];
        if (values.ContainsKey("cursor") != values.ContainsKey("direction")) failures["cursor"] = ["Cursor and direction must be supplied together."];
        request = new(Id("facilityId"), Id("statisticsItemId"), Id("statisticsItemConfigId"), Id("transponderId"), valid, cursor, direction, size,
            minDuration, maxDuration, minSpeed, maxSpeed, sort ?? AdminStatisticsEntriesSort.TimeEnd, order ?? AdminStatisticsEntriesSortOrder.Descending);
        errors = failures;
        return failures.Count == 0;
    }
}
