using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

// Keep the browser transport facade; API and BFF share the authoritative parser.
public static class StatisticsEntriesQuery
{
    public static string EncodeCursor(AdminStatisticsEntryCursorDTO cursor) => AdminStatisticsEntriesQuery.EncodeCursor(cursor);

    public static string Encode(GetAdminStatisticsEntriesRequest request) => AdminStatisticsEntriesQuery.Encode(request);

    public static bool TryParse(IReadOnlyDictionary<string, string?> values,
        out GetAdminStatisticsEntriesRequest request, out Dictionary<string, string[]> errors) =>
        AdminStatisticsEntriesQuery.TryParse(values, out request, out errors);
}
