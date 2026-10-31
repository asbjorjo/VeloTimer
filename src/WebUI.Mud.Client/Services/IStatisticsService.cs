using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

public interface IStatisticsService
{
    // Contract-first gate: explicit failure until both transports implement these methods.
    // Default bodies preserve existing implementers without returning success-shaped empty data.
    Task<PublicStatisticsDiscoveryDTO> GetPublicDiscoveryAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Public statistics discovery transport is not implemented yet.");

    Task<PublicStatisticsResultsPageDTO> GetPublicResultsAsync(GetPublicStatisticsResultsRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Public statistics results transport is not implemented yet.");

    Task<IReadOnlyList<BestStatisticsResultView>> GetBestResultsAsync(Guid? facilityId = null, DateTimeOffset? from = null,
        DateTimeOffset? to = null, CancellationToken cancellationToken = default);

    Task<IEnumerable<SampleView>> GetSamplesAsync(DateTime? cursor, bool isNextPage, int pageSize, CancellationToken cancellationToken = default);
}
