using VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.Module.Statistics.Interface.Client
{
    public interface IStatisticsClient
    {
        Task<GetSamplesResponse> GetSamplesAsync(DateTime? cursor, bool isNextPage, int pageSize);
    }
}