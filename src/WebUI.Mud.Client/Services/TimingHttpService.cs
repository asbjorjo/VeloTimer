namespace VeloTime.WebUI.Mud.Client.Services;

public class TimingHttpService(HttpClient httpClient) : ITimingService
{
    public async Task<IEnumerable<InstallationView>> GetInstallationsAsync(CancellationToken cancellationToken)
    {
        var url = $"/api/timing/installations";

        return await httpClient.GetFromJsonAsync<IEnumerable<InstallationView>>(url, cancellationToken) ?? Enumerable.Empty<InstallationView>();
    }

    public async Task<InstallationView> GetInstallationAsync(Guid Id, CancellationToken cancellationToken)
    {
        var url = $"/api/timing/installations/{Id}";

        return await httpClient.GetFromJsonAsync<InstallationView>(url, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Installation not found.");
    }

    public async Task UpdateInstallationAsync(InstallationView installation, CancellationToken cancellationToken = default)
    {
        var url = $"/api/timing/installations/{installation.Id}";
        var response = await httpClient.PutAsJsonAsync(url, installation, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
