using MediatR;
using VeloTimer.Shared.Data.Models.Timing;

public record RegisterPassingCommand(IEnumerable<PassingRegister> Passings) : IRequest;
