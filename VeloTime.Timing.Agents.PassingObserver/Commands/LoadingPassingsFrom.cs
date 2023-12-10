using MediatR;

namespace VeloTime.Timing.Agents.PassingObserver.Commands;
public record LoadingPassingsFrom(DateTimeOffset StartTime) : IRequest;