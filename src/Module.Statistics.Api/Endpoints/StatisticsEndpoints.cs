namespace VeloTime.Module.Statistics.Api.Endpoints;

internal static class StatisticsEndpoints
{
    internal static void MapStatisticsEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        var statistics = routeBuilder.MapGroup("/api/statistics").WithTags(["Statistics"]);

        statistics.MapSampleEndpoints();
        statistics.MapBestStatisticsResultsEndpoints();
        statistics.MapHallOfFameEndpoints();
        statistics.MapPublicStatisticsEndpoints();
        statistics.MapStatsProfileEndpoints();
        statistics.MapOwnerProfileEndpoints();
        statistics.MapStatisticsDefinitionEndpoints();
        statistics.MapStatisticsReferenceEndpoints();
    }
}
