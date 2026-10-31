using System.Globalization;
using System.Net;
using Microsoft.Extensions.Caching.Memory;
using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Statistics.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;
using Contracts = VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.WebUI.Mud.Services;

public class StatisticsService(
    IStatisticsClient statistics,
    IFacilitiesClient facitilies,
    IMemoryCache cache) : IStatisticsService
{
    public Task<Contracts.PublicStatisticsDiscoveryDTO> GetPublicDiscoveryAsync(CancellationToken cancellationToken = default) =>
        PublicCall(async () =>
        {
            var response = await statistics.GetPublicStatisticsDiscoveryAsync(cancellationToken);
            return new Contracts.PublicStatisticsDiscoveryDTO(
                response.Items.Select(item => new Contracts.StatisticsItemDTO(
                    item.Id, item.Name, item.Description, item.Distance)).ToList(),
                response.Configurations.Select(config => new Contracts.PublicStatisticsConfigurationDTO(
                    config.Id, config.StatisticsItemId, config.FacilityId, (Contracts.StatisticsConfigurationType)config.Type,
                    config.CoursePointStart, config.CoursePointStartName, config.CoursePointEnd, config.CoursePointEndName,
                    config.ParentConfigId, config.Repetitions)).ToList(),
                response.UnavailableConfigurations.Select(option => new Contracts.UnavailablePublicStatisticsConfigurationDTO(
                    new Contracts.StatisticsConfigurationDTO(option.Configuration.Id, option.Configuration.StatisticsItemId,
                        (Contracts.StatisticsConfigurationType)option.Configuration.Type, option.Configuration.CoursePointStart,
                        option.Configuration.CoursePointEnd, option.Configuration.ParentConfigId, option.Configuration.Repetitions),
                    (Contracts.PublicStatisticsConfigurationUnavailableReason)option.Reason)).ToList());
        }, cancellationToken);

    public Task<Contracts.PublicStatisticsResultsPageDTO> GetPublicResultsAsync(
        Contracts.GetPublicStatisticsResultsRequest request, CancellationToken cancellationToken = default)
    {
        request = Contracts.PublicStatisticsQuery.Normalize(request);
        if (Contracts.PublicStatisticsQuery.Validate(request).Count != 0)
            throw new HttpRequestException("Invalid public statistics filters or cursor.", null, HttpStatusCode.BadRequest);
        var query = Contracts.PublicStatisticsQuery.ToQuery(request);
        string? Value(string key) => query.TryGetValue(key, out var value) ? value : null;
        return PublicCall(async () =>
        {
            var response = await statistics.GetPublicStatisticsResultsAsync(
                Value("facilityId"), Value("itemId"), Value("configurationId"), Value("name"),
                Value("minTime"), Value("maxTime"), Value("minSpeed"), Value("maxSpeed"),
                Value("cursor"), Value("direction"), cancellationToken);
            if (response.Items.Count > Contracts.PublicStatisticsQuery.PageSize)
                throw new HttpRequestException("The public statistics page exceeded its row limit.", null, HttpStatusCode.BadGateway);
            return new Contracts.PublicStatisticsResultsPageDTO(
                response.Items.Select(row => new Contracts.PublicStatisticsResultDTO(
                    row.Id, row.ProfileName, row.StatisticsItemId, row.StatisticsItemConfigId, row.FacilityId,
                    TimeSpan.Parse(row.Duration, CultureInfo.InvariantCulture), row.SpeedKmh, row.TimeEnd)).ToList(),
                MapCursor(response.Previous), MapCursor(response.Next));
        }, cancellationToken);
    }

    private static Contracts.PublicStatisticsCursorDTO? MapCursor(PublicStatisticsCursorDTO? cursor)
    {
        if (cursor is null) return null;
        var mapped = new Contracts.PublicStatisticsCursorDTO(cursor.TimeEnd, cursor.Id);
        // Validate without rounding/truncating the persisted PostgreSQL timestamp.
        _ = Contracts.PublicStatisticsQuery.FormatCursor(mapped);
        return mapped;
    }

    private static async Task<T> PublicCall<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await action();
        }
        catch (Module.Statistics.Client.ApiException exception)
        {
            var status = exception.StatusCode is 400 or 401 or 403 or 404 or 503
                ? (HttpStatusCode)exception.StatusCode : HttpStatusCode.BadGateway;
            throw new HttpRequestException(exception.StatusCode == 400
                ? "Select an available configuration and valid public statistics filters."
                : "Public statistics could not be loaded.", exception, status);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException("Public statistics is temporarily unavailable.", exception, HttpStatusCode.ServiceUnavailable);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or Newtonsoft.Json.JsonException)
        {
            throw new HttpRequestException("The public statistics response was invalid.", exception, HttpStatusCode.BadGateway);
        }
    }

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
