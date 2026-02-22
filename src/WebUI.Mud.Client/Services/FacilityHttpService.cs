using System.Net.Http.Json;

namespace VeloTime.WebUI.Mud.Client.Services;

public class FacilityHttpService(HttpClient httpClient) : IFacilityService
{
    public async Task<IEnumerable<FacilityView>> GetFacilitiesAsync(CancellationToken cancellationToken)
    {
        var url = $"/api/facilities";

        return await httpClient.GetFromJsonAsync<IEnumerable<FacilityView>>(url) ?? Enumerable.Empty<FacilityView>();
    }

    public async Task<IEnumerable<InstallationView>> GetInstallationsAsync(Guid id, CancellationToken cancellationToken)
    {
        var url = $"/api/facilities/{id}/installations";

        return await httpClient.GetFromJsonAsync<IEnumerable<InstallationView>>(url, cancellationToken: cancellationToken) ?? Enumerable.Empty<InstallationView>();
    }

    public async Task<IEnumerable<CourseLayoutView>> GetFacilityLayoutsAsync(Guid id, CancellationToken tokenToken)
    {
        var url = $"/api/facilities/{id}/layouts";

        return await httpClient.GetFromJsonAsync<IEnumerable<CourseLayoutView>>(url, cancellationToken: tokenToken) ?? Enumerable.Empty<CourseLayoutView>();
    }

    public async Task<CourseLayoutDetailView> GetCourseLayoutDetailAsync(Guid layoutId, CancellationToken cancellationToken = default)
    {
        var url = $"/api/layouts/{layoutId}";

        return await httpClient.GetFromJsonAsync<CourseLayoutDetailView>(url, cancellationToken: cancellationToken);
    }
}
