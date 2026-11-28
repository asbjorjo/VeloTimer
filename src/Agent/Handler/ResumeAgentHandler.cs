using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SlimMessageBus;
using VeloTime.Agent.Capabilities;
using VeloTime.Agent.Interface.Messages.Control;
using VeloTime.Agent.Service;

namespace VeloTime.Agent.Handler;

/// <summary>
/// Calls the agent's <see cref="IControlService"/>. An agent without one does not advertise pause and resume, so the
/// platform should not send the command; if it arrives anyway it is logged and acknowledged.
/// </summary>
public class ResumeAgentHandler(IServiceProvider services, IOptions<AgentOptions> options, ILogger<ResumeAgentHandler> logger) : IConsumer<ResumeAgentCommand>
{
    public async Task OnHandle(ResumeAgentCommand message, CancellationToken cancellationToken)
    {
        if (!ControlCommandGuard.IsForThisAgent(message.AgentId, options.Value, logger, nameof(ResumeAgentCommand)))
            return;
        var control = services.GetService<IControlService>();
        if (control is null)
        {
            logger.LogWarning("Ignoring {Command}: this agent has no control service.", nameof(ResumeAgentCommand));
            return;
        }
        logger.LogInformation("Resuming agent.");
        await control.ResumeAgent();
    }
}
