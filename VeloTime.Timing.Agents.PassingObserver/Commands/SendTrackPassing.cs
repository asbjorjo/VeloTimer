using MediatR;
using VeloTime.Timing.Contracts;

namespace VeloTime.Timing.Agents.PassingObserver.Commands;
public record SendTrackPassing(TrackPassing TrackPassing) : IRequest;
