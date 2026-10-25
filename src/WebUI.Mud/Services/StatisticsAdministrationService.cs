using System.Net;
using System.Text.Json;
using VeloTime.Module.Statistics.Client;
using VeloTime.WebUI.Mud.Client.Services;
using Contracts = VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.WebUI.Mud.Services;

public sealed class StatisticsAdministrationService(IStatisticsClient client) : IStatisticsAdministrationService
{
    public Task<IReadOnlyList<Contracts.StatisticsItemDTO>> GetItemsAsync(CancellationToken cancellationToken = default) =>
        Execute<IReadOnlyList<Contracts.StatisticsItemDTO>>(async () => (await client.ListStatisticsItemsAsync(cancellationToken))
            .Select(item => new Contracts.StatisticsItemDTO(item.Id, item.Name, item.Description, item.Distance)).ToList());

    public Task<IReadOnlyList<Contracts.StatisticsConfigurationDTO>> GetConfigurationsAsync(CancellationToken cancellationToken = default) =>
        Execute<IReadOnlyList<Contracts.StatisticsConfigurationDTO>>(async () => (await client.ListStatisticsConfigurationsAsync(null, cancellationToken))
            .Select(config => new Contracts.StatisticsConfigurationDTO(config.Id, config.StatisticsItemId, (Contracts.StatisticsConfigurationType)config.Type,
                config.CoursePointStart, config.CoursePointEnd, config.ParentConfigId, config.Repetitions)).ToList());

    public Task SaveItemAsync(Guid? id, Contracts.SaveStatisticsItemRequest request, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        var body = new SaveStatisticsItemRequest { Name = request.Name, Description = request.Description, Distance = request.Distance };
        if (id.HasValue) await client.UpdateStatisticsItemAsync(id.Value, body, cancellationToken);
        else await client.CreateStatisticsItemAsync(body, cancellationToken);
        return true;
    });

    public Task DeleteItemAsync(Guid id, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await client.DeleteStatisticsItemAsync(id, cancellationToken);
        return true;
    });

    public Task SaveConfigurationAsync(Guid? id, Contracts.SaveStatisticsConfigurationRequest request, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        var body = new SaveStatisticsConfigurationRequest
        {
            StatisticsItemId = request.StatisticsItemId, Type = (int)request.Type, CoursePointStart = request.CoursePointStart,
            CoursePointEnd = request.CoursePointEnd, ParentConfigId = request.ParentConfigId, Repetitions = request.Repetitions
        };
        if (id.HasValue) await client.UpdateStatisticsConfigurationAsync(id.Value, body, cancellationToken);
        else await client.CreateStatisticsConfigurationAsync(body, cancellationToken);
        return true;
    });

    public Task DeleteConfigurationAsync(Guid id, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await client.DeleteStatisticsConfigurationAsync(id, cancellationToken);
        return true;
    });

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