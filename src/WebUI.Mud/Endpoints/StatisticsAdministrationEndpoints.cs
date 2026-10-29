using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.WebUI.Mud.Endpoints;

internal static class StatisticsAdministrationEndpoints
{
    internal static void MapStatisticsAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var statistics = endpoints.MapGroup("/admin/statistics").RequireAuthorization(AdministrationAuthorization.StatisticsPolicy);
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