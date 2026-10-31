using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

public interface IStatisticsService
{
    Task<PublicStatisticsDiscoveryDTO> GetPublicDiscoveryAsync(CancellationToken cancellationToken = default);

    Task<PublicStatisticsResultsPageDTO> GetPublicResultsAsync(GetPublicStatisticsResultsRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BestStatisticsResultView>> GetBestResultsAsync(Guid? facilityId = null, DateTimeOffset? from = null,
        DateTimeOffset? to = null, CancellationToken cancellationToken = default);

    Task<IEnumerable<SampleView>> GetSamplesAsync(DateTime? cursor, bool isNextPage, int pageSize, CancellationToken cancellationToken = default);
}
