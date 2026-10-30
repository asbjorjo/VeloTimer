using System.Globalization;
using System.Net;
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
    public async Task<IReadOnlyList<BestStatisticsResultView>> GetBestResultsAsync(Guid? facilityId = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        if (facilityId == Guid.Empty || (from.HasValue && to.HasValue && from >= to))
            throw new HttpRequestException("Provide a nonempty facility ID and a time range with from before to.", null, HttpStatusCode.BadRequest);

        try
        {
            var results = await statistics.GetBestStatisticsResultsAsync(facilityId, from, to, cancellationToken);
            var names = new Dictionary<Guid, string>();
            foreach (var id in results.Select(result => result.FacilityId).Distinct())
            {
                var facility = await facitilies.Facility2Async(id, cancellationToken);
                if (facility is null || string.IsNullOrWhiteSpace(facility.Name))
                    throw new HttpRequestException("The result facility could not be resolved.", null, HttpStatusCode.BadGateway);
                names.Add(id, facility.Name);
            }

            return results.Select(result => new BestStatisticsResultView(
                result.Id, result.StatisticsItemName, result.ProfileName, names[result.FacilityId],
                TimeSpan.Parse(result.Duration, CultureInfo.InvariantCulture), result.Speed)).ToList();
        }
        catch (Module.Statistics.Client.ApiException exception)
        {
            throw new HttpRequestException("Statistics could not be loaded.", exception, (HttpStatusCode)exception.StatusCode);
        }
        catch (Module.Facilities.Client.ApiException exception)
        {
            throw new HttpRequestException("Result facilities could not be loaded.", exception, HttpStatusCode.BadGateway);
        }
    }

    public async Task<IEnumerable<SampleView>> GetSamplesAsync(DateTime? cursor, bool isNextPage, int pageSize, CancellationToken cancellationToken = default)
    {
        var samples = new List<SampleView>();
        var rawsamples = await statistics.SampleAsync(cursor, isNextPage, pageSize, cancellationToken);
        foreach (var sample in rawsamples.Samples)
        {
            samples.Add(new SampleView
            {
                Time = sample.Time.DateTime,
                TransponderId = sample.TransponderId,
                TransponderLabel = sample.TransponderId.ToString(),
                StartPoint = (await cache.GetOrCreateAsync(sample.CoursePointStartId, async (entry) => { return await facitilies.CoursepointAsync(sample.CoursePointStartId, cancellationToken); }))!.Name,
                EndPoint = (await cache.GetOrCreateAsync(sample.CoursePointEndId, async (entry) => { return await facitilies.CoursepointAsync(sample.CoursePointEndId, cancellationToken); }))!.Name,
                Distance = sample.Distance,
                Duration = TimeSpan.Parse(sample.Duration),
                Speed = sample.Speed
            });
        }

        return samples;
    }
}
