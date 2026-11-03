using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

public interface IStatisticsAdministrationService
{
    Task<IReadOnlyList<StatisticsItemDTO>> GetItemsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StatisticsConfigurationDTO>> GetConfigurationsAsync(CancellationToken cancellationToken = default);
    Task SaveItemAsync(Guid? id, SaveStatisticsItemRequest request, CancellationToken cancellationToken = default);
    Task DeleteItemAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveConfigurationAsync(Guid? id, SaveStatisticsConfigurationRequest request, CancellationToken cancellationToken = default);
    Task DeleteConfigurationAsync(Guid id, CancellationToken cancellationToken = default);

    Task<HallOfFameSettingsDTO> GetHallOfFameSettingsAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Hall of Fame settings transport is not implemented yet.");

    Task<HallOfFameSettingsDTO> SaveHallOfFameSettingsAsync(
        SaveHallOfFameSettingsRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Hall of Fame settings transport is not implemented yet.");
}