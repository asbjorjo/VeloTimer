namespace VeloTime.WebUI.Mud.Client.Services;

public class ProfileHttpService(HttpClient httpClient) : IProfileService
{
    public async Task<string> GetUsernameAsync(CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<string>("api/profile/username", ct)
        ?? throw new InvalidOperationException("The username is missing.");

    public async Task<IEnumerable<LinkedAccountView>> GetLinkedAccountsAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<IEnumerable<LinkedAccountView>>("api/profile/accounts", cancellationToken)
        ?? Enumerable.Empty<LinkedAccountView>();

    public async Task<string> GetLinkAccountAsync(string RedirectUri, string Provider, CancellationToken cancellation = default)
    {
        using var response = await httpClient.PostAsJsonAsync($"api/profile/accounts/{Uri.EscapeDataString(Provider)}/link", RedirectUri, cancellation);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<string>(cancellation)
            ?? throw new InvalidOperationException("The account link is missing.");
    }

    public async Task DeleteLinkedAccountAsync(string Provider, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"api/profile/accounts/{Uri.EscapeDataString(Provider)}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}