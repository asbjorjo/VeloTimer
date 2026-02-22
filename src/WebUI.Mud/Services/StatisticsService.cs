using Microsoft.Extensions.Caching.Memory;
using VeloTime.Module.Facilities.Interface.Client;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Statistics.Interface.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.WebUI.Mud.Services;

public class StatisticsService(
    IStatisticsClient statistics,
    IFacitiliesClient facitilies,
    IMemoryCache cache) : IStatisticsService
{
    public async Task<IEnumerable<SampleView>> GetSamplesAsync(DateTime? cursor, bool isNextPage, int pageSize)
    {
        var samples = new List<SampleView>();
        var rawsamples = await statistics.GetSamplesAsync(cursor, isNextPage, pageSize);
        foreach (var sample in rawsamples.Samples)
        {
            samples.Add(new SampleView
            {
                Time = sample.Time,
                TransponderLabel = sample.TransponderId.ToString(),
                StartPoint = (await cache.GetOrCreateAsync(sample.CoursePointStartId, async (entry) => { return await facitilies.GetCoursePointById(sample.CoursePointStartId); })).Name,
                EndPoint = (await cache.GetOrCreateAsync(sample.CoursePointEndId, async (entry) => { return await facitilies.GetCoursePointById(sample.CoursePointEndId); })).Name,
                Distance = sample.Distance,
                Duration = sample.Duration,
                Speed = sample.Speed
            });
        }

        return samples;
    }
}
