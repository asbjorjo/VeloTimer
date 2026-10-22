using System.Globalization;

namespace VeloTime.WebUI.Mud.Client.Services;

public class StatisticsHttpService(HttpClient httpClient) : IStatisticsService
{
    public async Task<IEnumerable<SampleView>> GetSamplesAsync(DateTime? cursor, bool isNextPage, int pageSize, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string>
        {
            ["pageSize"] = pageSize.ToString(CultureInfo.InvariantCulture),
            ["isNextPage"] = isNextPage ? "true" : "false"
        };

        if (cursor.HasValue)
        {
            parameters["cursor"] = cursor.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        using var query = new FormUrlEncodedContent(parameters);
        var url = "api/statistics/samples?" + await query.ReadAsStringAsync(cancellationToken);
        var samples = await httpClient.GetFromJsonAsync<IEnumerable<SampleView>>(url, cancellationToken) ?? Enumerable.Empty<SampleView>();

        return samples;
    }
}
