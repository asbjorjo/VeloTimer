namespace VeloTime.Module.Timing.Api.Endpoints;

internal static class TimingEndpoints
{
    internal static void MapTimingEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        var timing = routeBuilder.MapGroup("/api/timing")
            .WithTags(["Timing"]);

        timing.MapInstallationEndpoints();
        timing.MapAgentEdnpoints();
        timing.MapSampleEndpoints();
        timing.MapTransponderEdnpoints();
        timing.MapTransponderStatusEndpoints();
        timing.MapOwnershipEndpoints();
        timing.MapTimingAdministrationEndpoints();
    }
}
