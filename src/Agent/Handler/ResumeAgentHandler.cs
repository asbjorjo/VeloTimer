using Microsoft.Extensions.Logging;
using SlimMessageBus;
using VeloTime.Agent.Interface.Messages.Control;
using VeloTime.Agent.Service;

namespace VeloTime.Agent.Handler;

public class ResumeAgentHandler(IControlService control, ILogger<ResumeAgentHandler> logger) : IConsumer<ResumeAgentCommand>
{
    public async Task OnHandle(ResumeAgentCommand message, CancellationToken cancellationToken)
    {
        logger.LogInformation("Resuming agent.");
        await control.ResumeAgent();
    }
}
