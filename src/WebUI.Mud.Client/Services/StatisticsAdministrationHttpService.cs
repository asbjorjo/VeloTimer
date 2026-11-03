using System.Text.Json;
using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

public sealed class StatisticsAdministrationHttpService(HttpClient client) : IStatisticsAdministrationService
{
    private const string Root = "api/admin/statistics";

    public async Task<IReadOnlyList<StatisticsItemDTO>> GetItemsAsync(CancellationToken cancellationToken = default) =>
        await client.GetFromJsonAsync<List<StatisticsItemDTO>>($"{Root}/items", cancellationToken) ?? [];

    public async Task<IReadOnlyList<StatisticsConfigurationDTO>> GetConfigurationsAsync(CancellationToken cancellationToken = default) =>
        await client.GetFromJsonAsync<List<StatisticsConfigurationDTO>>($"{Root}/configurations", cancellationToken) ?? [];

    public async Task<HallOfFameSettingsDTO> GetHallOfFameSettingsAsync(CancellationToken cancellationToken = default) =>
        await GetRequiredJsonAsync<HallOfFameSettingsDTO>($"{Root}/hall-of-fame", cancellationToken);

    public async Task<HallOfFameSettingsDTO> SaveHallOfFameSettingsAsync(
        SaveHallOfFameSettingsRequest request, CancellationToken cancellationToken = default) =>
        await SendForJsonAsync<HallOfFameSettingsDTO>($"{Root}/hall-of-fame", request, cancellationToken);

    public Task SaveItemAsync(Guid? id, SaveStatisticsItemRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(id.HasValue ? HttpMethod.Put : HttpMethod.Post, $"{Root}/items{(id.HasValue ? $"/{id}" : "")}", request, cancellationToken);

    public Task DeleteItemAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Delete, $"{Root}/items/{id}", null, cancellationToken);

    public Task SaveConfigurationAsync(Guid? id, SaveStatisticsConfigurationRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(id.HasValue ? HttpMethod.Put : HttpMethod.Post, $"{Root}/configurations{(id.HasValue ? $"/{id}" : "")}", request, cancellationToken);

    public Task DeleteConfigurationAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Delete, $"{Root}/configurations/{id}", null, cancellationToken);

    private async Task SendAsync<TRequest>(HttpMethod method, string path, TRequest? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return;
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

    private async Task<T> GetRequiredJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await StatisticsHttpService.CreateRequestExceptionAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new HttpRequestException("The Hall of Fame settings response was empty.");
    }

    private async Task<TResponse> SendForJsonAsync<TResponse>(
        string path, SaveHallOfFameSettingsRequest request, CancellationToken cancellationToken)
    {
        using var response = await client.PutAsJsonAsync(path, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await StatisticsHttpService.CreateRequestExceptionAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken)
            ?? throw new HttpRequestException("The saved Hall of Fame settings response was empty.");
    }
}