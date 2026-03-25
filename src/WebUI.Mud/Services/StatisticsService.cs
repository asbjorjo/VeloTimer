using Microsoft.Extensions.Caching.Memory;
using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Statistics.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.WebUI.Mud.Services;

public class StatisticsService(
    IStatisticsClient statistics,
    IFacilitiesClient facitilies,
    IMemoryCache cache) : IStatisticsService
{
    public async Task<IEnumerable<SampleView>> GetSamplesAsync(DateTime? cursor, bool isNextPage, int pageSize)
    {
        var samples = new List<SampleView>();
        var rawsamples = await statistics.SampleAsync(cursor, isNextPage, pageSize);
        foreach (var sample in rawsamples.Samples)
        {
            samples.Add(new SampleView
            {
                Time = sample.Time.DateTime,
                TransponderLabel = sample.TransponderId.ToString(),
                StartPoint = (await cache.GetOrCreateAsync(sample.CoursePointStartId, async (entry) => { return await facitilies.CoursepointAsync(sample.CoursePointStartId); })).Name,
                EndPoint = (await cache.GetOrCreateAsync(sample.CoursePointEndId, async (entry) => { return await facitilies.CoursepointAsync(sample.CoursePointEndId); })).Name,
                Distance = sample.Distance,
                Duration = TimeSpan.Parse(sample.Duration),
                Speed = sample.Speed
            });
        }

        return samples;
    }
}
