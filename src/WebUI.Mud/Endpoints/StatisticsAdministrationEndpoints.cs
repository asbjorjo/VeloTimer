using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.WebUI.Mud.Endpoints;

internal static class StatisticsAdministrationEndpoints
{
    internal static void MapStatisticsAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var statistics = endpoints.MapGroup("/admin/statistics").RequireAuthorization(AdministrationAuthorization.StatisticsPolicy);
        statistics.MapGet("/entries/{id:guid}/contributions", async (Guid id,
            IStatisticsAdministrationService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetEntryContributionsAsync(id, cancellationToken)));
        statistics.MapPost("/course-point-names/resolve", async (Guid[] coursePointIds,
            IStatisticsAdministrationService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetCoursePointNamesAsync(coursePointIds, cancellationToken)));
        statistics.MapGet("/entries", async (HttpContext http, IStatisticsAdministrationService service, CancellationToken cancellationToken) =>
        {
            var duplicates = http.Request.Query.Where(pair => pair.Value.Count != 1)
                .ToDictionary(pair => pair.Key, _ => new[] { "Supply each parameter exactly once." });
            if (duplicates.Count != 0) return Results.ValidationProblem(duplicates);
            var values = http.Request.Query.ToDictionary(pair => pair.Key, pair => (string?)pair.Value[0]);
            if (!StatisticsEntriesQuery.TryParse(values, out var request, out var errors)) return Results.ValidationProblem(errors);
            return Results.Ok(await service.ListEntriesAsync(request, cancellationToken));
        });
        statistics.MapPut("/entries/{id:guid}/manual-invalidation", async (Guid id, StatisticsEntryManualInvalidationRequest request,
            IStatisticsAdministrationService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.SetManualInvalidationAsync(id, request, cancellationToken)));
        statistics.MapGet("/recompute", async (HttpContext http, IStatisticsAdministrationService service, CancellationToken cancellationToken) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await service.GetRecomputeOverviewAsync(cancellationToken));
        });
        statistics.MapPost("/recompute/jobs", async (CreateRecomputeJobRequest request, IStatisticsAdministrationService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.CreateRecomputeJobAsync(request, cancellationToken)));
        statistics.MapGet("/items", async (IStatisticsAdministrationService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetItemsAsync(cancellationToken)));
        statistics.MapGet("/configurations", async (IStatisticsAdministrationService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetConfigurationsAsync(cancellationToken)));
        statistics.MapPost("/items", async (SaveStatisticsItemRequest request, IStatisticsAdministrationService service, CancellationToken cancellationToken) =>
        {
            await service.SaveItemAsync(null, request, cancellationToken);
            return Results.NoContent();
        });
        statistics.MapPut("/items/{id:guid}", async (Guid id, SaveStatisticsItemRequest request, IStatisticsAdministrationService service, CancellationToken cancellationToken) =>
        {
            await service.SaveItemAsync(id, request, cancellationToken);
            return Results.NoContent();
        });
        statistics.MapDelete("/items/{id:guid}", async (Guid id, IStatisticsAdministrationService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteItemAsync(id, cancellationToken);
            return Results.NoContent();
        });
        statistics.MapPost("/configurations", async (SaveStatisticsConfigurationRequest request, IStatisticsAdministrationService service, CancellationToken cancellationToken) =>
        {
            await service.SaveConfigurationAsync(null, request, cancellationToken);
            return Results.NoContent();
        });
        statistics.MapPut("/configurations/{id:guid}", async (Guid id, SaveStatisticsConfigurationRequest request, IStatisticsAdministrationService service, CancellationToken cancellationToken) =>
        {
            await service.SaveConfigurationAsync(id, request, cancellationToken);
            return Results.NoContent();
        });
        statistics.MapDelete("/configurations/{id:guid}", async (Guid id, IStatisticsAdministrationService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteConfigurationAsync(id, cancellationToken);
            return Results.NoContent();
        });
    }
}