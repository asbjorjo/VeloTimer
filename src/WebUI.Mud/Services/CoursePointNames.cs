using System.Net;
using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Facilities.Interface;

namespace VeloTime.WebUI.Mud.Services;

/// <summary>Shared, relationship-tagged lookup; cache factories own their service scopes.</summary>
internal static class CoursePointNames
{
    internal static Task<string> GetAsync(WebUiCache cache, Guid coursePointId, CancellationToken cancellationToken) =>
        cache.GetPublicAsync(
            "course-point-name", "string-v1", coursePointId.ToString("N"),
            async (IFacilitiesClient factoryClient, CancellationToken token) =>
            {
                var point = await factoryClient.CoursepointAsync(coursePointId, cancellationToken: token);
                if (point is null || point.Id != coursePointId || string.IsNullOrWhiteSpace(point.Name))
                    throw new HttpRequestException("A course-point name could not be resolved.", null, HttpStatusCode.BadGateway);
                return point.Name;
            },
            [FacilitiesCacheTags.Relationships], cancellationToken);
}
