using MediatR;
using VeloTimer.PassingLoader.Contracts;

namespace VeloTimer.PassingLoader.Commands;
public record SendTrackPassing(TrackPassingObserved TrackPassing) : IRequest;
