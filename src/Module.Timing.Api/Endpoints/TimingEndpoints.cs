namespace VeloTime.Module.Timing.Api.Endpoints;

internal static class TimingEndpoints
{
    internal static void MapTimingEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        var timing = routeBuilder.MapGroup("/api/timing")
            .WithTags(["Timing"]);

        timing.MapInstallationEndpoints();
        timing.MapSampleEndpoints();

    }
}
