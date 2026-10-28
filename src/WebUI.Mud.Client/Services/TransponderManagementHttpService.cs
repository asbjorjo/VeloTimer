using System.Text.Json;
using VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

public sealed class TransponderManagementHttpService(HttpClient client) : ITransponderManagementService
{
    private static string Root(bool administration) => administration ? "api/admin/timing" : "api";

    public async Task<IReadOnlyList<TransponderDTO>> GetTranspondersAsync(bool administration, CancellationToken cancellationToken = default) =>
        await client.GetFromJsonAsync<List<TransponderDTO>>($"{Root(administration)}/transponders", cancellationToken) ?? [];

    public Task RegisterAsync(string code, bool administration, CancellationToken cancellationToken = default) =>
        Send(HttpMethod.Post, $"{Root(administration)}/transponders", new ClaimTransponderRequest(code), cancellationToken);

    /// <inheritdoc />
    public async Task<RegisterTransponderResult> RegisterOwnershipAsync(RegisterTransponderRequest request, bool administration, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync($"{Root(administration)}/transponders/register", request, cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<RegisterTransponderResult>(cancellationToken)
            ?? throw new HttpRequestException("The registration response was empty.");
    }

    public Task SaveOwnershipAsync(Guid? id, SaveTransponderOwnershipRequest request, CancellationToken cancellationToken = default) =>
        Send(id.HasValue ? HttpMethod.Put : HttpMethod.Post, $"api/admin/timing/ownerships{(id.HasValue ? $"/{id}" : "")}", request, cancellationToken);

    public Task EndOwnershipAsync(Guid id, bool administration, CancellationToken cancellationToken = default) =>
        Send<object>(HttpMethod.Post, $"{Root(administration)}/ownerships/{id}/end", null, cancellationToken);

    public Task DeleteOwnershipAsync(Guid id, bool administration, CancellationToken cancellationToken = default) =>
        Send<object>(HttpMethod.Delete, $"{Root(administration)}/ownerships/{id}", null, cancellationToken);

    public Task SaveTransponderAsync(Guid id, string label, CancellationToken cancellationToken = default) =>
        Send(HttpMethod.Put, $"api/admin/timing/transponders/{id}", new SaveTransponderRequest(label), cancellationToken);

    public Task DeleteTransponderAsync(Guid id, CancellationToken cancellationToken = default) =>
        Send<object>(HttpMethod.Delete, $"api/admin/timing/transponders/{id}", null, cancellationToken);

    private async Task Send<TRequest>(HttpMethod method, string path, TRequest? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccess(response, cancellationToken);
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var message = "The request could not be completed.";
        try
        {
            using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (problem.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                message = detail.GetString() ?? message;
        }
        catch (JsonException) { }
        throw new HttpRequestException(message, null, response.StatusCode);
    }
}