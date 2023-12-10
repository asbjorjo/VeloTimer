using VeloTime.Timing.Contracts;

namespace VeloTime.Timing.Services;

public interface IMessagingService
{
    public Task SendStartLoadingFrom(StartLoadingFrom startLoadingFrom);
    public Task SendTrackPassing(TrackPassing passing);
    public Task SendTrackPassings(IEnumerable<TrackPassing> passings);
}
