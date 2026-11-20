using System.Net;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.WebUI.Mud.Client.Services;

public class AuthDebugHttpService(HttpClient httpClient) : IAuthDebugService
{
    public async Task<AuthDebugView?> GetAccessTokenInfoAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("api/debug/auth/token", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthDebugView>(cancellationToken);
    }
}
