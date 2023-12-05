using VeloTime.Timing.Contracts;

namespace VeloTime.Timing.Services;

public interface IMessagingService
{
    public Task SendTrackPassing(TrackPassing passing);
    public Task SendTrackPassings(IEnumerable<TrackPassing> passings);
}
