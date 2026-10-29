namespace VeloTime.WebUI.Mud.Client.Services;

public interface IStatisticsService
{
    Task<IReadOnlyList<BestStatisticsResultView>> GetBestResultsAsync(Guid? facilityId = null, DateTimeOffset? from = null,
        DateTimeOffset? to = null, CancellationToken cancellationToken = default);

    Task<IEnumerable<SampleView>> GetSamplesAsync(DateTime? cursor, bool isNextPage, int pageSize, CancellationToken cancellationToken = default);
}
