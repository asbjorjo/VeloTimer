using Facilities = VeloTime.Module.Facilities.Interface.Data;
using Timing = VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

public sealed class InfrastructureAdministrationHttpService(HttpClient httpClient) : IInfrastructureAdministrationService
{
    private readonly AdministrationHttpClient client = new(httpClient);
    public async Task<IReadOnlyList<Facilities.FacilityDTO>> GetFacilitiesAsync(CancellationToken cancellationToken = default) =>
        await client.SendAsync<List<Facilities.FacilityDTO>>(HttpMethod.Get, "api/admin/lookups/facilities", null, cancellationToken) ?? [];
    public async Task<IReadOnlyList<Facilities.CourseLayoutDTO>> GetLayoutsAsync(Guid facilityId, CancellationToken cancellationToken = default) =>
        await client.SendAsync<List<Facilities.CourseLayoutDTO>>(HttpMethod.Get, $"api/admin/lookups/facilities/{facilityId}/layouts", null, cancellationToken) ?? [];
    public async Task<Facilities.FacilityDTO> SaveFacilityAsync(Guid? id, Facilities.SaveFacilityRequest request, CancellationToken cancellationToken = default) =>
        await client.SendAsync<Facilities.FacilityDTO>(id.HasValue ? HttpMethod.Put : HttpMethod.Post, $"api/admin/facilities{Suffix(id)}", request, cancellationToken) ?? throw new HttpRequestException("Missing facility response.");
    public Task DeleteFacilityAsync(Guid id, CancellationToken cancellationToken = default) =>
        client.SendAsync<object>(HttpMethod.Delete, $"api/admin/facilities/{id}", null, cancellationToken);
    public async Task<Facilities.CourseLayoutDTO> SaveLayoutAsync(Guid facilityId, Guid? id, Facilities.SaveCourseLayoutRequest request, CancellationToken cancellationToken = default) =>
        await client.SendAsync<Facilities.CourseLayoutDTO>(id.HasValue ? HttpMethod.Put : HttpMethod.Post, $"api/admin/facilities/{facilityId}/layouts{Suffix(id)}", request, cancellationToken) ?? throw new HttpRequestException("Missing layout response.");
    public Task DeleteLayoutAsync(Guid facilityId, Guid id, CancellationToken cancellationToken = default) =>
        client.SendAsync<object>(HttpMethod.Delete, $"api/admin/facilities/{facilityId}/layouts/{id}", null, cancellationToken);
    public Task SetTimingLinkAsync(Guid pointId, Guid? timingPointId, CancellationToken cancellationToken = default) =>
        client.SendAsync<object>(HttpMethod.Put, $"api/admin/coursepoints/{pointId}/timing-link", new Facilities.SetCoursePointTimingLinkRequest(timingPointId), cancellationToken);
    public async Task<IReadOnlyList<Timing.InstallationDTO>> GetInstallationsAsync(CancellationToken cancellationToken = default) =>
        await client.SendAsync<List<Timing.InstallationDTO>>(HttpMethod.Get, "api/admin/lookups/installations", null, cancellationToken) ?? [];
    public async Task<Timing.InstallationDTO> SaveInstallationAsync(Guid? id, Timing.SaveInstallationRequest request, CancellationToken cancellationToken = default) =>
        await client.SendAsync<Timing.InstallationDTO>(id.HasValue ? HttpMethod.Put : HttpMethod.Post, $"api/admin/timing/installations{Suffix(id)}", request, cancellationToken) ?? throw new HttpRequestException("Missing installation response.");
    public Task DeleteInstallationAsync(Guid id, CancellationToken cancellationToken = default) =>
        client.SendAsync<object>(HttpMethod.Delete, $"api/admin/timing/installations/{id}", null, cancellationToken);
    public async Task<Timing.TimingPointDTO> SaveTimingPointAsync(Guid installationId, Guid? id, Timing.SaveTimingPointRequest request, CancellationToken cancellationToken = default) =>
        await client.SendAsync<Timing.TimingPointDTO>(id.HasValue ? HttpMethod.Put : HttpMethod.Post, $"api/admin/timing/installations/{installationId}/points{Suffix(id)}", request, cancellationToken) ?? throw new HttpRequestException("Missing timing point response.");
    public Task DeleteTimingPointAsync(Guid installationId, Guid id, CancellationToken cancellationToken = default) =>
        client.SendAsync<object>(HttpMethod.Delete, $"api/admin/timing/installations/{installationId}/points/{id}", null, cancellationToken);
    private static string Suffix(Guid? id) => id.HasValue ? $"/{id}" : "";
}