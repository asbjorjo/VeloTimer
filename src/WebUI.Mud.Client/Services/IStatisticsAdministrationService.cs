using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

public interface IStatisticsAdministrationService
{
    Task<StatisticsEntryContributionsDTO> GetEntryContributionsAsync(Guid entryId, CancellationToken cancellationToken = default);
    /// <summary>Resolve at most 1000 sample course-point IDs through the shared Facilities cache; failures throw.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetCoursePointNamesAsync(IReadOnlyList<Guid> coursePointIds, CancellationToken cancellationToken = default);
    Task<AdminStatisticsEntriesPageDTO> ListEntriesAsync(GetAdminStatisticsEntriesRequest request, CancellationToken cancellationToken = default);
    Task<AdminStatisticsEntryDTO> SetManualInvalidationAsync(Guid id, StatisticsEntryManualInvalidationRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StatisticsItemDTO>> GetItemsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StatisticsConfigurationDTO>> GetConfigurationsAsync(CancellationToken cancellationToken = default);
    Task SaveItemAsync(Guid? id, SaveStatisticsItemRequest request, CancellationToken cancellationToken = default);
    Task DeleteItemAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveConfigurationAsync(Guid? id, SaveStatisticsConfigurationRequest request, CancellationToken cancellationToken = default);
    Task DeleteConfigurationAsync(Guid id, CancellationToken cancellationToken = default);
}