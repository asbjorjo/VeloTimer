using System.Net;
using System.Text.Json;
using VeloTime.Module.Statistics.Client;
using VeloTime.Module.Common;
using VeloTime.WebUI.Mud.Client.Services;
using Contracts = VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.WebUI.Mud.Services;

public sealed class StatisticsAdministrationService(IStatisticsClient client, WebUiCache cache) : IStatisticsAdministrationService
{
    private const string DefinitionsTag = "velotime:webui:statistics-definitions";

    public Task<IReadOnlyList<Contracts.StatisticsItemDTO>> GetItemsAsync(CancellationToken cancellationToken = default) =>
        GetItemsCoreAsync(cancellationToken);

    public Task<IReadOnlyList<Contracts.StatisticsConfigurationDTO>> GetConfigurationsAsync(CancellationToken cancellationToken = default) =>
        GetConfigurationsCoreAsync(cancellationToken);

    private async Task<IReadOnlyList<Contracts.StatisticsItemDTO>> GetItemsCoreAsync(CancellationToken cancellationToken)
    {
        var snapshots = await cache.GetAdminAsync(
            AdministrationAuthorization.StatisticsPolicy, "statistics-admin-items", "statistics-item-v1",
            (IStatisticsClient factoryClient, CancellationToken token) => Execute(async () =>
                (await factoryClient.ListStatisticsItemsAsync(token)
                    ?? throw new InvalidDataException("The statistics item list is missing."))
                .Select(item => Map(item)).ToArray()),
            [DefinitionsTag], cancellationToken);
        return snapshots.ToArray();
    }

    private async Task<IReadOnlyList<Contracts.StatisticsConfigurationDTO>> GetConfigurationsCoreAsync(CancellationToken cancellationToken)
    {
        var snapshots = await cache.GetAdminAsync(
            AdministrationAuthorization.StatisticsPolicy, "statistics-admin-configurations", "all-configurations-v1",
            (IStatisticsClient factoryClient, CancellationToken token) => Execute(async () =>
                (await factoryClient.ListStatisticsConfigurationsAsync(null, token)
                    ?? throw new InvalidDataException("The statistics configuration list is missing."))
                .Select(config => Map(config)).ToArray()),
            [DefinitionsTag], cancellationToken);
        return snapshots.ToArray();
    }

    public Task SaveItemAsync(Guid? id, Contracts.SaveStatisticsItemRequest request, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await cache.RequirePolicyAsync(AdministrationAuthorization.StatisticsPolicy, cancellationToken);
        var body = new SaveStatisticsItemRequest { Name = request.Name, Description = request.Description, Distance = request.Distance };
        var response = await cache.WriteAndInvalidateAsync(
            () => id.HasValue ? client.UpdateStatisticsItemAsync(id.Value, body, cancellationToken) : client.CreateStatisticsItemAsync(body, cancellationToken),
            DefinitionsTag, "saving a statistics item");
        _ = Map(response, id);
        return true;
    });

    public Task DeleteItemAsync(Guid id, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await cache.RequirePolicyAsync(AdministrationAuthorization.StatisticsPolicy, cancellationToken);
        await AdministrationClientCall.ExecuteNoContent(() => client.DeleteStatisticsItemAsync(id, cancellationToken));
        await cache.InvalidateTagAsync(DefinitionsTag, "deleting a statistics item");
        return true;
    });

    public Task SaveConfigurationAsync(Guid? id, Contracts.SaveStatisticsConfigurationRequest request, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await cache.RequirePolicyAsync(AdministrationAuthorization.StatisticsPolicy, cancellationToken);
        var body = new SaveStatisticsConfigurationRequest
        {
            StatisticsItemId = request.StatisticsItemId, Type = (int)request.Type, CoursePointStart = request.CoursePointStart,
            CoursePointEnd = request.CoursePointEnd, ParentConfigId = request.ParentConfigId, Repetitions = request.Repetitions
        };
        var response = await cache.WriteAndInvalidateAsync(
            () => id.HasValue ? client.UpdateStatisticsConfigurationAsync(id.Value, body, cancellationToken) : client.CreateStatisticsConfigurationAsync(body, cancellationToken),
            DefinitionsTag, "saving a statistics configuration");
        var saved = Map(response, id);
        if (saved.StatisticsItemId != request.StatisticsItemId)
            throw new InvalidDataException("The saved configuration has an unexpected statistics item ID.");
        return true;
    });

    public Task DeleteConfigurationAsync(Guid id, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await cache.RequirePolicyAsync(AdministrationAuthorization.StatisticsPolicy, cancellationToken);
        await AdministrationClientCall.ExecuteNoContent(() => client.DeleteStatisticsConfigurationAsync(id, cancellationToken));
        await cache.InvalidateTagAsync(DefinitionsTag, "deleting a statistics configuration");
        return true;
    });

    private static Contracts.StatisticsItemDTO Map(StatisticsItemDTO item, Guid? expectedId = null)
    {
        if (item is null || item.Id == Guid.Empty || (expectedId.HasValue && item.Id != expectedId.Value) ||
            string.IsNullOrWhiteSpace(item.Name) || !double.IsFinite(item.Distance) || item.Distance <= 0)
            throw new InvalidDataException("The statistics item response is invalid.");
        return new(item.Id, item.Name, item.Description, item.Distance);
    }

    private static Contracts.StatisticsConfigurationDTO Map(StatisticsConfigurationDTO config, Guid? expectedId = null)
    {
        if (config is null || config.Id == Guid.Empty || (expectedId.HasValue && config.Id != expectedId.Value) ||
            config.StatisticsItemId == Guid.Empty || config.CoursePointStart == Guid.Empty ||
            config.CoursePointEnd == Guid.Empty || config.ParentConfigId == Guid.Empty ||
            !Enum.IsDefined((Contracts.StatisticsConfigurationType)config.Type))
            throw new InvalidDataException("The statistics configuration response is invalid.");
        if (((Contracts.StatisticsConfigurationType)config.Type == Contracts.StatisticsConfigurationType.Simple &&
             (config.CoursePointStart is null || config.CoursePointEnd is null ||
              config.ParentConfigId is not null || config.Repetitions is not null)) ||
            ((Contracts.StatisticsConfigurationType)config.Type == Contracts.StatisticsConfigurationType.Multi &&
             (config.CoursePointStart is not null || config.CoursePointEnd is not null ||
              config.ParentConfigId is null || config.Repetitions is null or <= 0)))
            throw new InvalidDataException("The statistics configuration response has an invalid shape.");
        return new(config.Id, config.StatisticsItemId, (Contracts.StatisticsConfigurationType)config.Type,
            config.CoursePointStart, config.CoursePointEnd, config.ParentConfigId, config.Repetitions);
    }

    private static async Task<TResult> Execute<TResult>(Func<Task<TResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ApiException exception) when (exception.StatusCode is 400 or 401 or 403 or 404 or 409 or 422)
        {
            var detail = "The request could not be completed.";
            try
            {
                using var document = JsonDocument.Parse(exception.Response);
                if (document.RootElement.TryGetProperty("detail", out var property) && property.ValueKind == JsonValueKind.String)
                    detail = property.GetString() ?? detail;
            }
            catch (JsonException)
            {
            }
            throw new HttpRequestException(detail, exception, (HttpStatusCode)exception.StatusCode);
        }
    }
}