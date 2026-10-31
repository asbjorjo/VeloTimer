using Microsoft.Extensions.Logging;
using SlimMessageBus;
using VeloTime.Agent.Interface.Messages.Control;
using VeloTime.Agent.Service;

namespace VeloTime.Agent.Handler;

public class PauseAgentHandler(IControlService control, ILogger<PauseAgentHandler> logger) : IConsumer<PauseAgentCommand>
{
    public async Task OnHandle(PauseAgentCommand message, CancellationToken cancellationToken)
    {
        logger.LogInformation("Pausing agent.");
        await control.PauseAgent();
    }
}
