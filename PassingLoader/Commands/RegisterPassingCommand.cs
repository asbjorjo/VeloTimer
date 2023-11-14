using MediatR;
using VeloTimer.PassingLoader.Contracts;

namespace VeloTimer.PassingLoader.Commands;
public record RegisterPassingCommand(PassingObserved Passing) : IRequest;
