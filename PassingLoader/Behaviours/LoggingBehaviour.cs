using MediatR;
using Microsoft.Extensions.Logging;

namespace VeloTimer.PassingLoader.Behaviours;

public class LoggingBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehaviour<TRequest, TResponse>> _logger;

    public LoggingBehaviour(ILogger<LoggingBehaviour<TRequest, TResponse>> logger) => _logger = logger;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        _logger.LogDebug($"Handling {typeof(TRequest).Name}");

        var response = await next();

        _logger.LogDebug($"Handled {typeof(TRequest).Name}");

        return response;
    }
}
