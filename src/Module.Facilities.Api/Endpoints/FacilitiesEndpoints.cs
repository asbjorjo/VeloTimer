namespace VeloTime.Module.Facilities.Api.Endpoints;

internal static class FacilitiesEndpoints
{
    internal static void MapFacilitiesEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        var facilities = routeBuilder.MapGroup("/api/facilities").WithTags(["Facilities"]);

        facilities.MapCourseLayoutEndpoints();
        facilities.MapCoursePointEndpoints();
        facilities.MapFacilityEndpoints();
    }
}
