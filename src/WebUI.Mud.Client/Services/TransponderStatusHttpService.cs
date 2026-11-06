using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.WebUI.Mud.Client.Services;

public sealed class TransponderStatusHttpService(HttpClient client) : ITransponderStatusService
{
    public async Task<TransponderStatusView> GetAsync(Guid id, int ownershipPage = 0, int warningPage = 0,
        int pageSize = 10, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(
            $"api/transponder-status/{id}?ownershipPage={ownershipPage}&warningPage={warningPage}&pageSize={pageSize}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Transponder status could not be loaded. Check your access and retry.", null, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<TransponderStatusView>(cancellationToken)
            ?? throw new HttpRequestException("The transponder status response was empty.");
    }
}
