using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace VeloTime.Module.Statistics.Api.Endpoints;

public interface ICoursePointReferences
{
    Task<Guid?> GetFacilityAsync(Guid pointId, CancellationToken cancellationToken);
}

internal sealed class CoursePointReferences(HttpClient client, IHttpContextAccessor accessor) : ICoursePointReferences
{
    public async Task<Guid?> GetFacilityAsync(Guid pointId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/facilities/coursepoint/{pointId}/facility");
        if (AuthenticationHeaderValue.TryParse(accessor.HttpContext?.Request.Headers.Authorization, out var authorization))
        {
            request.Headers.Authorization = authorization;
        }

        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var facility = await response.Content.ReadFromJsonAsync<FacilityReference>(cancellationToken);
        if (facility is null || facility.Id == Guid.Empty)
            throw new HttpRequestException("Facilities returned an invalid facility reference.");
        return facility.Id;
    }

    private sealed record FacilityReference(Guid Id);
}