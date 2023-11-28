using MediatR;
using VeloTimer.PassingLoader.Contracts;

namespace VeloTimer.PassingLoader.Commands;
public record RegisterTrackPassing(TrackPassingObserved TrackPassing) : IRequest;
