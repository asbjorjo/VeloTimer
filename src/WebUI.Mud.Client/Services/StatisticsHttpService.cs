using System.Globalization;

namespace VeloTime.WebUI.Mud.Client.Services;

public class StatisticsHttpService(HttpClient httpClient) : IStatisticsService
{
    public async Task<IReadOnlyList<BestStatisticsResultView>> GetBestResultsAsync(Guid? facilityId = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string>();
        if (facilityId.HasValue)
            parameters["facilityId"] = facilityId.Value.ToString();
        if (from.HasValue)
            parameters["from"] = from.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        if (to.HasValue)
            parameters["to"] = to.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        using var query = new FormUrlEncodedContent(parameters);
        var url = "api/statistics/best-results?" + await query.ReadAsStringAsync(cancellationToken);
        return await httpClient.GetFromJsonAsync<List<BestStatisticsResultView>>(url, cancellationToken)
            ?? throw new HttpRequestException("The statistics response was empty.");
    }

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
