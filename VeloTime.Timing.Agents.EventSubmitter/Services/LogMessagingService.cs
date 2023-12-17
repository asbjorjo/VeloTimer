using Microsoft.Extensions.Logging;
using VeloTime.Timing.Contracts;

namespace VeloTime.Timing.Agents.EventSubmitter.Services;

public class LogMessagingService : IExternalMessagingService
{
    private ILogger<LogMessagingService> _logger;

    public LogMessagingService(ILogger<LogMessagingService> logger) => _logger = logger;

    public Task SendStartLoadingFrom(StartLoadingFrom startLoadingFrom)
    {
        throw new NotImplementedException();
    }

    public Task SendTrackPassing(TrackPassing passing)
    {
        _logger.LogInformation(passing.ToString());

        return Task.CompletedTask;
    }

    public async Task SendTrackPassings(IEnumerable<TrackPassing> passings)
    {
        _logger.LogInformation($"Received {passings.Count()}");

        foreach (var passing in passings)
        {
            await SendTrackPassing(passing);
        }

        _logger.LogInformation("Finished");
    }
}
