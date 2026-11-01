using System.Text.Json;
using System.Globalization;
using VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

public sealed class TransponderManagementHttpService(HttpClient client) : ITransponderManagementService
{
    private static string Root(bool administration) => administration ? "api/admin/timing" : "api";

    public async Task<TransponderPage> GetTranspondersAsync(bool administration, int page = 0, int pageSize = 10,
        string search = "", bool includeHistory = true, CancellationToken cancellationToken = default,
        TransponderColumnFilters? filters = null, string? sortBy = null, bool sortDescending = false)
    {
        var query = new List<string>
        {
            $"page={page}",
            $"pageSize={pageSize}",
            $"search={Uri.EscapeDataString(search)}",
            $"includeHistory={includeHistory}"
        };
        AddQuery(query, "labelFilter", filters?.Label);
        AddQuery(query, "systemFilter", filters?.System);
        AddQuery(query, "ownerFilter", filters?.Owner);
        AddQuery(query, "lastSeenFilter", filters?.LastSeen?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddQuery(query, "ownedFromFilter", filters?.OwnedFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddQuery(query, "ownedToFilter", filters?.OwnedTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddRange(query, "lastSeen", filters?.LastSeenUtcRange);
        AddRange(query, "ownedFrom", filters?.OwnedFromUtcRange);
        AddRange(query, "ownedTo", filters?.OwnedToUtcRange);
        AddQuery(query, "statusFilter", filters?.Status?.ToString());
        AddQuery(query, "sortBy", sortBy);
        query.Add($"sortDescending={sortDescending}");
        using var response = await client.GetAsync($"{Root(administration)}/transponders?{string.Join('&', query)}", cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TransponderPage>(cancellationToken)
            ?? throw new HttpRequestException("The transponder page response was empty.");
    }

    private static void AddRange(List<string> query, string prefix, UtcInstantRange? range)
    {
        if (range is null) return;
        if (range.StartInclusive.Offset != TimeSpan.Zero || range.EndExclusive.Offset != TimeSpan.Zero)
            throw new ArgumentException($"Both {prefix} UTC instant range bounds must have a zero UTC offset.", nameof(range));

        AddQuery(query, prefix + "UtcStart", range.StartInclusive.ToString("O", CultureInfo.InvariantCulture));
        AddQuery(query, prefix + "UtcEnd", range.EndExclusive.ToString("O", CultureInfo.InvariantCulture));
    }

    private static void AddQuery(List<string> query, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            query.Add($"{name}={Uri.EscapeDataString(value)}");
    }

    public async Task<TransponderLookupPage> LookupTranspondersAsync(int page, string search, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync($"api/admin/timing/transponders/lookup?page={page}&search={Uri.EscapeDataString(search)}", cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TransponderLookupPage>(cancellationToken)
            ?? throw new HttpRequestException("The transponder lookup response was empty.");
    }

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