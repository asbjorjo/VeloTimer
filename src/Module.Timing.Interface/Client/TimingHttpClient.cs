using System.Net.Http.Json;
using VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.Module.Timing.Interface.Client;

public class TimingHttpClient : ITimingClient
{
    private readonly HttpClient httpClient;

    public TimingHttpClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
        httpClient.BaseAddress = new Uri("http://velotime.api:8080/api/timing/");
    }

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
