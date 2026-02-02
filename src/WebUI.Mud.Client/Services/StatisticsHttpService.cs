using Microsoft.AspNetCore.Http;
using System.Net.Http.Json;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.WebUI.Mud.Client.Services;

public class StatisticsHttpService(HttpClient httpClient) : IStatisticsService
{
    public async Task<IEnumerable<SampleView>> GetSamplesAsync(DateTime? cursor, bool isNextPage, int pageSize)
    {
        var url = $"/api/statistics/samples";

        var query = QueryString.Create("pageSize", pageSize.ToString())
            .Add("isNextpage", isNextPage.ToString());

        if (cursor.HasValue)
        {
            query = query.Add("cursor", cursor.Value.ToString("o"));
        }

        var samples = await httpClient.GetFromJsonAsync<IEnumerable<SampleView>>(url) ?? Enumerable.Empty<SampleView>();

        return samples;
    }
}
