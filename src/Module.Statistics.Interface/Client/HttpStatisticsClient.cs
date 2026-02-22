using Microsoft.AspNetCore.Http;
using System.Net.Http.Json;
using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.Module.Statistics.Interface.Client;

public class HttpStatisticsClient(HttpClient httpClient) : IStatisticsClient
{
    public async Task<GetSamplesResponse> GetSamplesAsync(DateTime? cursor, bool isNextPage, int pageSize)
    {
        var url = $"sample";

        var query = QueryString.Create("pageSize", pageSize.ToString())
            .Add("isNextpage", isNextPage.ToString());

        if (cursor.HasValue)
        {
            query = query.Add("cursor", cursor.Value.ToString("o"));
        }
        var response = await httpClient.GetFromJsonAsync<GetSamplesResponse>(url);
        return response ?? new GetSamplesResponse(Enumerable.Empty<SampleDTO>(), null, null, true);
    }
}
