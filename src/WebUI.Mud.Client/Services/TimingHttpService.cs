using System.Net.Http.Json;

namespace VeloTime.WebUI.Mud.Client.Services;

public class TimingHttpService(HttpClient httpClient) : ITimingService
{
    public async Task<IEnumerable<InstallationView>> GetInstallationsAsync(CancellationToken cancellationToken)
    {
        var url = $"/api/timing/installations";

        return await httpClient.GetFromJsonAsync<IEnumerable<InstallationView>>(url) ?? Enumerable.Empty<InstallationView>(); ;
    }

    public async Task<InstallationView> GetInstallationAsync(Guid Id, CancellationToken cancellationToken)
    {
        var url = $"/api/timing/installations/{Id}";

        return await httpClient.GetFromJsonAsync<InstallationView>(url, cancellationToken: cancellationToken);
    }
}
