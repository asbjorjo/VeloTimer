using System.Net.Http.Json;
using VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.Module.Timing.Interface.Client;

public class TimingHttpClient(HttpClient httpClient) : ITimingClient
{
    public async Task<ICollection<InstallationDTO>> GetAllInstallations(CancellationToken cancellationToken)
    {
        return await httpClient.GetFromJsonAsync<ICollection<InstallationDTO>>("installation", cancellationToken);
    }

    public async Task<InstallationDTO> GetInstallationById(Guid id, CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<InstallationDTO>($"installation/{id}", cancellationToken);
    }

    public async Task<ICollection<InstallationDTO>> GetInstallationsForFacility(Guid id)
    {
        return await httpClient.GetFromJsonAsync<ICollection<InstallationDTO>>($"installation?FacilityId={id}");
    }
}
