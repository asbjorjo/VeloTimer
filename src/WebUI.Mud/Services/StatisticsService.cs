using System.Globalization;
using System.Net;
using VeloTime.Module.Facilities.Client;
using VeloTime.Module.Facilities.Interface;
using VeloTime.Module.Statistics.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;
using Contracts = VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.WebUI.Mud.Services;

public class StatisticsService(
    IStatisticsClient statistics,
    WebUiCache cache) : IStatisticsService
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

    public async Task<Contracts.HallOfFameDTO> GetHallOfFameAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await statistics.GetHallOfFameAsync(cancellationToken);
            return MapHallOfFame(response);
        }
        catch (Module.Statistics.Client.ApiException exception)
        {
            var status = exception.StatusCode is 500 or 502 or 503
                ? (HttpStatusCode)exception.StatusCode
                : HttpStatusCode.BadGateway;
            var message = exception.StatusCode == 500
                ? "Hall of Fame settings are invalid or stale. Contact a statistics administrator."
                : exception.StatusCode == 503
                    ? "The Hall of Fame is temporarily unavailable."
                    : "The Hall of Fame could not be loaded.";
            throw new HttpRequestException(message, exception, status);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException(
                "The Hall of Fame is temporarily unavailable.", exception, HttpStatusCode.ServiceUnavailable);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or Newtonsoft.Json.JsonException)
        {
            throw new HttpRequestException("The Hall of Fame response was invalid.", exception, HttpStatusCode.BadGateway);
        }
    }

    private static Contracts.HallOfFameDTO MapHallOfFame(Module.Statistics.Client.HallOfFameDTO response)
    {
        if (response?.Sections is null)
            throw new InvalidDataException("The Hall of Fame response is missing sections.");

        var sections = response.Sections.Select(section =>
        {
            if (section is null)
                throw new InvalidDataException("A Hall of Fame section is missing.");
            var mode = (Contracts.HallOfFameRankingMode)section.RankingMode;
            if (section.StatisticsItemId == Guid.Empty || string.IsNullOrWhiteSpace(section.StatisticsItemName) ||
                !Enum.IsDefined(mode) || section.RowCount is < Contracts.HallOfFameQuery.MinimumRowCount or > Contracts.HallOfFameQuery.MaximumRowCount ||
                section.Columns is null || section.Entries is null)
                throw new InvalidDataException("The Hall of Fame section response is invalid.");

            var columns = section.Columns.Select(value => (Contracts.HallOfFameColumn)value).ToArray();
            if (columns.Any(column => !Enum.IsDefined(column)) || columns.Distinct().Count() != columns.Length ||
                section.Entries.Count > section.RowCount)
                throw new InvalidDataException("The Hall of Fame section response has invalid columns or row count.");

            return new Contracts.HallOfFameSectionDTO(
                section.StatisticsItemId,
                section.StatisticsItemName,
                mode,
                section.RowCount,
                columns,
                section.Entries.Select(entry => MapHallOfFameEntry(entry, columns)).ToArray());
        }).ToArray();
        return new Contracts.HallOfFameDTO(sections);
    }

    private static Contracts.HallOfFameEntryDTO MapHallOfFameEntry(
        Module.Statistics.Client.HallOfFameEntryDTO entry,
        IReadOnlyCollection<Contracts.HallOfFameColumn> columns)
    {
        bool Includes(Contracts.HallOfFameColumn column) => columns.Contains(column);
        if (entry is null || entry.Rank <= 0 || string.IsNullOrWhiteSpace(entry.ProfileName))
            throw new InvalidDataException("The Hall of Fame entry response is invalid.");

        TimeSpan? bestTime = null;
        if (Includes(Contracts.HallOfFameColumn.BestTime))
        {
            if (string.IsNullOrWhiteSpace(entry.BestTime))
                throw new InvalidDataException("A configured best-time value was missing.");
            bestTime = TimeSpan.Parse(entry.BestTime, CultureInfo.InvariantCulture);
            if (bestTime < TimeSpan.Zero)
                throw new InvalidDataException("A Hall of Fame best time cannot be negative.");
        }
        else if (entry.BestTime is not null)
            throw new InvalidDataException("The Hall of Fame returned an unconfigured best-time value.");

        if (!Includes(Contracts.HallOfFameColumn.SpeedOfBestResult) && entry.SpeedOfBestResult is not null ||
            Includes(Contracts.HallOfFameColumn.SpeedOfBestResult) &&
            (entry.SpeedOfBestResult is not { } speed || !double.IsFinite(speed) || speed < 0))
            throw new InvalidDataException("The Hall of Fame speed value does not match the selected columns.");
        if (!Includes(Contracts.HallOfFameColumn.PassingCount) && entry.PassingCount is not null ||
            Includes(Contracts.HallOfFameColumn.PassingCount) && entry.PassingCount is not >= 0)
            throw new InvalidDataException("The Hall of Fame passing count does not match the selected columns.");
        if (!Includes(Contracts.HallOfFameColumn.FacilityOfBestResult) && entry.FacilityOfBestResult is not null ||
            Includes(Contracts.HallOfFameColumn.FacilityOfBestResult) && string.IsNullOrWhiteSpace(entry.FacilityOfBestResult))
            throw new InvalidDataException("The Hall of Fame facility value does not match the selected columns.");
        if (!Includes(Contracts.HallOfFameColumn.DateOfBestResult) && entry.DateOfBestResult is not null ||
            Includes(Contracts.HallOfFameColumn.DateOfBestResult) &&
            (entry.DateOfBestResult is not { } date || date.Offset != TimeSpan.Zero))
            throw new InvalidDataException("The Hall of Fame date value does not match the selected columns or is not UTC.");

        return new(
            entry.Rank,
            entry.ProfileName,
            bestTime,
            entry.SpeedOfBestResult,
            entry.PassingCount,
            entry.FacilityOfBestResult,
            entry.DateOfBestResult);
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
                names.Add(id, await GetFacilityNameAsync(id, cancellationToken));
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
                StartPoint = await GetCoursePointNameAsync(sample.CoursePointStartId, cancellationToken),
                EndPoint = await GetCoursePointNameAsync(sample.CoursePointEndId, cancellationToken),
                Distance = sample.Distance,
                Duration = TimeSpan.Parse(sample.Duration),
                Speed = sample.Speed
            });
        }

        return samples;
    }

    private Task<string> GetCoursePointNameAsync(Guid coursePointId, CancellationToken cancellationToken) =>
        cache.GetPublicAsync(
            "course-point-name", "string-v1", coursePointId.ToString("N"),
            async (IFacilitiesClient factoryClient, CancellationToken token) =>
            {
                var point = await factoryClient.CoursepointAsync(coursePointId, cancellationToken: token);
                if (point is null || point.Id != coursePointId || string.IsNullOrWhiteSpace(point.Name))
                    throw new HttpRequestException("A course-point name could not be resolved.", null, HttpStatusCode.BadGateway);
                return point.Name;
            },
            [FacilitiesCacheTags.Relationships], cancellationToken);

    private Task<string> GetFacilityNameAsync(Guid facilityId, CancellationToken cancellationToken) =>
        cache.GetPublicAsync(
            "facility-name", "string-v1", facilityId.ToString("N"),
            async (IFacilitiesClient factoryClient, CancellationToken token) =>
            {
                var facility = await factoryClient.Facility2Async(facilityId, token);
                if (facility is null || facility.Id != facilityId || string.IsNullOrWhiteSpace(facility.Name))
                    throw new HttpRequestException("The result facility could not be resolved.", null, HttpStatusCode.BadGateway);
                return facility.Name;
            },
            [FacilitiesCacheTags.Relationships], cancellationToken);
}
