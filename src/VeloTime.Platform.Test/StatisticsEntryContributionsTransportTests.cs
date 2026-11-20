using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Client;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Timing.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Services;
using StatsApi = VeloTime.Module.Statistics.Client;
using TimingApi = VeloTime.Module.Timing.Client;
using FacilitiesApi = VeloTime.Module.Facilities.Client;

namespace VeloTime.Platform.Test;

public sealed class StatisticsEntryContributionsTransportTests
{
    [Fact]
    public async Task BffUsesStatisticsIdentityAndExactUtcWindowAndDoesNotCacheContributionPayloads()
    {
        var entryId = Guid.NewGuid();
        var transponderId = Guid.NewGuid();
        var from = new DateTimeOffset(2026, 11, 1, 2, 3, 4, TimeSpan.Zero).AddTicks(1234567);
        var to = from.AddDays(2).AddTicks(9876543);
        var sampleId = Guid.NewGuid();
        var passingId = Guid.NewGuid();
        var startPoint = Guid.NewGuid();
        var endPoint = Guid.NewGuid();
        var statisticsCalls = 0;
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((method, args) =>
        {
            Assert.Equal("GetAdminStatisticsEntrySamplesAsync", method.Name);
            Assert.Equal(entryId, args[0]);
            statisticsCalls++;
            return Samples(entryId, transponderId, from, to,
                Sample(sampleId, from, to, startPoint, endPoint, double.NaN));
        });
        var timingCalls = 0;
        var timing = PlatformServiceTests.TestProxy.Create<ITimingClient>((method, args) =>
        {
            Assert.Equal("GetTransponderPassingsAsync", method.Name);
            Assert.Equal(transponderId, args[0]);
            Assert.Equal(from.ToString("O", CultureInfo.InvariantCulture), args[1]);
            Assert.Equal(to.ToString("O", CultureInfo.InvariantCulture), args[2]);
            timingCalls++;
            return Passings(transponderId, from, to, new TimingApi.PassingDTO
            {
                Id = passingId, Time = from, TimingPointDescription = "Finish", InstallationDescription = "Finish stand",
                TimingPointId = Guid.NewGuid(), InstallationId = Guid.NewGuid(), FacilityId = Guid.NewGuid(),
                LowBattery = true, LowStrength = false, LowHits = true
            }, truncated: true);
        });
        using var host = new WebUiCacheTestHost(statistics, timing);
        var service = new StatisticsAdministrationService(statistics, host.Cache, timing,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StatisticsAdministrationService>.Instance);
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nb-NO");
            var first = await service.GetEntryContributionsAsync(entryId);
            var second = await service.GetEntryContributionsAsync(entryId);
            Assert.Equal(2, statisticsCalls);
            Assert.Equal(2, timingCalls);
            Assert.Equal(entryId, first.EntryId);
            Assert.Equal(transponderId, first.TransponderId);
            Assert.Equal(from, first.TimeStart);
            Assert.Equal(to, first.TimeEnd);
            Assert.Equal(TimeSpan.Zero, first.TimeStart.Offset);
            var sample = Assert.Single(first.Samples);
            Assert.Equal(TimeSpan.FromTicks(1234560), sample.Duration);
            Assert.Null(sample.SpeedMetresPerSecond);
            Assert.Equal(125.5, sample.DistanceMetres);
            Assert.True(first.SamplesTruncated);
            Assert.Equal(StatisticsEntryPassingsStatus.Available, first.PassingsStatus);
            Assert.True(first.PassingsTruncated);
            var passing = Assert.Single(first.Passings!);
            Assert.Equal("Finish", passing.TimingPointDescription);
            Assert.Equal("Finish stand", passing.InstallationDescription);
            Assert.True(passing.LowBattery);
            Assert.False(passing.LowStrength);
            Assert.True(passing.LowHits);
            Assert.Equal(first.EntryId, second.EntryId);
            Assert.Equal(first.TimeStart, second.TimeStart);
            Assert.Equal(sample.Id, Assert.Single(second.Samples).Id);
            Assert.Equal(passing.Id, Assert.Single(second.Passings!).Id);
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    [Theory]
    [InlineData("forbidden", StatisticsEntryPassingsStatus.Forbidden)]
    [InlineData("unavailable", StatisticsEntryPassingsStatus.Unavailable)]
    [InlineData("malformed", StatisticsEntryPassingsStatus.Unavailable)]
    public async Task TimingFailuresKeepSamplesExplicitAndNeverFabricateEmptyPassings(string failure, StatisticsEntryPassingsStatus expected)
    {
        var entryId = Guid.NewGuid();
        var transponderId = Guid.NewGuid();
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddHours(1);
        var statistics = StatsClient(_ => Samples(entryId, transponderId, from, to, Sample(Guid.NewGuid(), from, to, Guid.NewGuid(), Guid.NewGuid())));
        var timing = PlatformServiceTests.TestProxy.Create<ITimingClient>((_, _) => failure switch
        {
            "forbidden" => throw new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden),
            "unavailable" => throw new HttpRequestException("Upstream unavailable"),
            _ => Passings(Guid.NewGuid(), from, to)
        });
        using var host = new WebUiCacheTestHost(statistics, timing);
        var service = new StatisticsAdministrationService(statistics, host.Cache, timing,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StatisticsAdministrationService>.Instance);
        var result = await service.GetEntryContributionsAsync(entryId);
        Assert.Single(result.Samples);
        Assert.Equal(expected, result.PassingsStatus);
        Assert.Null(result.Passings);
        Assert.False(result.PassingsTruncated);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("broken-circuit")]
    [InlineData("non-caller-cancellation")]
    public async Task TimingResilienceFailuresKeepSamplesMarkPassingsUnavailableAndLog(string failureKind)
    {
        var entryId = Guid.NewGuid();
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        Exception failure = failureKind switch
        {
            "timeout" => new Polly.Timeout.TimeoutRejectedException("Timing timed out"),
            "broken-circuit" => new Polly.CircuitBreaker.BrokenCircuitException("Timing circuit is open"),
            _ => new OperationCanceledException("Timing canceled outside the caller")
        };
        var statistics = StatsClient(_ => Samples(entryId, Guid.NewGuid(), from, from.AddHours(1),
            Sample(Guid.NewGuid(), from, from.AddMinutes(1), Guid.NewGuid(), Guid.NewGuid())));
        var timing = PlatformServiceTests.TestProxy.Create<ITimingClient>((_, args) =>
        {
            Assert.False(((CancellationToken)args[^1]!).IsCancellationRequested);
            throw failure;
        });
        using var host = new WebUiCacheTestHost(statistics, timing);
        var logger = new RecordingLogger<StatisticsAdministrationService>();
        var service = new StatisticsAdministrationService(statistics, host.Cache, timing, logger);

        var result = await service.GetEntryContributionsAsync(entryId);

        Assert.Equal(StatisticsEntryPassingsStatus.Unavailable, result.PassingsStatus);
        Assert.Null(result.Passings);
        Assert.False(result.PassingsTruncated);
        Assert.Single(result.Samples);
        var log = Assert.Single(logger.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, log.Level);
        Assert.Same(failure, log.Exception);
    }

    [Fact]
    public async Task CallerCancellationDuringTimingRequestPropagatesWithoutBeingMappedOrLogged()
    {
        var entryId = Guid.NewGuid();
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var statistics = StatsClient(_ => Samples(entryId, Guid.NewGuid(), from, from.AddHours(1)));
        using var cancellation = new CancellationTokenSource();
        var timing = PlatformServiceTests.TestProxy.Create<ITimingClient>((_, args) =>
        {
            var token = (CancellationToken)args[^1]!;
            Assert.Equal(cancellation.Token, token);
            cancellation.Cancel();
            throw new OperationCanceledException(token);
        });
        using var host = new WebUiCacheTestHost(statistics, timing);
        var logger = new RecordingLogger<StatisticsAdministrationService>();
        var service = new StatisticsAdministrationService(statistics, host.Cache, timing, logger);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetEntryContributionsAsync(entryId, cancellation.Token));

        Assert.Empty(logger.Entries);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task TimingClientUnauthorizedForbiddenAndNotFoundStatusesKeepTheirExplicitSemantics(int status)
    {
        var entryId = Guid.NewGuid();
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var statistics = StatsClient(_ => Samples(entryId, Guid.NewGuid(), from, from.AddHours(1),
            Sample(Guid.NewGuid(), from, from.AddMinutes(1), Guid.NewGuid(), Guid.NewGuid())));
        var timing = PlatformServiceTests.TestProxy.Create<ITimingClient>((_, _) =>
            throw new TimingApi.ApiException("Timing response", status, "", new Dictionary<string, IEnumerable<string>>(), null!));
        using var host = new WebUiCacheTestHost(statistics, timing);
        var service = new StatisticsAdministrationService(statistics, host.Cache, timing,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StatisticsAdministrationService>.Instance);

        if (status == 403)
        {
            var result = await service.GetEntryContributionsAsync(entryId);
            Assert.Equal(StatisticsEntryPassingsStatus.Forbidden, result.PassingsStatus);
            Assert.Null(result.Passings);
            Assert.Single(result.Samples);
        }
        else
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetEntryContributionsAsync(entryId));
            Assert.Equal((HttpStatusCode)status, error.StatusCode);
        }
    }

    [Fact]
    public async Task OverlongStatisticsWindowIsExplicitAndShortCircuitsTiming()
    {
        var entryId = Guid.NewGuid();
        var transponderId = Guid.NewGuid();
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var statistics = StatsClient(_ => Samples(entryId, transponderId, from, from.AddDays(7).AddTicks(1),
            Sample(Guid.NewGuid(), from, from.AddMinutes(1), Guid.NewGuid(), Guid.NewGuid())));
        var timing = PlatformServiceTests.TestProxy.Create<ITimingClient>((_, _) => throw new InvalidOperationException("must not call Timing"));
        using var host = new WebUiCacheTestHost(statistics, timing);
        var result = await new StatisticsAdministrationService(statistics, host.Cache, timing,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StatisticsAdministrationService>.Instance).GetEntryContributionsAsync(entryId);
        Assert.Single(result.Samples);
        Assert.Equal(StatisticsEntryPassingsStatus.WindowTooLarge, result.PassingsStatus);
        Assert.Null(result.Passings);
        Assert.Empty(((PlatformServiceTests.TestProxy)(object)timing).Calls);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    public async Task StatisticsExplicitErrorsRemainErrorsAndDoNotCallTiming(int status)
    {
        var entryId = Guid.NewGuid();
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((_, _) =>
            throw new StatsApi.ApiException("upstream response", status, "{\"detail\":\"entry missing or invalid\"}", new Dictionary<string, IEnumerable<string>>(), null!));
        var timing = PlatformServiceTests.TestProxy.Create<ITimingClient>((_, _) => throw new InvalidOperationException("must not call Timing"));
        using var host = new WebUiCacheTestHost(statistics, timing);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new StatisticsAdministrationService(statistics, host.Cache, timing,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StatisticsAdministrationService>.Instance).GetEntryContributionsAsync(entryId));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.Contains("entry missing or invalid", error.Message);
        Assert.Empty(((PlatformServiceTests.TestProxy)(object)timing).Calls);
    }

    [Theory]
    [InlineData(401, HttpStatusCode.Unauthorized)]
    [InlineData(404, HttpStatusCode.NotFound)]
    public async Task TimingUnauthorizedAndNotFoundRemainExplicitErrors(int status, HttpStatusCode expected)
    {
        var entryId = Guid.NewGuid();
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var statistics = StatsClient(_ => Samples(entryId, Guid.NewGuid(), from, from.AddHours(1)));
        var timing = PlatformServiceTests.TestProxy.Create<ITimingClient>((_, _) =>
            throw new HttpRequestException("downstream rejected", null, (HttpStatusCode)status));
        using var host = new WebUiCacheTestHost(statistics, timing);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new StatisticsAdministrationService(statistics, host.Cache, timing,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StatisticsAdministrationService>.Instance).GetEntryContributionsAsync(entryId));
        Assert.Equal(expected, error.StatusCode);
    }

    [Fact]
    public async Task ExactlySevenDayWindowCallsTimingAndAvailableEmptyRemainsGenuineEmpty()
    {
        var entryId = Guid.NewGuid();
        var transponder = Guid.NewGuid();
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(1234567);
        var to = from.AddDays(7);
        var statistics = StatsClient(_ => Samples(entryId, transponder, from, to));
        var timing = PlatformServiceTests.TestProxy.Create<ITimingClient>((method, args) =>
        {
            Assert.Equal("GetTransponderPassingsAsync", method.Name);
            Assert.Equal(transponder, args[0]);
            Assert.Equal(from.ToString("O", CultureInfo.InvariantCulture), args[1]);
            Assert.Equal(to.ToString("O", CultureInfo.InvariantCulture), args[2]);
            return Passings(transponder, from, to);
        });
        using var host = new WebUiCacheTestHost(statistics, timing);
        var result = await new StatisticsAdministrationService(statistics, host.Cache, timing,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StatisticsAdministrationService>.Instance).GetEntryContributionsAsync(entryId);
        Assert.Equal(StatisticsEntryPassingsStatus.Available, result.PassingsStatus);
        Assert.NotNull(result.Passings);
        Assert.Empty(result.Passings);
        Assert.Single(((PlatformServiceTests.TestProxy)(object)timing).Calls);
    }

    [Fact]
    public async Task CoursePointNamesAreCachedButInvalidPayloadDoesNotBecomeANameOrAStaleCacheEntry()
    {
        var id = Guid.NewGuid();
        var calls = 0;
        var facilities = PlatformServiceTests.TestProxy.Create<FacilitiesApi.IFacilitiesClient>((method, args) =>
        {
            Assert.Equal("CoursepointAsync", method.Name);
            Assert.Equal(id, args[0]);
            calls++;
            return calls == 1 ? null : new FacilitiesApi.CoursePointDTO { Id = id, Name = "North finish" };
        });
        var statistics = PlatformServiceTests.TestProxy.Create<IStatisticsClient>((_, _) => throw new NotSupportedException());
        var timing = PlatformServiceTests.TestProxy.Create<ITimingClient>((_, _) => throw new NotSupportedException());
        using var host = new WebUiCacheTestHost(statistics, timing, facilities);
        var service = new StatisticsAdministrationService(statistics, host.Cache, timing,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StatisticsAdministrationService>.Instance);
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetCoursePointNamesAsync([id]));
        Assert.Equal(HttpStatusCode.BadGateway, failure.StatusCode);
        var names = await service.GetCoursePointNamesAsync([id, id]);
        Assert.Equal("North finish", names[id]);
        Assert.Equal(2, calls);
        Assert.Equal("North finish", (await service.GetCoursePointNamesAsync([id]))[id]);
        Assert.Equal(2, calls);
    }

    private static IStatisticsClient StatsClient(Func<Guid, StatsApi.AdminStatisticsEntrySamplesDTO> get) =>
        PlatformServiceTests.TestProxy.Create<IStatisticsClient>((method, args) =>
        {
            Assert.Equal("GetAdminStatisticsEntrySamplesAsync", method.Name);
            return get((Guid)args[0]!);
        });

    private static StatsApi.AdminStatisticsEntrySamplesDTO Samples(Guid entryId, Guid transponderId,
        DateTimeOffset from, DateTimeOffset to, params StatsApi.AdminStatisticsSampleDTO[] samples) => new()
    {
        EntryId = entryId, TransponderId = transponderId, TimeStart = from, TimeEnd = to,
        Samples = samples, Truncated = true
    };

    private static StatsApi.AdminStatisticsSampleDTO Sample(Guid id, DateTimeOffset from, DateTimeOffset to,
        Guid startPoint, Guid endPoint, double? speed = 9.25) => new()
    {
        Id = id, TimeStart = from, TimeEnd = to, CoursePointStartId = startPoint, CoursePointEndId = endPoint,
        DistanceMetres = 125.5, Duration = TimeSpan.FromTicks(1234560).ToString("c", CultureInfo.InvariantCulture),
        SpeedMetresPerSecond = speed, FacilityId = Guid.NewGuid()
    };

    private static TimingApi.TransponderPassingsDTO Passings(Guid transponder, DateTimeOffset from,
        DateTimeOffset to, TimingApi.PassingDTO? passing = null, bool truncated = false) => new()
    {
        TransponderId = transponder, From = from, To = to,
        Passings = passing is null ? [] : [passing], Truncated = truncated
    };

    private sealed class RecordingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) where TState : notnull =>
            Entries.Add((logLevel, exception));
    }
}

public sealed partial class WebUiBffTests
{
    [Fact]
    public async Task EntryContributionsBffRequiresStatisticsAdminAndForwardsTheContributionRoute()
    {
        var backend = PlatformServiceTests.TestProxy.Create<IStatisticsAdministrationService>((method, args) =>
        {
            Assert.Equal(nameof(IStatisticsAdministrationService.GetEntryContributionsAsync), method.Name);
            return new StatisticsEntryContributionsDTO((Guid)args[0]!, Guid.NewGuid(), DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch, [], false, null, false, StatisticsEntryPassingsStatus.Unavailable);
        });
        await using var app = await CreateAppAsync(administration: backend);
        using var client = CreateClient(app);
        var id = Guid.NewGuid();
        await client.GetAsync("signin?role=statistics-admin");
        using var response = await client.GetAsync($"api/admin/statistics/entries/{id:D}/contributions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var result = await response.Content.ReadFromJsonAsync<StatisticsEntryContributionsDTO>();
        Assert.Equal(id, result!.EntryId);
        Assert.Single(((PlatformServiceTests.TestProxy)(object)backend).Calls);
        await client.GetAsync("signin?role=user");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"api/admin/statistics/entries/{id:D}/contributions")).StatusCode);
        Assert.Single(((PlatformServiceTests.TestProxy)(object)backend).Calls);
    }
}
