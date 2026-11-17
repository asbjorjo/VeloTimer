using System.Globalization;
using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

// Browser/BFF transport only. The Statistics API remains authoritative for validation.
public static class StatisticsEntriesQuery
{
    public static string EncodeCursor(AdminStatisticsEntryCursorDTO cursor) =>
        $"{cursor.TimeEnd.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)}|{cursor.Id:D}";

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
            ["pageSize"] = request.PageSize.ToString(CultureInfo.InvariantCulture)
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
        string[] allowed = ["facilityId", "statisticsItemId", "statisticsItemConfigId", "transponderId", "isValid", "cursor", "direction", "pageSize"];
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
        AdminStatisticsEntryCursorDTO? cursor = null;
        if (values.ContainsKey("cursor"))
        {
            if (PublicStatisticsQuery.TryParseCursor(Value("cursor"), out var parsed)) cursor = new(parsed!.TimeEnd, parsed.Id);
            else failures["cursor"] = ["Supply a UTC round-trip timestamp, |, and an entry UUID."];
        }
        AdminStatisticsEntriesPageDirection? direction = Value("direction") switch
        {
            "next" => AdminStatisticsEntriesPageDirection.Next,
            "previous" => AdminStatisticsEntriesPageDirection.Previous,
            _ => null
        };
        if (values.ContainsKey("direction") && direction is null) failures["direction"] = ["Supply next or previous."];
        if (values.ContainsKey("cursor") != values.ContainsKey("direction")) failures["cursor"] = ["Cursor and direction must be supplied together."];
        request = new(Id("facilityId"), Id("statisticsItemId"), Id("statisticsItemConfigId"), Id("transponderId"), valid, cursor, direction, size);
        errors = failures;
        return failures.Count == 0;
    }
}
