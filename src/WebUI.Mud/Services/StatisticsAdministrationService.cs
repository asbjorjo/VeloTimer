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

    public Task<Contracts.HallOfFameSettingsDTO> GetHallOfFameSettingsAsync(CancellationToken cancellationToken = default) =>
        Execute(async () =>
        {
            await cache.RequirePolicyAsync(AdministrationAuthorization.StatisticsPolicy, cancellationToken);
            return MapHallOfFameSettings(
                await client.GetHallOfFameSettingsAsync(cancellationToken));
        });

    public Task<Contracts.HallOfFameSettingsDTO> SaveHallOfFameSettingsAsync(
        Contracts.SaveHallOfFameSettingsRequest request, CancellationToken cancellationToken = default) =>
        Execute(async () =>
        {
            await cache.RequirePolicyAsync(AdministrationAuthorization.StatisticsPolicy, cancellationToken);
            var validationErrors = Contracts.HallOfFameQuery.Validate(request);
            if (request.ExpectedVersion == long.MaxValue)
                validationErrors["expectedVersion"] = ["The settings version cannot be incremented further."];
            if (validationErrors.Count != 0)
                throw new HttpRequestException(
                    string.Join(" ", validationErrors.SelectMany(error =>
                        error.Value.Select(message => $"{error.Key}: {message}"))),
                    null, HttpStatusCode.BadRequest);

            var body = new SaveHallOfFameSettingsRequest
            {
                ExpectedVersion = request.ExpectedVersion,
                Sections = request.Sections.Select(section => new HallOfFameSectionSettingsDTO
                {
                    StatisticsItemId = section.StatisticsItemId,
                    RankingMode = (int)section.RankingMode,
                    IsEnabled = section.IsEnabled,
                    RowCount = section.RowCount,
                    Columns = section.Columns.Select(column => (int)column).ToArray()
                }).ToArray()
            };
            var saved = MapHallOfFameSettings(await client.ReplaceHallOfFameSettingsAsync(body, cancellationToken));
            if (saved.Version != checked(request.ExpectedVersion + 1) ||
                !SectionsMatch(saved.Sections, request.Sections))
                throw new InvalidDataException("The saved Hall of Fame settings response did not match the request.");
            return saved;
        });

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
        await AdministrationClientCall.Execute(async () =>
        {
            await client.DeleteStatisticsItemAsync(id, cancellationToken);
            return true;
        });
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

    private static Contracts.HallOfFameSettingsDTO MapHallOfFameSettings(HallOfFameSettingsDTO settings)
    {
        if (settings is null || settings.Version < 0 || settings.Sections is null)
            throw new InvalidDataException("The Hall of Fame settings response is invalid.");

        var sections = settings.Sections.Select(section =>
        {
            if (section is null)
                throw new InvalidDataException("A Hall of Fame settings section is missing.");
            var mode = (Contracts.HallOfFameRankingMode)section.RankingMode;
            if (section.StatisticsItemId == Guid.Empty || !Enum.IsDefined(mode) ||
                section.Columns is null)
                throw new InvalidDataException("A Hall of Fame settings section is invalid.");
            var columns = section.Columns.Select(value => (Contracts.HallOfFameColumn)value).ToArray();
            if (columns.Any(column => !Enum.IsDefined(column)))
                throw new InvalidDataException("Hall of Fame settings contain an unknown column.");
            return new Contracts.HallOfFameSectionSettingsDTO(
                section.StatisticsItemId, mode, section.IsEnabled, section.RowCount, columns);
        }).ToArray();
        var mapped = new Contracts.HallOfFameSettingsDTO(settings.Version, sections);
        if (Contracts.HallOfFameQuery.Validate(
                new Contracts.SaveHallOfFameSettingsRequest(settings.Version, mapped.Sections)).Count != 0)
            throw new InvalidDataException("Hall of Fame settings failed structural validation.");
        return mapped;
    }

    private static bool SectionsMatch(
        IReadOnlyList<Contracts.HallOfFameSectionSettingsDTO> saved,
        IReadOnlyList<Contracts.HallOfFameSectionSettingsDTO> requested) =>
        saved.Count == requested.Count && saved.Zip(requested).All(pair =>
            pair.First.StatisticsItemId == pair.Second.StatisticsItemId &&
            pair.First.RankingMode == pair.Second.RankingMode &&
            pair.First.IsEnabled == pair.Second.IsEnabled &&
            pair.First.RowCount == pair.Second.RowCount &&
            pair.First.Columns.SequenceEqual(pair.Second.Columns));

    private static async Task<TResult> Execute<TResult>(Func<Task<TResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ApiException exception) when (exception.StatusCode is 400 or 401 or 403 or 404 or 409 or 422 or 500)
        {
            var detail = "The request could not be completed.";
            try
            {
                using var document = JsonDocument.Parse(exception.Response);
                if (document.RootElement.TryGetProperty("detail", out var property) && property.ValueKind == JsonValueKind.String)
                    detail = property.GetString() ?? detail;
                else if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                    detail = string.Join(" ", errors.EnumerateObject().SelectMany(error =>
                        error.Value.ValueKind == JsonValueKind.Array
                            ? error.Value.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                                .Select(value => $"{error.Name}: {value.GetString()}")
                            : []));
            }
            catch (JsonException)
            {
            }
            throw new HttpRequestException(detail, exception, (HttpStatusCode)exception.StatusCode);
        }
    }
}