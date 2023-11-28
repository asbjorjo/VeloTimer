using MediatR;
using VeloTimer.PassingLoader.Contracts;

namespace VeloTimer.PassingLoader.Commands;
public record ProcessRawPassing(RawPassingObserved Passing) : IRequest;
