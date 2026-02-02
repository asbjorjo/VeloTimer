namespace VeloTime.WebUI.Mud.Client.Services;

public interface IStatisticsService
{
    Task<IEnumerable<SampleView>> GetSamplesAsync(DateTime? cursor, bool isNextPage, int pageSize);
}
