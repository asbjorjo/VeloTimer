using System.Text.Json;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.WebUI.Mud.Client.Services;

/// <summary>Retrieves owner display details through the authenticated Web UI backend.</summary>
/// <param name="client">The same-origin HTTP client with antiforgery support.</param>
public sealed class TransponderOwnerHttpService(HttpClient client) : ITransponderOwnerService
{
    /// <inheritdoc />
    public async Task<OwnTransponderProfile> GetOwnProfileAsync(CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync("api/transponder-owners/me", cancellationToken);
        return await ReadAsync<OwnTransponderProfile>(response, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TransponderOwnerView>> ResolveAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync("api/admin/timing/transponder-owners/resolve", userIds, cancellationToken);
        return await ReadAsync<List<TransponderOwnerView>>(response, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TransponderOwnerPage> SearchAsync(string query, int? profileOffset = 0, int? keycloakOffset = 0, CancellationToken cancellationToken = default)
    {
        var path = $"api/admin/timing/transponder-owners/search?query={Uri.EscapeDataString(query)}";
        if (profileOffset.HasValue) path += $"&profileOffset={profileOffset.Value}";
        if (keycloakOffset.HasValue) path += $"&keycloakOffset={keycloakOffset.Value}";
        using var response = await client.GetAsync(path, cancellationToken);
        return await ReadAsync<TransponderOwnerPage>(response, cancellationToken);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var message = "Owner names could not be loaded. Check your access and try again.";
            try
            {
                using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                if (problem.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                    message = detail.GetString() ?? message;
            }
            catch (JsonException) { }
            throw new HttpRequestException(message, null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new HttpRequestException("The owner-name response was empty.");
    }
}
