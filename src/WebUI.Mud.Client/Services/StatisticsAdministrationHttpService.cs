using System.Text.Json;
using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

public sealed class StatisticsAdministrationHttpService(HttpClient client) : IStatisticsAdministrationService
{
    private const string Root = "api/admin/statistics";

    public async Task<StatisticsEntryContributionsDTO> GetEntryContributionsAsync(Guid entryId, CancellationToken cancellationToken = default) =>
        await client.GetFromJsonAsync<StatisticsEntryContributionsDTO>($"{Root}/entries/{entryId:D}/contributions", cancellationToken)
        ?? throw new InvalidDataException("The statistics entry contributions are missing.");

    public async Task<IReadOnlyDictionary<Guid, string>> GetCoursePointNamesAsync(IReadOnlyList<Guid> coursePointIds, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync($"{Root}/course-point-names/resolve", coursePointIds, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Dictionary<Guid, string>>(cancellationToken)
            ?? throw new InvalidDataException("The course-point names are missing.");
    }

    public async Task<AdminStatisticsEntriesPageDTO> ListEntriesAsync(GetAdminStatisticsEntriesRequest request, CancellationToken cancellationToken = default) =>
        await client.GetFromJsonAsync<AdminStatisticsEntriesPageDTO>($"{Root}/entries?{StatisticsEntriesQuery.Encode(request)}", cancellationToken)
        ?? throw new InvalidDataException("The statistics entry page is missing.");

    public async Task<AdminStatisticsEntryDTO> SetManualInvalidationAsync(Guid id, StatisticsEntryManualInvalidationRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await client.PutAsJsonAsync($"{Root}/entries/{id}/manual-invalidation", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AdminStatisticsEntryDTO>(cancellationToken)
            ?? throw new InvalidDataException("The statistics entry is missing.");
    }

    public async Task<IReadOnlyList<StatisticsItemDTO>> GetItemsAsync(CancellationToken cancellationToken = default) =>
        await client.GetFromJsonAsync<List<StatisticsItemDTO>>($"{Root}/items", cancellationToken) ?? throw new InvalidDataException("The statistics definition list is missing.");

    public async Task<IReadOnlyList<StatisticsConfigurationDTO>> GetConfigurationsAsync(CancellationToken cancellationToken = default) =>
        await client.GetFromJsonAsync<List<StatisticsConfigurationDTO>>($"{Root}/configurations", cancellationToken) ?? throw new InvalidDataException("The statistics definition list is missing.");

    public Task SaveItemAsync(Guid? id, SaveStatisticsItemRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(id.HasValue ? HttpMethod.Put : HttpMethod.Post, $"{Root}/items{(id.HasValue ? $"/{id}" : "")}", request, cancellationToken);

    public Task DeleteItemAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Delete, $"{Root}/items/{id}", null, cancellationToken);

    public Task SaveConfigurationAsync(Guid? id, SaveStatisticsConfigurationRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(id.HasValue ? HttpMethod.Put : HttpMethod.Post, $"{Root}/configurations{(id.HasValue ? $"/{id}" : "")}", request, cancellationToken);

    public Task DeleteConfigurationAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Delete, $"{Root}/configurations/{id}", null, cancellationToken);

    public async Task<RecomputeOverviewDTO> GetRecomputeOverviewAsync(CancellationToken cancellationToken = default) =>
        await client.GetFromJsonAsync<RecomputeOverviewDTO>($"{Root}/recompute", cancellationToken)
        ?? throw new InvalidDataException("The statistics recompute overview is missing.");

    public async Task<RecomputeJobDTO> CreateRecomputeJobAsync(CreateRecomputeJobRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync($"{Root}/recompute/jobs", request, cancellationToken);
        if (!response.IsSuccessStatusCode) await ThrowAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<RecomputeJobDTO>(cancellationToken)
            ?? throw new InvalidDataException("The statistics recompute job is missing.");
    }

    private async Task SendAsync<TRequest>(HttpMethod method, string path, TRequest? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return;
        await ThrowAsync(response, cancellationToken);
    }

    private static async Task ThrowAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var message = "The request could not be completed.";
        try
        {
            using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (problem.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                message = detail.GetString() ?? message;
        }
        catch (JsonException)
        {
        }
        throw new HttpRequestException(message, null, response.StatusCode);
    }
}