using System.Net;
using System.Net.Http.Headers;
using VeloTime.Module.Facilities.Interface.Data;

namespace VeloTime.Module.Statistics.Api.Endpoints;

public interface ICourseLayoutReferences
{
    Task<CourseLayoutDTO?> GetLayoutAsync(Guid layoutId, CancellationToken cancellationToken);
}

internal sealed class CourseLayoutReferences(HttpClient client, IHttpContextAccessor accessor) : ICourseLayoutReferences
{
    public async Task<CourseLayoutDTO?> GetLayoutAsync(Guid layoutId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/facilities/layouts/{layoutId}");
        if (AuthenticationHeaderValue.TryParse(accessor.HttpContext?.Request.Headers.Authorization, out var authorization))
            request.Headers.Authorization = authorization;
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var layout = await response.Content.ReadFromJsonAsync<CourseLayoutDTO>(cancellationToken);
        if (layout is null || layout.Id != layoutId || layout.FacilityId == Guid.Empty || layout.Segments is null)
            throw new HttpRequestException("Facilities returned an invalid course layout.");
        return layout;
    }
}
