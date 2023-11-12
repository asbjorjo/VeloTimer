using MediatR;
using VeloTimer.Shared.Data.Models.Timing;

public record RegisterPassingCommand(PassingRegister Passing) : IRequest;
