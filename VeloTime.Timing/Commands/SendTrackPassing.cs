using MediatR;
using VeloTime.Timing.Contracts;

namespace VeloTime.Timing.Commands;
public record SendTrackPassing(TrackPassing TrackPassing) : IRequest;
